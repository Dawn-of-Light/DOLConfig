using System;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

using DOL.Database.Attributes;
using DOL.Database.UniqueID;

namespace DOL.Database
{
	public abstract class SQLObjectDatabase : ObjectDatabase
	{
		private static readonly object Lock = new object();

		protected DbConfig Config { get; set; }
		protected virtual string PreCommandDirectives => "";

		protected SQLObjectDatabase(string ConnectionString)
			: base(ConnectionString)
		{
			
		}
		
		#region ObjectDatabase Base Implementation for SQL
		public override void RegisterDataObject(Type dataObjectType)
		{
			var tableName = AttributesUtils.GetTableName(dataObjectType);
			
			DataTableHandler existingHandler;
			if (TableDatasets.TryGetValue(tableName, out existingHandler))
			{
				if (dataObjectType != existingHandler.ObjectType)
					throw new DatabaseException(string.Format("Table Handler Duplicate for Type: {2}, Table Name '{0}' Already Registered with Type : {1}", tableName, existingHandler.ObjectType, dataObjectType));
				
				return;
			}
			
			var dataTableHandler = new DataTableHandler(dataObjectType);

			try
			{
				
				CheckOrCreateTableImpl(dataTableHandler);

			    lock (Lock)
			    {
			        TableDatasets.Add(tableName, dataTableHandler);
                }
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("RegisterDataObject: Error While Registering Table \"{0}\"\n{1}", tableName, e);
			}
		}
		#endregion
		
		#region ObjectDatabase Objects Implementations
		protected override IEnumerable<bool> AddObjectImpl(DataTableHandler tableHandler, IEnumerable<DataObject> dataObjects)
		{
			var success = new List<bool>();
			if (!dataObjects.Any())
				return success;
			
			try
			{
				// Check Primary Keys
				var usePrimaryAutoInc = tableHandler.FieldElementBindings.Any(bind => bind.PrimaryKey != null && bind.PrimaryKey.AutoIncrement);
				
				// Columns
				var columns = tableHandler.FieldElementBindings.Where(bind => bind.PrimaryKey == null || !bind.PrimaryKey.AutoIncrement)
					.Select(bind => new { Binding = bind, ColumnName = string.Format("`{0}`", bind.ColumnName), ParamName = string.Format("@{0}", bind.ColumnName) }).ToArray();
				
				// Prepare SQL Query
				var command = string.Format("INSERT INTO `{0}` ({1}) VALUES({2})", tableHandler.TableName,
				                            string.Join(", ", columns.Select(col => col.ColumnName)),
				                            string.Join(", ", columns.Select(col => col.ParamName)));
				
				var objs = dataObjects.ToArray();
				
				// Init Object Id GUID
				foreach (var obj in objs.Where(obj => obj.ObjectId == null))
					obj.ObjectId = IDGenerator.GenerateID();
				
				// Build Parameters
				var parameters = objs.Select(obj => columns.Select(col => new QueryParameter(col.ParamName, col.Binding.GetValue(obj), col.Binding.ValueType)));
				
				// Primary Key Auto Inc Handler
				if (usePrimaryAutoInc)
				{
					var lastId = ExecuteScalarImpl(command, parameters, true);
					
					var binding = tableHandler.FieldElementBindings.First(bind => bind.PrimaryKey != null && bind.PrimaryKey.AutoIncrement);
					var resultByObjects = lastId.Select((result, index) => new { Result = Convert.ToInt64(result), DataObject = objs[index] });
					
					foreach (var result in resultByObjects)
					{
						if (result.Result > 0)
						{
							DatabaseSetValue(result.DataObject, binding, result.Result);
							result.DataObject.ObjectId = result.Result.ToString();
							result.DataObject.Dirty = false;
							result.DataObject.IsPersisted = true;
							result.DataObject.IsDeleted = false;
							success.Add(true);
						}
						else
						{
							if (log.IsErrorEnabled)
								log.ErrorFormat("Error adding data object into {0} Object = {1}, UsePrimaryAutoInc, Query = {2}", tableHandler.TableName, result.DataObject, command);
							
							success.Add(false);
						}
					}

				}
				else
				{
					var affected = ExecuteNonQueryImpl(command, parameters);
					var resultByObjects = affected.Select((result, index) => new { Result = result, DataObject = objs[index] });
					
					foreach (var result in resultByObjects)
					{
						if (result.Result > 0)
						{
							result.DataObject.Dirty = false;
							result.DataObject.IsPersisted = true;
							result.DataObject.IsDeleted = false;
							success.Add(true);
						}
						else
						{
							if (log.IsErrorEnabled)
								log.ErrorFormat("Error adding data object into {0} Object = {1} Query = {2}", tableHandler.TableName, result.DataObject, command);
							
							success.Add(false);
						}
					}
				}
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("Error while adding data objects in table: {0}\n{1}", tableHandler.TableName, e);
			}

			return success;
		}

		protected override IEnumerable<bool> SaveObjectImpl(DataTableHandler tableHandler, IEnumerable<DataObject> dataObjects)
		{
			var success = new List<bool>();
			if (!dataObjects.Any())
				return success;
			
			try
			{
				// Columns Filtering out ReadOnly
				var columns = tableHandler.FieldElementBindings.Where(bind => bind.PrimaryKey == null && bind.ReadOnly == null)
					.Select(bind => new { Binding = bind, ColumnName = string.Format("`{0}`", bind.ColumnName), ParamName = string.Format("@{0}", bind.ColumnName) }).ToArray();
				// Primary Key
				var primary = tableHandler.FieldElementBindings.Where(bind => bind.PrimaryKey != null)
					.Select(bind => new { Binding = bind, ColumnName = string.Format("`{0}`", bind.ColumnName), ParamName = string.Format("@{0}", bind.ColumnName) }).ToArray();
				
				if (!primary.Any())
					throw new DatabaseException(string.Format("Table {0} has no primary key for saving...", tableHandler.TableName));
				
				var command = string.Format("UPDATE `{0}` SET {1} WHERE {2}", tableHandler.TableName,
				                            string.Join(", ", columns.Select(col => string.Format("{0} = {1}", col.ColumnName, col.ParamName))),
				                            string.Join(" AND ", primary.Select(col => string.Format("{0} = {1}", col.ColumnName, col.ParamName))));
				
				var objs = dataObjects.ToArray();
				var parameters = objs.Select(obj => columns.Concat(primary).Select(col => new QueryParameter(col.ParamName, col.Binding.GetValue(obj), col.Binding.ValueType)));
				
				var affected = ExecuteNonQueryImpl(command, parameters);
				var resultByObjects = affected.Select((result, index) => new { Result = result, DataObject = objs[index] });
				
				foreach (var result in resultByObjects)
				{
					if (result.Result > 0)
					{
						result.DataObject.Dirty = false;
						result.DataObject.IsPersisted = true;
						success.Add(true);
					}
					else
					{
						if (log.IsErrorEnabled)
						{
							if (result.Result < 0)
								log.ErrorFormat("Error saving data object in table {0} Object = {1} --- constraint failed? {2}", tableHandler.TableName, result.DataObject, command);
							else
								log.ErrorFormat("Error saving data object in table {0} Object = {1} --- keyvalue changed? {2}\n{3}", tableHandler.TableName, result.DataObject, command, Environment.StackTrace);
						}
						success.Add(false);
					}
				}
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("Error while saving data object in table: {0}\n{1}", tableHandler.TableName, e);
			}

			return success;
		}
		#endregion
		
		#region ObjectDatabase Select Implementation
		protected override IList<IList<DataObject>> MultipleSelectObjectsImpl(DataTableHandler tableHandler, IEnumerable<WhereClause> whereClauseBatch)
		{
			var columns = tableHandler.FieldElementBindings.ToArray();

			string selectFromExpression = string.Format("SELECT {0} FROM `{1}` ",
										string.Join(", ", columns.Select(col => string.Format("`{0}`", col.ColumnName))),
										tableHandler.TableName);

			var primary = columns.FirstOrDefault(col => col.PrimaryKey != null);
			var dataObjects = new List<IList<DataObject>>();

			ExecuteSelectImpl(selectFromExpression, whereClauseBatch, reader => FillQueryResultList(reader, tableHandler, columns, primary, dataObjects));

			return dataObjects.ToArray();
		}

        private void FillQueryResultList(IDataReader reader, DataTableHandler tableHandler, ElementBinding[] columns, ElementBinding primary, List<IList<DataObject>> resultList)
		{
            var list = new List<DataObject>();

            var data = new object[reader.FieldCount];
            while (reader.Read())
            {
                reader.GetValues(data);
                var obj = Activator.CreateInstance(tableHandler.ObjectType) as DataObject;

                // Fill Object
                var current = 0;
                foreach (var column in columns)
                {
                    DatabaseSetValue(obj, column, data[current]);
                    current++;
                }

                // Set Primary Key
                if (primary != null)
                    obj.ObjectId = primary.GetValue(obj).ToString();

                list.Add(obj);
                obj.Dirty = false;
                obj.IsPersisted = true;
            }
            resultList.Add(list.ToArray());
        }

        /// <summary>
        /// Set Value to DataObject Field according to ElementBinding
        /// </summary>
        /// <param name="obj">DataObject to Fill</param>
        /// <param name="bind">ElementBinding for the targeted Member</param>
        /// <param name="value">Object Value to Fill</param>
        protected virtual void DatabaseSetValue(DataObject obj, ElementBinding bind, object value)
		{
			if (value == null || value.GetType().IsInstanceOfType(DBNull.Value))
				return;
			
			try
			{
				if (bind.ValueType == typeof(bool))
					bind.SetValue(obj, Convert.ToBoolean(value));
				else if (bind.ValueType == typeof(char))
					bind.SetValue(obj, Convert.ToChar(value));
				else if (bind.ValueType == typeof(sbyte))
					bind.SetValue(obj, Convert.ToSByte(value));
				else if (bind.ValueType == typeof(short))
					bind.SetValue(obj, Convert.ToInt16(value));
				else if (bind.ValueType == typeof(int))
					bind.SetValue(obj, Convert.ToInt32(value));
				else if (bind.ValueType == typeof(long))
					bind.SetValue(obj, Convert.ToInt64(value));
				else if (bind.ValueType == typeof(byte))
					bind.SetValue(obj, Convert.ToByte(value));
				else if (bind.ValueType == typeof(ushort))
					bind.SetValue(obj, Convert.ToUInt16(value));
				else if (bind.ValueType == typeof(uint))
					bind.SetValue(obj, Convert.ToUInt32(value));
				else if (bind.ValueType == typeof(ulong))
					bind.SetValue(obj, Convert.ToUInt64(value));
				else if (bind.ValueType == typeof(DateTime))
					bind.SetValue(obj, Convert.ToDateTime(value));
				else if (bind.ValueType == typeof(float))
					bind.SetValue(obj, Convert.ToSingle(value));
				else if (bind.ValueType == typeof(double))
					bind.SetValue(obj, Convert.ToDouble(value));
				else if (bind.ValueType == typeof(string))
					bind.SetValue(obj, Convert.ToString(value));
				else
					bind.SetValue(obj, value);
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("{0}: {1} = {2} doesnt fit to {3}\n{4}", obj.TableName, bind.ColumnName, value.GetType().FullName, bind.ValueType, e);
			}
		}
		
		/// <summary>
		/// Fill SQL Command Parameter with Converted Values.
		/// </summary>
		/// <param name="parameter">Parameter collection for this Command</param>
		/// <param name="dbParams">DbParameter Object to Fill</param>
		protected virtual void FillSQLParameter(IEnumerable<QueryParameter> parameter, DbParameterCollection dbParams)
		{
			dbParams.Clear();
			foreach(var param in parameter)
    		{
				dbParams.Add(ConvertToDBParameter(param));
    		}
		}

		protected abstract DbParameter ConvertToDBParameter(QueryParameter queryParameter);
		#endregion

		#region Table Implementation
		/// <summary>
		/// Check for Table Existence, Create or Alter accordingly
		/// </summary>
		/// <param name="table">Table Handler</param>
		public abstract void CheckOrCreateTableImpl(DataTableHandler table);
		#endregion

		protected virtual void ExecuteSelectImpl(string SQLCommand, IEnumerable<IEnumerable<QueryParameter>> parameters, Action<IDataReader> Reader)
		{
			if (log.IsDebugEnabled)
				log.DebugFormat("ExecuteSelectImpl: {0}", SQLCommand);

			bool repeat;
			var current = 0;
			do
			{
				repeat = false;

				if (!parameters.Any()) throw new ArgumentException("No parameter list was given.");

				using (var conn = CreateConnection())
				{
					using (var cmd = conn.CreateCommand())
					{
						try
						{
							conn.Open();
							long start = (DateTime.UtcNow.Ticks / 10000);

							foreach (var parameter in parameters.Skip(current))
							{
								cmd.CommandText = SQLCommand;
								FillSQLParameter(parameter, cmd.Parameters);
								cmd.Prepare();

								using (var reader = cmd.ExecuteReader())
								{
									try
									{
										Reader(reader);
									}
									catch (Exception es)
									{
										if (log.IsWarnEnabled)
											log.WarnFormat("ExecuteSelectImpl: Exception in Select Callback : {2}{0}{2}{1}", es, Environment.StackTrace, Environment.NewLine);
									}
									finally
									{
										reader.Close();
									}
								}
								current++;
							}

							if (log.IsDebugEnabled)
								log.DebugFormat("ExecuteSelectImpl: SQL Select exec time {0}ms", ((DateTime.UtcNow.Ticks / 10000) - start));
							else if (log.IsWarnEnabled && (DateTime.UtcNow.Ticks / 10000) - start > 500)
								log.WarnFormat("ExecuteSelectImpl: SQL Select took {0}ms!\n{1}", ((DateTime.UtcNow.Ticks / 10000) - start), SQLCommand);

						}
						catch (Exception e)
						{
							if (!HandleException(e))
							{
								if (log.IsErrorEnabled)
									log.ErrorFormat("ExecuteSelectImpl: UnHandled Exception for Select Query \"{0}\"\n{1}", SQLCommand, e);

								throw;
							}
							repeat = true;
						}
						finally
						{
							CloseConnection(conn);
						}
					}
				}
			}
			while (repeat);
		}

		protected virtual void ExecuteSelectImpl(string selectFromExpression, IEnumerable<WhereClause> whereClauseBatch, Action<IDataReader> Reader)
		{
			if (!whereClauseBatch.Any()) throw new ArgumentException("No parameter list was given.");

			if (log.IsDebugEnabled)
				log.DebugFormat("ExecuteSelectImpl: {0}", selectFromExpression);

			bool repeat;
			var current = 0;
			do
			{
				repeat = false;

				using (var conn = CreateConnection())
				{
					using (var cmd = conn.CreateCommand())
					{
						try
						{
							conn.Open();
							long start = (DateTime.UtcNow.Ticks / 10000);

							foreach (var whereClause in whereClauseBatch.Skip(current))
							{
								cmd.CommandText = selectFromExpression + whereClause.ParameterizedText;
								FillSQLParameter(whereClause.Parameters, cmd.Parameters);

								using (var reader = cmd.ExecuteReader())
								{
									try
									{
										Reader(reader);
									}
									catch (Exception es)
									{
										if (log.IsWarnEnabled)
											log.WarnFormat("ExecuteSelectImpl: Exception in Select Callback : {2}{0}{2}{1}", es, Environment.StackTrace, Environment.NewLine);
									}
									finally
									{
										reader.Close();
									}
								}
								current++;
							}

							if (log.IsDebugEnabled)
								log.DebugFormat("ExecuteSelectImpl: SQL Select exec time {0}ms", ((DateTime.UtcNow.Ticks / 10000) - start));
							else if (log.IsWarnEnabled && (DateTime.UtcNow.Ticks / 10000) - start > 500)
								log.WarnFormat("ExecuteSelectImpl: SQL Select took {0}ms!\n{1}", ((DateTime.UtcNow.Ticks / 10000) - start), selectFromExpression);

						}
						catch (Exception e)
						{
							if (!HandleException(e))
							{
								if (log.IsErrorEnabled)
									log.ErrorFormat("ExecuteSelectImpl: UnHandled Exception in Select Query \"{0}\"\n{1}", selectFromExpression, e);

								throw;
							}
							repeat = true;
						}
						finally
						{
							CloseConnection(conn);
						}
					}
				}
			}
			while (repeat);
		}

		public abstract DbConnection CreateConnection();

		protected abstract void CloseConnection(DbConnection connection);

		#region Non Query Implementation
		
		/// <summary>
		/// Implementation of Raw Non-Query
		/// </summary>
		/// <param name="SQLCommand">Raw Command</param>
		protected int ExecuteNonQueryImpl(string SQLCommand)
		{
			return ExecuteNonQueryImpl(SQLCommand, new [] { Array.Empty<QueryParameter>() } ).First();
		}
		
		/// <summary>
		/// Implementation of Raw Non-Query with Parameters for Prepared Query
		/// </summary>
		/// <param name="SQLCommand">Raw Command</param>
		/// <param name="parameters">Collection of Parameters for Single/Multiple Command</param>
		/// <returns>True foreach Command that succeeded</returns>
		protected virtual IEnumerable<int> ExecuteNonQueryImpl(string SQLCommand, IEnumerable<IEnumerable<QueryParameter>> parameters)
		{
			if (log.IsDebugEnabled)
				log.DebugFormat("ExecuteNonQueryImpl: {0}", SQLCommand);

			var affected = new List<int>();
			bool repeat;
			var current = 0;
			do
			{
				repeat = false;

				if (!parameters.Any()) throw new ArgumentException("No parameter list was given.");

				using (var conn = CreateConnection())
				{
					using (var cmd = conn.CreateCommand())
					{
						try
						{
							cmd.CommandText = $"{PreCommandDirectives}{SQLCommand}";
							conn.Open();
							long start = (DateTime.UtcNow.Ticks / 10000);

							foreach (var parameter in parameters.Skip(current))
							{
								FillSQLParameter(parameter, cmd.Parameters);
								cmd.Prepare();

								var result = -1;
								try
								{
									result = cmd.ExecuteNonQuery();
									affected.Add(result);
								}
								catch (Exception ex)
								{
									if (HandleSQLException(ex))
									{
										affected.Add(result);
										if (log.IsErrorEnabled)
											log.ErrorFormat("ExecuteNonQueryImpl: Constraint Violation for raw query \"{0}\"\n{1}\n{2}", SQLCommand, ex, Environment.StackTrace);
									}
									else
									{
										throw;
									}
								}
								current++;

								if (log.IsDebugEnabled && result < 1)
									log.DebugFormat("ExecuteNonQueryImpl: No Change for raw query \"{0}\"", SQLCommand);
							}

							if (log.IsDebugEnabled)
								log.DebugFormat("ExecuteNonQueryImpl: SQL NonQuery exec time {0}ms", ((DateTime.UtcNow.Ticks / 10000) - start));
							else if (log.IsWarnEnabled && (DateTime.UtcNow.Ticks / 10000) - start > 500)
								log.WarnFormat("ExecuteNonQueryImpl: SQL NonQuery took {0}ms!\n{1}", ((DateTime.UtcNow.Ticks / 10000) - start), SQLCommand);
						}
						catch (Exception e)
						{
							if (!HandleException(e))
							{
								if (log.IsErrorEnabled)
									log.ErrorFormat("ExecuteNonQueryImpl: UnHandled Exception for raw query \"{0}\"\n{1}", SQLCommand, e);

								throw;
							}
							repeat = true;
						}
						finally
						{
							CloseConnection(conn);
						}
					}
				}
			}
			while (repeat);

			return affected;
		}
		#endregion

		#region Scalar Implementation
		
		/// <summary>
		/// Implementation of Scalar Query with Parameters for Prepared Query
		/// </summary>
		/// <param name="SQLCommand">Scalar Command</param>
		/// <param name="parameters">Collection of Parameters for Single/Multiple Read</param>
		/// <param name="retrieveLastInsertID">Return Last Insert ID of each Command instead of Scalar</param>
		/// <returns>Objects Returned by Scalar</returns>
		protected abstract object[] ExecuteScalarImpl(string SQLCommand, IEnumerable<IEnumerable<QueryParameter>> parameters, bool retrieveLastInsertID);
		#endregion
				
		protected virtual bool HandleException(Exception e)
		{
			bool ret = false;
			var socketException = e.InnerException == null
				? null
				: e.InnerException.InnerException as System.Net.Sockets.SocketException;
			
			if (socketException == null)
				socketException = e.InnerException as System.Net.Sockets.SocketException;

			if (socketException != null)
			{
				// Handle socket exception. Error codes:
				// http://msdn2.microsoft.com/en-us/library/ms740668.aspx
				// 10052 = Network dropped connection on reset.
				// 10053 = Software caused connection abort.
				// 10054 = Connection reset by peer.
				// 10057 = Socket is not connected.
				// 10058 = Cannot send after socket shutdown.
				switch (socketException.ErrorCode)
				{
					case 10052:
					case 10053:
					case 10054:
					case 10057:
					case 10058:
						ret = true;
						break;
				}

				if (log.IsWarnEnabled)
					log.WarnFormat("Socket exception: ({0}) {1}; repeat: {2}", socketException.ErrorCode, socketException.Message, ret);
			}

			return ret;
		}

		protected abstract bool HandleSQLException(Exception e);
	}
}

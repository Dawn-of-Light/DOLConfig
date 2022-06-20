using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

using DOL.Database.Attributes;
using DOL.Database.Connection;
using DOL.Database.Handlers;

using log4net;

namespace DOL.Database
{
	/// <summary>
	/// Default Object Database Base Implementation
	/// </summary>
	public abstract class ObjectDatabase : IObjectDatabase
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		protected static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Number Format Info to Use for Database
		/// </summary>
		protected static readonly NumberFormatInfo Nfi = new CultureInfo("en-US", false).NumberFormat;

		/// <summary>
		/// Data Table Handlers for this Database Handler
		/// </summary>
		protected readonly Dictionary<string, DataTableHandler> TableDatasets = new Dictionary<string, DataTableHandler>();

		/// <summary>
		/// Connection String for this Database
		/// </summary>
		protected string ConnectionString { get; set; }
		
		/// <summary>
		/// Creates a new Instance of <see cref="ObjectDatabase"/>
		/// </summary>
		/// <param name="ConnectionString">Database Connection String</param>
		protected ObjectDatabase(string ConnectionString)
		{
			this.ConnectionString = ConnectionString;
		}
		
		/// <summary>
		/// Helper to Retrieve Table Handler from Object Type
		/// Return Real Table Handler for Modifications Queries
		/// </summary>
		/// <param name="objectType">Object Type</param>
		/// <returns>DataTableHandler for this Object Type or null.</returns>
		protected DataTableHandler GetTableHandler(Type objectType)
		{
			var tableName = AttributesUtils.GetTableName(objectType);
			DataTableHandler handler;
			return TableDatasets.TryGetValue(tableName, out handler) ? handler : null;
		}

		#region Public Save Objects Implementation
		/// <summary>
		/// Saves a DataObject to database if saving is allowed and object is dirty
		/// </summary>
		/// <param name="dataObject">DataObject to Save in database</param>
		/// <returns>True is the DataObject was saved.</returns>
		public bool SaveObject(DataObject dataObject)
		{
			var success = true;
			foreach (var grp in new [] { dataObject }.GroupBy(obj => obj.GetType()))
			{
				var tableHandler = GetTableHandler(grp.Key);
				
				if (tableHandler == null)
				{
					if (log.IsErrorEnabled)
						log.ErrorFormat("SaveObject: DataObject Type ({0}) not registered !", grp.Key.FullName);
					success = false;
					continue;
				}
				
				var objs = grp.Where(obj => obj.Dirty).ToArray();
				var results = SaveObjectImpl(tableHandler, objs);
				var resultsByObjs = results.Select((result, index) => new { Success = result, DataObject = objs[index] })
					.GroupBy(obj => obj.Success);
				
				foreach (var resultGrp in resultsByObjs)
				{
					if(!resultGrp.Key)
					{
						if (log.IsErrorEnabled)
						{
							foreach(var obj in resultGrp)
								log.ErrorFormat("SaveObject: DataObject ({0}) could not be saved into database...", obj.DataObject);
						}
						success = false;
					}
				}
				
				if (tableHandler.HasRelations)
					success &= SaveObjectRelations(tableHandler, grp);				
			}
			return success;
		}
		
		/// <summary>
		/// Save DataObjects to database if saving is allowed and object is dirty
		/// </summary>
		/// <param name="dataObjects">DataObjects to Save in database</param>
		/// <returns>True if All DataObjects were saved.</returns>
		public bool SaveObject(IEnumerable<DataObject> dataObjects)
		{
			var success = true;
			foreach (var grp in dataObjects.GroupBy(obj => obj.GetType()))
			{
				var tableHandler = GetTableHandler(grp.Key);
				
				if (tableHandler == null)
				{
					if (log.IsErrorEnabled)
						log.ErrorFormat("SaveObject: DataObject Type ({0}) not registered !", grp.Key.FullName);
					success = false;
					continue;
				}
				
				var objs = grp.Where(obj => obj.Dirty).ToArray();
				var results = SaveObjectImpl(tableHandler, objs);
				var resultsByObjs = results.Select((result, index) => new { Success = result, DataObject = objs[index] })
					.GroupBy(obj => obj.Success);
				
				foreach (var resultGrp in resultsByObjs)
				{
					if(!resultGrp.Key)
					{
						if (log.IsErrorEnabled)
						{
							foreach(var obj in resultGrp)
								log.ErrorFormat("SaveObject: DataObject ({0}) could not be saved into database...", obj.DataObject);
						}
						success = false;
					}
				}
				
				if (tableHandler.HasRelations)
					success &= SaveObjectRelations(tableHandler, grp);				
			}
			return success;
		}
		#endregion

		#region Relation Update Handling
		/// <summary>
		/// Save Relations Objects attached to DataObjects
		/// </summary>
		/// <param name="tableHandler">TableHandler for Source DataObjects Relation</param>
		/// <param name="dataObjects">DataObjects to parse</param>
		/// <returns>True if all Relations were saved</returns>
		protected bool SaveObjectRelations(DataTableHandler tableHandler, IEnumerable<DataObject> dataObjects)
		{
			var success = true;
			foreach (var relation in tableHandler.ElementBindings.Where(bind => bind.Relation != null))
			{
				// Relation Check
				var remoteHandler = GetTableHandler(relation.ValueType);
				if (remoteHandler == null)
				{
					if (log.IsErrorEnabled)
						log.ErrorFormat("SaveObjectRelations: Remote Table for Type ({0}) is not registered !", relation.ValueType.FullName);
					success = false;
					continue;
				}

				// Check For Array Type
				var groups = relation.ValueType.HasElementType
					? dataObjects.Select(obj => new { Source = obj, Enumerable = (IEnumerable<DataObject>)relation.GetValue(obj) })
					.Where(obj => obj.Enumerable != null).Select(obj => obj.Enumerable.Select(rel => new { Local = obj.Source, Remote = rel }))
					.SelectMany(obj => obj).Where(obj => obj.Remote != null).GroupBy(obj => obj.Remote.IsPersisted)
					: dataObjects.Select(obj => new { Local = obj, Remote = (DataObject)relation.GetValue(obj) }).Where(obj => obj.Remote != null).GroupBy(obj => obj.Remote.IsPersisted);
				
				foreach (var grp in groups)
				{
					// Group by object that can be added or saved
					foreach (var allowed in grp.GroupBy(obj => grp.Key ? obj.Remote.Dirty : obj.Remote.AllowAdd))
					{
						if (allowed.Key)
						{
							var objs = allowed.ToArray();
							var results = grp.Key ? SaveObjectImpl(remoteHandler, objs.Select(obj => obj.Remote)) : AddObjectImpl(remoteHandler, objs.Select(obj => obj.Remote));
							
							var resultsByObjs = results.Select((result, index) => new { Success = result, RelObject = objs[index] });
							
							foreach (var resultGrp in resultsByObjs.GroupBy(obj => obj.Success))
							{
								if (!resultGrp.Key)
								{
									if (log.IsErrorEnabled)
									{
										foreach (var result in resultGrp)
											log.ErrorFormat("SaveObjectRelations: {0} Relation ({1}) of DataObject ({2}) failed for Object ({3})", grp.Key ? "Saving" : "Adding",
											                relation.ValueType, result.RelObject.Local, result.RelObject.Remote);
									}
									success = false;
								}
							}
						}
						else
						{
							// Objects that could not be added can lead to failure
							if (!grp.Key)
							{
								if (log.IsWarnEnabled)
								{
									foreach (var obj in allowed)
										log.WarnFormat("SaveObjectRelations: DataObject ({0}) not allowed to be added to Database", obj);
								}
								success = false;
							}
						}
					}
				}
			}
			return success;
		}
		#endregion

		#region Relation Select/Fill Handling
		
		/// <summary>
		/// Populate or Refresh Objects Relations
		/// </summary>
		/// <param name="dataObjects">Objects to Populate</param>
		/// <param name="force">Force Refresh even if Autoload is False</param>
		protected virtual void FillObjectRelations(IEnumerable<DataObject> dataObjects, bool force)
		{
			var groups = dataObjects.GroupBy(obj => obj.GetType());
			
			foreach (var grp in groups)
			{
				var dataType = grp.Key;
				var tableName = AttributesUtils.GetTableName(dataType);
				try
				{
					
					DataTableHandler tableHandler;
					if (!TableDatasets.TryGetValue(tableName, out tableHandler))
						throw new DatabaseException(string.Format("Table {0} is not registered for Database Connection...", tableName));
					
					if (!tableHandler.HasRelations)
						return;
					
					var relations = tableHandler.ElementBindings.Where(bind => bind.Relation != null);
					foreach (var relation in relations)
					{
						// Check if Loading is needed
						if (!(relation.Relation.AutoLoad || force))
							continue;
						
						var remoteName = AttributesUtils.GetTableName(relation.ValueType);						
						try
						{
							DataTableHandler remoteHandler;
							if (!TableDatasets.TryGetValue(remoteName, out remoteHandler))
								throw new DatabaseException(string.Format("Table {0} is not registered for Database Connection...", remoteName));

							// Select Object On Relation Constraint
							var localBind = tableHandler.FieldElementBindings.Single(bind => bind.ColumnName.Equals(relation.Relation.LocalField, StringComparison.OrdinalIgnoreCase));
							var remoteBind = remoteHandler.FieldElementBindings.Single(bind => bind.ColumnName.Equals(relation.Relation.RemoteField, StringComparison.OrdinalIgnoreCase));
							
							FillObjectRelationsImpl(relation, localBind, remoteBind, remoteHandler, grp);
						}
						catch (Exception re)
						{
							if (log.IsErrorEnabled)
								log.ErrorFormat("Could not Retrieve Objects from Relation (Table {0}, Local {1}, Remote Table {2}, Remote {3})\n{4}", tableName,
								                relation.Relation.LocalField, AttributesUtils.GetTableName(relation.ValueType), relation.Relation.RemoteField, re);
						}
					}
				}
				catch (Exception e)
				{
					if (log.IsErrorEnabled)
						log.ErrorFormat("Could not Resolve Relations for Table {0}\n{1}", tableName, e);
				}
			}
		}
		
		/// <summary>
		/// Populate or Refresh Object Relation Implementation
		/// </summary>
		/// <param name="relationBind">Element Binding for Relation Field</param>
		/// <param name="localBind">Local Binding for Value Match</param>
		/// <param name="remoteBind">Remote Binding for Column Match</param>
		/// <param name="remoteHandler">Remote Table Handler for Cache Retrieving</param>
		/// <param name="dataObjects">DataObjects to Populate</param>
		protected virtual void FillObjectRelationsImpl(ElementBinding relationBind, ElementBinding localBind, ElementBinding remoteBind, DataTableHandler remoteHandler, IEnumerable<DataObject> dataObjects)
		{
			var type = relationBind.ValueType;
			var isElementType = false;
			if (type.HasElementType)
			{
				type = type.GetElementType();
				isElementType = true;
			}
			
			var objects = dataObjects.ToArray();
			IEnumerable<IEnumerable<DataObject>> objsResults = null;
			
			
			var whereClauses = objects.Select(obj => DB.Column(remoteBind.ColumnName).IsEqualTo(localBind.GetValue(obj)));
			objsResults = MultipleSelectObjectsImpl(remoteHandler, whereClauses);
			
			var resultByObjs = objsResults.Select((obj, index) => new { DataObject = objects[index], Results = obj }).ToArray();
			
			// Store Relations
			foreach (var result in resultByObjs)
			{
				if (isElementType)
				{
					if (result.Results.Any())
					{
						MethodInfo castMethod = typeof(Enumerable).GetMethod("OfType").MakeGenericMethod(type);
						MethodInfo methodToArray = typeof(Enumerable).GetMethod("ToArray").MakeGenericMethod(type);
						relationBind.SetValue(result.DataObject, methodToArray.Invoke(null, new object[] { castMethod.Invoke(null, new object[] { result.Results }) }));
					}
					else
					{
						relationBind.SetValue(result.DataObject, null);
					}
				}
				else
				{
					relationBind.SetValue(result.DataObject, result.Results.SingleOrDefault());
				}
			}
			
			// Fill Sub Relations
			FillObjectRelations(resultByObjs.SelectMany(result => result.Results), false);
		}
		#endregion
		
		#region Public Object Select All API
		public IList<TObject> SelectAllObjects<TObject>()
			where TObject : DataObject
		{
			var tableHandler = GetTableHandler(typeof(TObject));
			if (tableHandler == null)
			{
				if (log.IsErrorEnabled)
					log.ErrorFormat("SelectAllObjects: DataObject Type ({0}) not registered !", typeof(TObject).FullName);

				throw new DatabaseException(string.Format("Table {0} is not registered for Database Connection...", typeof(TObject).FullName));
			}

			var dataObjects = MultipleSelectObjectsImpl(tableHandler, new[] { WhereClause.Empty }).Single().OfType<TObject>().ToArray();

			FillObjectRelations(dataObjects, false);

			return dataObjects;
		}
		#endregion
		
		#region Public API
		/// <summary>
		/// Register Data Object Type if not already Registered
		/// </summary>
		/// <param name="dataObjectType">DataObject Type</param>
		public virtual void RegisterDataObject(Type dataObjectType)
		{
			var tableName = AttributesUtils.GetTableName(dataObjectType);
			if (TableDatasets.ContainsKey(tableName))
				return;
			
			var dataTableHandler = new DataTableHandler(dataObjectType);
			TableDatasets.Add(tableName, dataTableHandler);
		}

		#endregion

		#region Implementation
		/// <summary>
		/// Adds new DataObjects to the database.
		/// </summary>
		/// <param name="dataObjects">DataObjects to add to the database</param>
		/// <param name="tableHandler">Table Handler for the DataObjects Collection</param>
		/// <returns>True if objects were added successfully; false otherwise</returns>
		protected abstract IEnumerable<bool> AddObjectImpl(DataTableHandler tableHandler, IEnumerable<DataObject> dataObjects);

		/// <summary>
		/// Saves Persisted DataObjects into Database
		/// </summary>
		/// <param name="dataObjects">DataObjects to Save</param>
		/// <param name="tableHandler">Table Handler for the DataObjects Collection</param>
		/// <returns>True if objects were saved successfully; false otherwise</returns>
		protected abstract IEnumerable<bool> SaveObjectImpl(DataTableHandler tableHandler, IEnumerable<DataObject> dataObjects);

		protected abstract IList<IList<DataObject>> MultipleSelectObjectsImpl(DataTableHandler tableHandler, IEnumerable<WhereClause> whereClauseBatch);
		#endregion

		#region Factory

		public static MySQLObjectDatabase GetObjectDatabase(ConnectionType connectionType, string connectionString)
		{
			if (connectionType == ConnectionType.DATABASE_MYSQL)
				return new MySQLObjectDatabase(connectionString);

			return null;
		}

		#endregion
	}
}

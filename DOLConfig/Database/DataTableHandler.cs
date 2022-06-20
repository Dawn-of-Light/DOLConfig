using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using DataTable = System.Data.DataTable;

using DOL.Database.Attributes;

namespace DOL.Database
{
	/// <summary>
	/// DataTableHandler that keep tracks of DataObjects Definition for matching Database schema. 
	/// </summary>
	public sealed class DataTableHandler
	{
		/// <summary>
		/// Data Object Type
		/// </summary>
		public Type ObjectType { get; private set; }
		/// <summary>
		/// Has Relations
		/// </summary>
		public bool HasRelations { get; private set; }
		/// <summary>
		/// The Table Name for this Handler
		/// </summary>
		public string TableName { get; private set; }
		/// <summary>
		/// Element Bindings for this Handler
		/// </summary>
		public ElementBinding[] ElementBindings { get; private set; }
		/// <summary>
		/// Retrieve Element Bindings for DataTable Fields Only
		/// </summary>
		public IEnumerable<ElementBinding> FieldElementBindings 
		{
			get { return ElementBindings.Where(bind => bind.Relation == null); }
		}
		/// <summary>
		/// Data Table Handled
		/// </summary>
		public DataTable Table { get; private set; }
		/// <summary>
		/// Retrieve Single Primary Key Binding
		/// </summary>
		public ElementBinding PrimaryKey { get { return PrimaryKeys.FirstOrDefault(); } }
		/// <summary>
		/// Retrieve Multiple Primary Key Binding (Future Support)
		/// </summary>
		public ElementBinding[] PrimaryKeys { get { return Table.PrimaryKey.Select(col => ElementBindings.FirstOrDefault(bind => bind.ColumnName.Equals(col.ColumnName, StringComparison.OrdinalIgnoreCase))).ToArray(); } }
		/// <summary>
		/// Helper to retrieve the name of the automatic primary key associated with this table
		/// </summary>
		public string PrimaryKeyColumnName => $"{TableName}_ID";

		/// <summary>
		/// Create new instance of <see cref="DataTableHandler"/>
		/// </summary>
		/// <param name="ObjectType">DataObject Type</param>
		public DataTableHandler(Type ObjectType)
		{
			if (!typeof(DataObject).IsAssignableFrom(ObjectType))
				throw new ArgumentException("DataTableHandler can only register DataObject Types", "ObjectType");
			
			this.ObjectType = ObjectType;
			// Init Cache and Table Params
			TableName = AttributesUtils.GetTableName(ObjectType);

			HasRelations = false;
			
			// Parse Table Type
			ElementBindings = ObjectType.GetMembers().Select(member => new ElementBinding(member)).Where(bind => bind.IsDataElementBinding).ToArray();
			
			// If no Primary Key AutoIncrement add GUID
			if (FieldElementBindings.Any(bind => bind.PrimaryKey != null && !bind.PrimaryKey.AutoIncrement))
				ElementBindings = ElementBindings.Concat(new [] {
				                                         	new ElementBinding(ObjectType.GetProperty("ObjectId"),
				                                         	                   new DataElement(){ Unique = true },
																			   PrimaryKeyColumnName)
				                                         }).ToArray();
			else if (FieldElementBindings.All(bind => bind.PrimaryKey == null))
				ElementBindings = ElementBindings.Concat(new [] {
				                                         	new ElementBinding(ObjectType.GetProperty("ObjectId"),
				                                         	                   new PrimaryKey(),
																			   PrimaryKeyColumnName)
				                                         }).ToArray();
			
			
			// Prepare Table
			Table = new DataTable(TableName);
			var multipleUnique = new List<ElementBinding>();
			var indexes = new Dictionary<string, ElementBinding[]>();
			
			//Build Table for DataSet
			foreach (var bind in ElementBindings)
			{
				if (bind.Relation != null)
				{
					HasRelations = true;
					continue;
				}
				
				var column = Table.Columns.Add(bind.ColumnName, bind.ValueType);
				
				if (bind.PrimaryKey != null)
				{
					column.AutoIncrement = bind.PrimaryKey.AutoIncrement;
					Table.PrimaryKey = new [] { column };
				}
				
				if (bind.DataElement != null)
				{
					column.AllowDBNull = bind.DataElement.AllowDbNull;
					
					// Store Multi Unique for definition after table
					// Single Unique can be defined directly.
					if (!string.IsNullOrEmpty(bind.DataElement.UniqueColumns))
					{
						multipleUnique.Add(bind);

					}
					else if (bind.DataElement.Unique)
					{
						Table.Constraints.Add(new UniqueConstraint(string.Format("U_{0}_{1}", TableName, bind.ColumnName), column));
					}
					
					// Store Indexes for definition after table
					if (!string.IsNullOrEmpty(bind.DataElement.IndexColumns))
						indexes.Add(string.Format("I_{0}_{1}", TableName, bind.ColumnName), bind.DataElement.IndexColumns.Split(',')
						            .Select(col => FieldElementBindings.FirstOrDefault(ind => ind.ColumnName.Equals(col.Trim(), StringComparison.OrdinalIgnoreCase)))
						            .Concat(new [] { bind }).ToArray());
					else if (bind.DataElement.Index)
						indexes.Add(string.Format("I_{0}_{1}", TableName, bind.ColumnName), new [] { bind });
					
					if (bind.DataElement.Varchar > 0)
						column.ExtendedProperties.Add("VARCHAR", bind.DataElement.Varchar);
				}
			}
			
			// Set Indexes when all columns are set
			Table.ExtendedProperties.Add("INDEXES", indexes.Select(kv => new KeyValuePair<string, DataColumn[]>(
				kv.Key,
				kv.Value.Select(bind => Table.Columns[bind.ColumnName]).ToArray()))
				.ToDictionary(kv => kv.Key, kv => kv.Value));
			
			// Set Unique Constraints when all columns are set.
			foreach (var bind in multipleUnique)
			{
				var columns = bind.DataElement.UniqueColumns.Split(',').Select(column => column.Trim()).Concat(new [] { bind.ColumnName });
				Table.Constraints.Add(new UniqueConstraint(string.Format("U_{0}_{1}", TableName, bind.ColumnName),
				                                           columns.Select(column => Table.Columns[column]).ToArray()));
			}
		}
	}
}
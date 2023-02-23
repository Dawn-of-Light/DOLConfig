using System;
using System.Linq;
using System.Reflection;

namespace DOL.Database.Attributes
{
	/// <summary>
	/// Utils Method for Handling DOL Database Attributes
	/// </summary>
	public static class AttributesUtils
	{
		/// <summary>
		/// Returns the TableName from Type if DataTable Attribute is found 
		/// </summary>
		/// <param name="type">Type inherited from DataObject</param>
		/// <returns>Table Name from DataTable Attribute or ClassName</returns>
		public static string GetTableName(Type type)
		{
			// Check if Type is Element
			if (type.HasElementType)
				type = type.GetElementType();
			
			var dataTable = type.GetCustomAttributes<DataTable>(true).FirstOrDefault();
			
			if (dataTable != null && !string.IsNullOrEmpty(dataTable.TableName))
				return dataTable.TableName;
			
			return type.Name;
		}
	}
}

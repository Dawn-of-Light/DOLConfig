using System;
using System.Collections.Generic;

namespace DOL.Database
{
	/// <summary>
	/// Interface for Handling Object Database API
	/// </summary>
	public interface IObjectDatabase
	{
		#region Save Objects
		/// <summary>
		/// Save a DataObject to database if saving is allowed and object is dirty
		/// </summary>
		/// <param name="dataObject">DataObject to Save in database</param>
		/// <returns>True if the DataObject was saved.</returns>
		bool SaveObject(DataObject dataObject);

		/// <summary>
		/// Save DataObjects to database if saving is allowed and object is dirty
		/// </summary>
		/// <param name="dataObjects">DataObjects to Save in database</param>
		/// <returns>True if All DataObjects were saved.</returns>
		bool SaveObject(IEnumerable<DataObject> dataObjects);
		#endregion
		
		#region Select All Object
		/// <summary>
		/// Select all Objects From Table holding TObject Type
		/// </summary>
		/// <typeparam name="TObject">DataObject Type to Select</typeparam>
		/// <returns>Collection of all DataObject for this Type</returns>
		IList<TObject> SelectAllObjects<TObject>()
			where TObject : DataObject;
		#endregion
		
		#region Metadata Handlers
		/// <summary>
		/// Register Data Object Type if not already Registered
		/// </summary>
		/// <param name="dataObjectType">DataObject Type</param>
		void RegisterDataObject(Type dataObjectType);
		#endregion
	}
}

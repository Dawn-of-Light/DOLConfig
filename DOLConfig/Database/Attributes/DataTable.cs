using System;

namespace DOL.Database.Attributes
{
	[AttributeUsage(AttributeTargets.Class)]
	public class DataTable : Attribute
	{
		public DataTable()
		{
			TableName = null;
		}

		public string TableName { get; set; }
	}
}

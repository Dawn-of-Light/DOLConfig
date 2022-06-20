using System;
using System.IO;
using System.Text;
using System.Xml;

namespace DOL.Config
{
	public class XMLConfigFile : ConfigElement
	{
		public XMLConfigFile()
			: base(null)
		{
		}

		private static bool IsBadXMLElementName(string name)
		{
			if (name == null)
				return false;

			if (name.IndexOf(@"\") != -1)
				return true;

			if (name.IndexOf(@"/") != -1)
				return true;

			if (name.IndexOf(@"<") != -1)
				return true;

			if (name.IndexOf(@">") != -1)
				return true;

			return false;
		}

		private static void SaveElement(XmlWriter writer, string name, ConfigElement element)
		{
			bool badName = IsBadXMLElementName(name);

			if (element.HasChildren)
			{
				if (name == null)
					name = "root";

				if (badName)
				{
					writer.WriteStartElement("param");
					writer.WriteAttributeString("name", name);
				}
				else
				{
					writer.WriteStartElement(name);
				}

				foreach (var entry in element.Children)
				{
					SaveElement(writer, entry.Key, entry.Value);
				}

				writer.WriteEndElement();
			}
			else
			{
				if (name != null)
				{
					if (badName)
					{
						writer.WriteStartElement("param");
						writer.WriteAttributeString("name", name);
						writer.WriteString(element.GetString());
						writer.WriteEndElement();
					}
					else
					{
						writer.WriteElementString(name, element.GetString());
					}
				}
			}
		}

		public void Save(FileInfo configFile)
		{
			if (configFile == null)
				throw new ArgumentNullException("configFile");

			if (configFile.Exists)
				configFile.Delete();

			var writer = new XmlTextWriter(configFile.FullName, Encoding.UTF8)
			             	{
			             		Formatting = Formatting.Indented
			             	};

			try
			{
				writer.WriteStartDocument();
				SaveElement(writer, null, this);
				writer.WriteEndDocument();
			}
			finally
			{
				writer.Close();
			}
		}

		public static XMLConfigFile ParseXMLFile(FileInfo configFile)
		{
			if (configFile == null)
				throw new ArgumentNullException("configFile");

			var root = new XMLConfigFile();

			if (!configFile.Exists)
				return root;

			ConfigElement current = root;
			using(var reader = new XmlTextReader(configFile.OpenRead()))
			{
				while (reader.Read())
				{
					if (reader.NodeType == XmlNodeType.Element)
					{
						if (reader.Name == "root")
							continue;

						if (reader.Name == "param")
						{
							string name = reader.GetAttribute("name");

							if (name != null && name != "root")
							{
								var newElement = new ConfigElement(current);
								current[name] = newElement;
								current = newElement;
							}
						}
						else
						{
							var newElement = new ConfigElement(current);
							current[reader.Name] = newElement;
							var isNotSingleTag = !reader.IsEmptyElement;
							if (isNotSingleTag) current = newElement;
						}
					}
					else if (reader.NodeType == XmlNodeType.Text)
					{
						current.Set(reader.Value);
					}
					else if (reader.NodeType == XmlNodeType.EndElement)
					{
						if (reader.Name != "root")
						{
							current = current.Parent;
						}
					}
				}
			}

			return root;
		}
	}
}
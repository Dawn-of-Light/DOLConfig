using System;
using System.IO;
using System.Net;
using System.Reflection;

using DOL.Config;
using DOL.Database.Connection;

namespace DOL.GS
{
    public class GameServerConfiguration
    {
        #region BaseServerProperties
        public ushort Port { get; set; }
        public IPAddress IP { get; set; }
        public IPAddress RegionIP { get; set; }
        public ushort RegionPort { get; set; }
        public IPAddress UDPIP { get; set; }
        public ushort UDPPort { get; set; }
        public bool EnableUPnP { get; set; }
        public bool DetectRegionIP { get; set; }
        #endregion

        #region Server
        protected string ScriptAssemblies { get; set; }

        public string RootDirectory { get; set; }
        public string ScriptCompilationTarget { get; set; }
        public string[] AdditionalScriptAssemblies
            => string.IsNullOrEmpty(ScriptAssemblies.Trim()) ? Array.Empty<string>() : ScriptAssemblies.Split(',');
        public bool EnableCompilation { get; set; }
        public bool AutoAccountCreation { get; set; }
        public eGameServerType ServerType { get; set; }
        public string ServerName { get; set; }
        public string ServerNameShort { get; set; }
        public string GMActionsLoggerName { get; set; }
        public string CheatLoggerName { get; set; }
        public string InventoryLoggerName { get; set; }
        public string LogConfigFile { get; set; }
        #endregion

        #region Logging
        protected string m_gmActionsLoggerName;
        protected string m_cheatLoggerName;
        protected string m_invalidNamesFile = "";
        public string InvalidNamesFile { get; set; }

        #endregion

        #region Database
        public string DBConnectionString { get; set; }
        public ConnectionType DBType { get; set; }
        public bool AutoSave { get; set; }
        public int SaveInterval { get; set; }
        #endregion

        #region Constructors
        public GameServerConfiguration() : base()
        {
            Port = 10300;
            IP = IPAddress.Any;
            RegionIP = IPAddress.Any;
            RegionPort = 10400;
            UDPIP = IPAddress.Any;
            UDPPort = 10400;
            DetectRegionIP = true;
            EnableUPnP = true;

            ServerName = "Dawn Of Light";
            ServerNameShort = "DOLSERVER";

            RootDirectory = new FileInfo(Assembly.GetEntryAssembly().Location).DirectoryName;

            LogConfigFile = Path.Combine(Path.Combine(".", "config"), "logconfig.xml");

            ScriptCompilationTarget = Path.Combine(Path.Combine(".", "lib"), "GameServerScripts.dll");
            ScriptAssemblies = " ";
            EnableCompilation = true;
            AutoAccountCreation = true;
            ServerType = eGameServerType.GST_Normal;

            m_cheatLoggerName = "cheats";
            m_gmActionsLoggerName = "gmactions";
            InventoryLoggerName = "inventories";
            m_invalidNamesFile = Path.Combine(Path.Combine(".", "config"), "invalidnames.txt");

            DBType = ConnectionType.DATABASE_SQLITE;
            DBConnectionString = $"Data Source={Path.Combine(RootDirectory, "dol.sqlite3.db")}";
            AutoSave = true;
            SaveInterval = 10;
        }
        #endregion

        public void LoadFromXMLFile(FileInfo configFile)
        {
            if (configFile == null)
                throw new ArgumentNullException("configFile");

            XMLConfigFile xmlConfig = XMLConfigFile.ParseXMLFile(configFile);
            LoadFromConfig(xmlConfig);
        }

        public void SaveToXMLFile(FileInfo configFile)
        {
            if (configFile == null)
                throw new ArgumentNullException("configFile");

            var config = new XMLConfigFile();
            SaveToConfig(config);

            config.Save(configFile);
        }
        #region Load/Save

        protected void LoadFromConfig(ConfigElement root)
        {
            string ip = root["Server"]["IP"].GetString("any");
            IP = ip == "any" ? IPAddress.Any : IPAddress.Parse(ip);
            Port = (ushort)root["Server"]["Port"].GetInt(Port);

            ip = root["Server"]["RegionIP"].GetString("any");
            RegionIP = ip == "any" ? IPAddress.Any : IPAddress.Parse(ip);
            RegionPort = (ushort)root["Server"]["RegionPort"].GetInt(RegionPort);

            ip = root["Server"]["UdpIP"].GetString("any");

            UDPIP = ip == "any" ? IPAddress.Any : IPAddress.Parse(ip);
            UDPPort = (ushort)root["Server"]["UdpPort"].GetInt(UDPPort);

            EnableUPnP = root["Server"]["EnableUPnP"].GetBoolean(EnableUPnP);
            DetectRegionIP = root["Server"]["DetectRegionIP"].GetBoolean(DetectRegionIP);

            // Removed to not confuse users
            //			m_rootDirectory = root["Server"]["RootDirectory"].GetString(m_rootDirectory);

            LogConfigFile = root["Server"]["LogConfigFile"].GetString(LogConfigFile);

            ScriptCompilationTarget = root["Server"]["ScriptCompilationTarget"].GetString(ScriptCompilationTarget);
            ScriptAssemblies = root["Server"]["ScriptAssemblies"].GetString(ScriptAssemblies);
            EnableCompilation = root["Server"]["EnableCompilation"].GetBoolean(true);
            AutoAccountCreation = root["Server"]["AutoAccountCreation"].GetBoolean(AutoAccountCreation);

            string serverType = root["Server"]["GameType"].GetString("Normal");
            switch (serverType.ToLower())
            {
                case "normal":
                    ServerType = eGameServerType.GST_Normal;
                    break;
                case "casual":
                    ServerType = eGameServerType.GST_Casual;
                    break;
                case "roleplay":
                    ServerType = eGameServerType.GST_Roleplay;
                    break;
                case "pve":
                    ServerType = eGameServerType.GST_PvE;
                    break;
                case "pvp":
                    ServerType = eGameServerType.GST_PvP;
                    break;
                case "test":
                    ServerType = eGameServerType.GST_Test;
                    break;
                default:
                    ServerType = eGameServerType.GST_Normal;
                    break;
            }

            ServerName = root["Server"]["ServerName"].GetString(ServerName);
            ServerNameShort = root["Server"]["ServerNameShort"].GetString(ServerNameShort);

            m_cheatLoggerName = root["Server"]["CheatLoggerName"].GetString(m_cheatLoggerName);
            m_gmActionsLoggerName = root["Server"]["GMActionLoggerName"].GetString(m_gmActionsLoggerName);
            m_invalidNamesFile = root["Server"]["InvalidNamesFile"].GetString(m_invalidNamesFile);

            string db = root["Server"]["DBType"].GetString("XML");
            switch (db.ToLower())
            {
                case "xml":
                    DBType = ConnectionType.DATABASE_XML;
                    break;
                case "mysql":
                    DBType = ConnectionType.DATABASE_MYSQL;
                    break;
                case "sqlite":
                    DBType = ConnectionType.DATABASE_SQLITE;
                    break;
                case "mssql":
                    DBType = ConnectionType.DATABASE_MSSQL;
                    break;
                case "odbc":
                    DBType = ConnectionType.DATABASE_ODBC;
                    break;
                case "oledb":
                    DBType = ConnectionType.DATABASE_OLEDB;
                    break;
                default:
                    DBType = ConnectionType.DATABASE_XML;
                    break;
            }
            DBConnectionString = root["Server"]["DBConnectionString"].GetString(DBConnectionString);
            AutoSave = root["Server"]["DBAutosave"].GetBoolean(AutoSave);
            SaveInterval = root["Server"]["DBAutosaveInterval"].GetInt(SaveInterval);
        }

        protected void SaveToConfig(ConfigElement root)
        {
            root["Server"]["Port"].Set(Port);
            root["Server"]["IP"].Set(IP);
            root["Server"]["RegionIP"].Set(RegionIP);
            root["Server"]["RegionPort"].Set(RegionPort);
            root["Server"]["UdpIP"].Set(UDPIP);
            root["Server"]["UdpPort"].Set(UDPPort);
            root["Server"]["EnableUPnP"].Set(EnableUPnP);
            root["Server"]["DetectRegionIP"].Set(DetectRegionIP);
            root["Server"]["ServerName"].Set(ServerName);
            root["Server"]["ServerNameShort"].Set(ServerNameShort);
            // Removed to not confuse users
            //			root["Server"]["RootDirectory"].Set(m_rootDirectory);
            root["Server"]["LogConfigFile"].Set(LogConfigFile);

            root["Server"]["ScriptCompilationTarget"].Set(ScriptCompilationTarget);
            root["Server"]["ScriptAssemblies"].Set(ScriptAssemblies);
            root["Server"]["EnableCompilation"].Set(EnableCompilation);
            root["Server"]["AutoAccountCreation"].Set(AutoAccountCreation);

            string serverType = "Normal";

            switch (ServerType)
            {
                case eGameServerType.GST_Normal:
                    serverType = "Normal";
                    break;
                case eGameServerType.GST_Casual:
                    serverType = "Casual";
                    break;
                case eGameServerType.GST_Roleplay:
                    serverType = "Roleplay";
                    break;
                case eGameServerType.GST_PvE:
                    serverType = "PvE";
                    break;
                case eGameServerType.GST_PvP:
                    serverType = "PvP";
                    break;
                case eGameServerType.GST_Test:
                    serverType = "Test";
                    break;
                default:
                    serverType = "Normal";
                    break;
            }
            root["Server"]["GameType"].Set(serverType);

            root["Server"]["CheatLoggerName"].Set(m_cheatLoggerName);
            root["Server"]["GMActionLoggerName"].Set(m_gmActionsLoggerName);
            root["Server"]["InvalidNamesFile"].Set(m_invalidNamesFile);

            string db = "XML";

            switch (DBType)
            {
                case ConnectionType.DATABASE_XML:
                    db = "XML";
                    break;
                case ConnectionType.DATABASE_MYSQL:
                    db = "MYSQL";
                    break;
                case ConnectionType.DATABASE_SQLITE:
                    db = "SQLITE";
                    break;
                case ConnectionType.DATABASE_MSSQL:
                    db = "MSSQL";
                    break;
                case ConnectionType.DATABASE_ODBC:
                    db = "ODBC";
                    break;
                case ConnectionType.DATABASE_OLEDB:
                    db = "OLEDB";
                    break;
                default:
                    DBType = ConnectionType.DATABASE_XML;
                    break;
            }
            root["Server"]["DBType"].Set(db);
            root["Server"]["DBConnectionString"].Set(DBConnectionString);
            root["Server"]["DBAutosave"].Set(AutoSave);
            root["Server"]["DBAutosaveInterval"].Set(SaveInterval);
        }
        #endregion
    }

    public enum eGameServerType
    {
        GST_Normal = 0,
        GST_Test = 1,
        GST_PvP = 2,
        GST_PvE = 3,
        GST_Roleplay = 4,
        GST_Casual = 5,
        GST_Unknown = 6,
        _GST_Count = 7,
    }
}
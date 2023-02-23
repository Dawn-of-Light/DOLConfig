using System.Collections.Generic;

namespace DOL.Config
{
    public class ConfigElement
    {
        private readonly Dictionary<string, ConfigElement> _children = new Dictionary<string, ConfigElement>();

        private readonly ConfigElement _parent;

        private string _value;

        public ConfigElement(ConfigElement parent)
        {
            _parent = parent;
        }

        public ConfigElement this[string key]
        {
            get
            {
                lock (_children)
                {
                    if (!_children.ContainsKey(key))
                    {
                        _children.Add(key, GetNewConfigElement(this));
                    }
                }

                return _children[key];
            }
            set
            {
                lock (_children)
                {
                    _children[key] = value;
                }
            }
        }

        public ConfigElement Parent => _parent;

        public bool HasChildren => _children.Count > 0;

        public Dictionary<string, ConfigElement> Children => _children;

        private static ConfigElement GetNewConfigElement(ConfigElement parent)
            => new ConfigElement(parent);

        public string GetString()
            => _value ?? "";

        public string GetString(string defaultValue)
            => _value ?? defaultValue;

        public int GetInt()
            => int.Parse(_value ?? "0");

        public int GetInt(int defaultValue)
            => _value != null ? int.Parse(_value) : defaultValue;

        public long GetLong()
            => long.Parse(_value ?? "0");

        public long GetLong(long defaultValue)
            => _value != null ? long.Parse(_value) : defaultValue;

        public bool GetBoolean()
            => bool.Parse(_value ?? "false");

        public bool GetBoolean(bool defaultValue)
            => _value != null ? bool.Parse(_value) : defaultValue;

        public void Set(object value)
        {
            if (value == null)
                value = "";

            _value = value.ToString();
        }
    }
}
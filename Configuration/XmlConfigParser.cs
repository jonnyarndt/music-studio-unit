
using System;
using core_tools;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;

namespace musicStudioUnit.Configuration
{
    /// <summary>
    /// XML Configuration Parser for MSU local configuration files
    /// Extends existing core_tools file operations for robust XML parsing
    /// </summary>
    public class XmlConfigParser : IKeyName, IDisposable
    {
        private readonly string _key;
        private readonly string _configFileName;
        private readonly string _configDirectory;

        public string Key => _key;
        public string Name => "XML Configuration Parser";

        // Events
        public event EventHandler<XmlConfigurationLoadedEventArgs> ConfigurationLoaded;
        public event EventHandler<XmlConfigurationErrorEventArgs> ConfigurationError;

        public XmlConfigParser(string key, string configFileName = "msu.xml")
        {
            _key = key;
            _configFileName = configFileName;
            // Use Global.FilePathPrefix if set, otherwise use GetConfigDirectory()
            string prefix = musicStudioUnit.Global.FilePathPrefix ?? string.Empty;
            if (!string.IsNullOrEmpty(prefix)) {
                Debug.Console(0, this, "DEBUG: Using Global.FilePathPrefix for config directory: {0}", prefix);
                _configDirectory = prefix;
            } else {
                _configDirectory = GetConfigDirectory();
            }
            DeviceManager.AddDevice(key, this);
            Debug.Console(1, this, "XML Configuration Parser initialized for file: {0}", configFileName);
            Debug.Console(0, this, "DEBUG: Final config directory: {0}", _configDirectory);
        }

        /// <summary>
        /// Load and parse XML configuration file
        /// </summary>
        /// <returns>Parsed LocalConfiguration object or null if failed</returns>
        public LocalConfiguration LoadConfiguration()
        {
            try
            {
                string configPath = GetConfigFilePath();
                Debug.Console(1, this, "Loading XML configuration from: {0}", configPath);
                Debug.Console(0, this, "DEBUG: Current working directory: {0}", Crestron.SimplSharp.CrestronIO.Directory.GetApplicationDirectory());
                Debug.Console(0, this, "DEBUG: Global.FilePathPrefix: {0}", musicStudioUnit.Global.FilePathPrefix);

                // Check if file exists
                if (!Crestron.SimplSharp.CrestronIO.File.Exists(configPath))
                {
                    var error = string.Format("Configuration file not found: {0}", configPath);
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                }

                // Read file content
                string xmlContent = ReadConfigFile(configPath);
                if (string.IsNullOrEmpty(xmlContent))
                {
                    var error = "Configuration file is empty or could not be read";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                }

                // Validate XML structure
                if (!ValidateXmlStructure(xmlContent))
                {
                    var error = "Configuration file contains invalid XML structure";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                }

                // Parse XML to object
                LocalConfiguration config = ParseXmlContent(xmlContent);
                if (config != null)
                {
                    Debug.Console(1, this, "XML configuration loaded successfully");
                    LogConfigurationSummary(config);
                    OnConfigurationLoaded(config);
                    return config;
                }
                else
                {
                    var error = "Failed to parse XML configuration";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                    return null;
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("Error loading XML configuration: {0}", ex.Message);
                Debug.Console(0, this, error);
                OnConfigurationError(error);
                return null;
            }
        }

        /// <summary>
        /// Reload configuration file
        /// </summary>
        public LocalConfiguration ReloadConfiguration()
        {
            Debug.Console(1, this, "Reloading XML configuration");
            return LoadConfiguration();
        }

        /// <summary>
        /// Validate that required configuration sections exist
        /// </summary>
        public bool ValidateConfiguration(LocalConfiguration config)
        {
            if (config == null)
            {
                Debug.Console(0, this, "Configuration is null");
                return false;
            }

            bool isValid = true;

            // Validate address section
            if (config.Address == null)
            {
                Debug.Console(0, this, "Address section is missing");
                isValid = false;
            }
            else if (string.IsNullOrEmpty(config.Address.City))
            {
                Debug.Console(0, this, "City is required in address section");
                isValid = false;
            }

            // Validate remote section
            if (config.Remote == null)
            {
                Debug.Console(0, this, "Remote section is missing");
                isValid = false;
            }
            else
            {
                if (string.IsNullOrEmpty(config.Remote.IP))
                {
                    Debug.Console(0, this, "Remote IP is required");
                    isValid = false;
                }
                if (config.Remote.Port <= 0 || config.Remote.Port > 65535)
                {
                    Debug.Console(0, this, "Remote port must be between 1 and 65535");
                    isValid = false;
                }
                if (string.IsNullOrEmpty(config.Remote.File))
                {
                    Debug.Console(0, this, "Remote file name is required");
                    isValid = false;
                }
            }

            // Validate HVAC section
            if (config.HVAC == null)
            {
                Debug.Console(0, this, "HVAC section is missing");
                isValid = false;
            }
            else
            {
                if (string.IsNullOrEmpty(config.HVAC.IP))
                {
                    Debug.Console(0, this, "HVAC IP is required");
                    isValid = false;
                }
                if (config.HVAC.Port <= 0 || config.HVAC.Port > 65535)
                {
                    Debug.Console(0, this, "HVAC port must be between 1 and 65535");
                    isValid = false;
                }
                if (config.HVAC.IdleSetpoint < -40.0f || config.HVAC.IdleSetpoint > 50.0f)
                {
                    Debug.Console(0, this, "HVAC idle setpoint must be between -40°C and 50°C");
                    isValid = false;
                }
            }

            // Validate DMS section
            if (config.DMS == null)
            {
                Debug.Console(0, this, "DMS section is missing");
                isValid = false;
            }
            else
            {
                if (string.IsNullOrEmpty(config.DMS.IP))
                {
                    Debug.Console(0, this, "DMS IP is required");
                    isValid = false;
                }
                if (config.DMS.Port <= 0 || config.DMS.Port > 65535)
                {
                    Debug.Console(0, this, "DMS port must be between 1 and 65535");
                    isValid = false;
                }
                if (config.DMS.ListenPort <= 0 || config.DMS.ListenPort > 65535)
                {
                    Debug.Console(0, this, "DMS listen port must be between 1 and 65535");
                    isValid = false;
                }
            }

            return isValid;
        }

        private string ReadConfigFile(string filePath)
        {
            try
            {
                // Use Crestron.SimplSharp.CrestronIO.StreamReader for robust file reading similar to core_tools pattern
                using (Crestron.SimplSharp.CrestronIO.FileStream fs = new Crestron.SimplSharp.CrestronIO.FileStream(filePath, Crestron.SimplSharp.CrestronIO.FileMode.Open, Crestron.SimplSharp.CrestronIO.FileAccess.Read))
                using (Crestron.SimplSharp.CrestronIO.StreamReader sr = new Crestron.SimplSharp.CrestronIO.StreamReader(fs))
                {
                    string content = sr.ReadToEnd();
                    Debug.Console(2, this, "Read {0} characters from configuration file", content.Length);
                    return content;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error reading configuration file: {0}", ex.Message);
                return null;
            }
        }

        private bool ValidateXmlStructure(string xmlContent)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlContent);

                // Check for root configuration element
                if (doc.DocumentElement == null || doc.DocumentElement.Name != "configuration")
                {
                    Debug.Console(0, this, "Invalid XML structure: Missing root 'configuration' element");
                    return false;
                }

                // Check for required child elements
                string[] requiredElements = { "address", "remote", "hvac", "dms" };
                foreach (string element in requiredElements)
                {
                    if (doc.DocumentElement.SelectSingleNode(element) == null)
                    {
                        Debug.Console(0, this, "Invalid XML structure: Missing '{0}' element", element);
                        return false;
                    }
                }

                Debug.Console(2, this, "XML structure validation passed");
                return true;
            }
            catch (System.Xml.XmlException ex)
            {
                Debug.Console(0, this, "XML validation error: {0}", ex.Message);
                return false;
            }
        }

        private LocalConfiguration? ParseXmlContent(string xmlContent)
        {
            try
            {
                // Normalize XML content to lowercase element names
                string normalizedXmlContent = NormalizeXmlElementsToLowercase(xmlContent);
                
                XmlSerializer serializer = new XmlSerializer(typeof(LocalConfiguration));
                using (var memoryStream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(normalizedXmlContent)))
                {
                    LocalConfiguration? config = (LocalConfiguration?)serializer.Deserialize(memoryStream);
                    return config;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error parsing XML content: {0}", ex.Message);
                return null;
            }
        }

        private string NormalizeXmlElementsToLowercase(string xmlContent)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlContent);
                
                // Recursively normalize all element names to lowercase
                NormalizeElementNames(doc.DocumentElement);
                
                string normalizedXml = doc.OuterXml;
                Debug.Console(2, this, "Normalized XML content to lowercase element names");
                return normalizedXml;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error normalizing XML content: {0}", ex.Message);
                return xmlContent; // Return original if normalization fails
            }
        }

        private void NormalizeElementNames(XmlNode node)
        {
            if (node == null) return;
            
            // Create a list of child nodes to avoid modification during iteration
            var childNodes = new List<XmlNode>();
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                    childNodes.Add(child);
            }
            
            // Process each child element
            foreach (XmlNode child in childNodes)
            {
                // Recursively normalize child elements first
                NormalizeElementNames(child);
                
                // Rename the element to lowercase if needed
                if (child.Name != child.Name.ToLower())
                {
                    XmlElement newElement = node.OwnerDocument.CreateElement(child.Name.ToLower());
                    
                    // Copy all attributes
                    foreach (XmlAttribute attr in child.Attributes)
                    {
                        newElement.SetAttribute(attr.Name, attr.Value);
                    }
                    
                    // Copy all child nodes
                    while (child.FirstChild != null)
                    {
                        newElement.AppendChild(child.FirstChild);
                    }
                    
                    // Replace the old element with the new one
                    node.ReplaceChild(newElement, child);
                }
            }
        }

        private string GetConfigDirectory()
        {
            // Handle different file system structures between 4-series processor and VC-4
            string currentDir = Crestron.SimplSharp.CrestronIO.Directory.GetApplicationDirectory();
            
            // Try user subdirectory first (standard for RMC4)
            string userDir = Crestron.SimplSharp.CrestronIO.Path.Combine(currentDir, "user");
            if (Crestron.SimplSharp.CrestronIO.Directory.Exists(userDir))
            {
                Debug.Console(1, this, "Using user directory: {0}", userDir);
                return userDir;
            }

            // Fall back to current directory (VC-4 and other scenarios)
            Debug.Console(1, this, "Using current directory: {0}", currentDir);
            return currentDir;
        }

        private string GetConfigFilePath()
        {
            return Crestron.SimplSharp.CrestronIO.Path.Combine(_configDirectory, _configFileName);
        }

        private void LogConfigurationSummary(LocalConfiguration config)
        {
            Debug.Console(1, this, "Configuration Summary:");
            Debug.Console(1, this, "  Building: {0}, {1}", config.Address?.Street, config.Address?.City);
            Debug.Console(1, this, "  Remote Server: {0}:{1}/{2}", config.Remote?.IP, config.Remote?.Port, config.Remote?.File);
            Debug.Console(1, this, "  HVAC Server: {0}:{1} (Idle: {2:F1}°C)", config.HVAC?.IP, config.HVAC?.Port, config.HVAC?.IdleSetpoint);
            Debug.Console(1, this, "  DMS Server: {0}:{1} (Listen: {2})", config.DMS?.IP, config.DMS?.Port, config.DMS?.ListenPort);
        }

        protected virtual void OnConfigurationLoaded(LocalConfiguration config)
        {
            ConfigurationLoaded?.Invoke(this, new XmlConfigurationLoadedEventArgs { Configuration = config });
        }

        protected virtual void OnConfigurationError(string errorMessage)
        {
            ConfigurationError?.Invoke(this, new XmlConfigurationErrorEventArgs { ErrorMessage = errorMessage });
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }

    /// <summary>
    /// Event arguments for XML configuration loaded
    /// </summary>
    public class XmlConfigurationLoadedEventArgs : EventArgs
    {
        public required LocalConfiguration Configuration { get; set; }
    }

    /// <summary>
    /// Event arguments for XML configuration errors
    /// </summary>
    public class XmlConfigurationErrorEventArgs : EventArgs
    {
        public string? ErrorMessage { get; set; }
    }
}

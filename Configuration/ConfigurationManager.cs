
using System;
using core_tools;
using System.IO;
using System.Xml.Serialization;
using Crestron.SimplSharp;
using Newtonsoft.Json;
using musicStudioUnit.Configuration;

namespace musicStudioUnit.Services
{
    /// <summary>
    /// Configuration Manager for loading local XML and remote JSON configuration files
    /// Enhanced with dedicated XML and HTTP parsers
    /// </summary>
    public class ConfigurationManager : IKeyName, IDisposable
    {
        private readonly string _key;
        private LocalConfiguration? _localConfig;
        private RemoteConfiguration? _remoteConfig;
        private XmlConfigParser _xmlParser;
        private HttpConfigClient _httpClient;

        public string Key => _key;
        public string Name => "Configuration Manager";

        public LocalConfiguration? LocalConfig => _localConfig;
        public RemoteConfiguration? RemoteConfig => _remoteConfig;

        public event EventHandler<ConfigurationLoadedEventArgs>? ConfigurationLoaded;

        public ConfigurationManager(string key)
        {
            _key = key;
            // Debug.SetContextName(key); // Not available in current PepperDash version
            DeviceManager.AddDevice(key, this);

            // Initialize parsers
            _xmlParser = new XmlConfigParser(key + "XmlParser");
            _httpClient = new HttpConfigClient(key + "HttpClient");

            // Register for parser events
            _xmlParser.ConfigurationLoaded -= OnXmlConfigurationLoaded;
            _xmlParser.ConfigurationError -= OnXmlConfigurationError;
            _httpClient.ConfigurationLoaded -= OnHttpConfigurationLoaded;
            _httpClient.ConfigurationError -= OnHttpConfigurationError;

            _xmlParser.ConfigurationLoaded += OnXmlConfigurationLoaded;
            _xmlParser.ConfigurationError += OnXmlConfigurationError;
            _httpClient.ConfigurationLoaded += OnHttpConfigurationLoaded;
            _httpClient.ConfigurationError += OnHttpConfigurationError;
        }

        /// <summary>
        /// Load local XML configuration from processor user directory
        /// </summary>
        public bool LoadLocalConfiguration()
        {
            try
            {
                Debug.Console(1, this, "Loading local XML configuration using enhanced parser");

                _localConfig = _xmlParser.LoadConfiguration();
                
                if (_localConfig != null)
                {
                    // Validate configuration
                    if (_xmlParser.ValidateConfiguration(_localConfig))
                    {
                        Debug.Console(1, this, "Local configuration loaded and validated successfully");
                        return true;
                    }
                    else
                    {
                        Debug.Console(0, this, "Local configuration validation failed");
                        _localConfig = null;
                        return false;
                    }
                }
                else
                {
                    Debug.Console(0, this, "Failed to load local configuration");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error in LoadLocalConfiguration: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Load remote JSON configuration from web server
        /// </summary>
        public bool LoadRemoteConfiguration()
        {
            if (_localConfig == null)
            {
                Debug.Console(0, this, "Local configuration must be loaded first");
                return false;
            }

            try
            {
                Debug.Console(1, this, "Loading remote JSON configuration using enhanced HTTP client");

                // Test server connectivity first
                if (!_httpClient.TestServerConnectivity(_localConfig.Remote.IP, _localConfig.Remote.Port))
                {
                    Debug.Console(0, this, "Cannot connect to configuration server");
                    return false;
                }

                _remoteConfig = _httpClient.LoadConfiguration(
                    _localConfig.Remote.IP,
                    _localConfig.Remote.Port,
                    _localConfig.Remote.File);

                if (_remoteConfig != null)
                {
                    // Validate configuration
                    if (_httpClient.ValidateConfiguration(_remoteConfig))
                    {
                        Debug.Console(1, this, "Remote configuration loaded and validated successfully");
                        
                        // Fire configuration loaded event
                        ConfigurationLoaded?.Invoke(this, new ConfigurationLoadedEventArgs
                        {
                            LocalConfig = _localConfig,
                            RemoteConfig = _remoteConfig
                        });

                        return true;
                    }
                    else
                    {
                        Debug.Console(0, this, "Remote configuration validation failed");
                        _remoteConfig = null;
                        return false;
                    }
                }
                else
                {
                    Debug.Console(0, this, "Failed to load remote configuration");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error in LoadRemoteConfiguration: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Get the current MSU configuration based on MAC address
        /// </summary>
        public MSUConfiguration GetCurrentMSUConfiguration()
        {
            if (_remoteConfig == null)
            {
                Debug.Console(0, this, "Remote configuration not loaded");
                return null;
            }

            try
            {
                // Get current processor MAC address
                var sysInfo = new SystemInformationMethods();
                sysInfo.GetEthernetInfo();
                string currentMAC = sysInfo.Adapter.MacAddress;

                Debug.Console(1, this, "Searching for MSU with MAC: {0}", currentMAC);

                // Find matching MSU configuration
                foreach (var msu in _remoteConfig.MSUUnits)
                {
                    // Normalize MAC address format for comparison
                    string configMAC = NormalizeMACAddress(msu.MSU_MAC);
                    string systemMAC = NormalizeMACAddress(currentMAC);

                    if (string.Equals(configMAC, systemMAC, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Console(1, this, "Found MSU configuration: {0} at ({1},{2})", 
                            msu.MSU_NAME, msu.X_COORD, msu.Y_COORD);
                        return msu;
                    }
                }

                Debug.Console(0, this, "No MSU configuration found for MAC: {0}", currentMAC);
                return null;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error getting current MSU configuration: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Reload all configuration files
        /// </summary>
        public bool ReloadConfiguration()
        {
            Debug.Console(1, this, "Reloading configuration files");
            
            bool localLoaded = LoadLocalConfiguration();
            bool remoteLoaded = false;

            if (localLoaded)
            {
                remoteLoaded = LoadRemoteConfiguration();
            }

            return localLoaded && remoteLoaded;
        }

        private string GetConfigFilePath()
        {
            // Handle different file system structures between 4-series and VC-4
            string userDir = Directory.GetCurrentDirectory();
            
            // Try standard user directory
            string configPath = Path.Combine(userDir, "user", "msu.xml");
            if (Crestron.SimplSharp.CrestronIO.File.Exists(configPath))
                return configPath;

            // Try alternate paths for different processor types
            configPath = Path.Combine(userDir, "msu.xml");
            if (Crestron.SimplSharp.CrestronIO.File.Exists(configPath))
                return configPath;

            // Default to user directory path
            return Path.Combine(userDir, "user", "msu.xml");
        }

        /// <summary>
        /// Event handler for XML configuration loading
        /// </summary>
        private void OnXmlConfigurationLoaded(object? sender, XmlConfigurationLoadedEventArgs args)
        {
            Debug.Console(1, this, "XML configuration loaded via parser");
            _localConfig = args.Configuration;
        }

        /// <summary>
        /// Event handler for XML configuration errors
        /// </summary>
        private void OnXmlConfigurationError(object? sender, XmlConfigurationErrorEventArgs args)
        {
            Debug.Console(0, this, "XML configuration error: {0}", args.ErrorMessage);
        }

        /// <summary>
        /// Event handler for HTTP configuration loading
        /// </summary>
        private void OnHttpConfigurationLoaded(object? sender, JsonConfigurationLoadedEventArgs args)
        {
            Debug.Console(1, this, "HTTP configuration loaded via client");
            _remoteConfig = args.Configuration;
        }

        /// <summary>
        /// Event handler for HTTP configuration errors
        /// </summary>
        private void OnHttpConfigurationError(object? sender, JsonConfigurationErrorEventArgs args)
        {
            Debug.Console(0, this, "HTTP configuration error: {0}", args.ErrorMessage);
        }

        private string NormalizeMACAddress(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress))
                return string.Empty;

            // Remove common delimiters and convert to uppercase
            return macAddress.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpper();
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }

    /// <summary>
    /// Event arguments for configuration loaded event
    /// </summary>
    public class ConfigurationLoadedEventArgs : EventArgs
    {
        public LocalConfiguration? LocalConfig { get; set; }
        public RemoteConfiguration? RemoteConfig { get; set; }
    }
}

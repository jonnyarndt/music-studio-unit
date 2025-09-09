using System;
using PepperDash.Core;
using System.Net;
using PepperDash.Core;
using System.Text;
using PepperDash.Core;
using System.Threading;
using PepperDash.Core;
using Crestron.SimplSharp;
using Newtonsoft.Json;
using core_tools;

namespace musicStudioUnit.Configuration
{
    /// <summary>
    /// HTTP Client for JSON Configuration Retrieval
    /// Built on existing core_tools patterns for network communication
    /// </summary>
    public class HttpConfigClient : IKeyName, IDisposable
    {
        private readonly string _key;
        private WebClient _webClient;
        private CTimer _timeoutTimer;
        private bool _requestInProgress;
        private const int DEFAULT_TIMEOUT_MS = 10000; // 10 seconds

        public string Key => _key;
        public string Name => "HTTP Configuration Client";

        // Events
        public event EventHandler<JsonConfigurationLoadedEventArgs> ConfigurationLoaded;
        public event EventHandler<JsonConfigurationErrorEventArgs> ConfigurationError;

        public HttpConfigClient(string key)
        {
            _key = key;
            DeviceManager.AddDevice(key, this);
            Debug.Console(1, this, "HTTP Configuration Client initialized");
        }

        /// <summary>
        /// Load JSON configuration from remote server
        /// </summary>
        /// <param name="serverIP">Server IP address</param>
        /// <param name="port">Server port</param>
        /// <param name="fileName">Configuration file name</param>
        /// <param name="timeoutMs">Request timeout in milliseconds</param>
        /// <returns>Parsed RemoteConfiguration object or null if failed</returns>
        public RemoteConfiguration LoadConfiguration(string serverIP, int port, string fileName, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            try
            {
                if (_requestInProgress)
                {
                    Debug.Console(1, this, "Configuration request already in progress, ignoring new request");
                    return null;
                }

                // Validate parameters
                if (string.IsNullOrEmpty(serverIP))
                {
                    var error = "Server IP cannot be null or empty";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                    return null;
                }

                if (port <= 0 || port > 65535)
                {
                    var error = string.Format("Invalid port number: {0}. Must be between 1 and 65535", port);
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                    return null;
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    var error = "File name cannot be null or empty";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                    return null;
                }

                // Build URL
                string url = BuildConfigurationUrl(serverIP, port, fileName);
                Debug.Console(1, this, "Loading JSON configuration from: {0}", url);

                // Perform HTTP request
                string jsonContent = PerformHttpRequest(url, timeoutMs);
                if (string.IsNullOrEmpty(jsonContent))
                {
                    var error = "No content received from configuration server";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                    return null;
                }

                // Parse JSON content
                RemoteConfiguration config = ParseJsonContent(jsonContent);
                if (config != null)
                {
                    Debug.Console(1, this, "JSON configuration loaded successfully");
                    LogConfigurationSummary(config);
                    OnConfigurationLoaded(config);
                    return config;
                }
                else
                {
                    var error = "Failed to parse JSON configuration";
                    Debug.Console(0, this, error);
                    OnConfigurationError(error);
                    return null;
                }
            }
            catch (Exception ex)
            {
                var error = string.Format("Error loading JSON configuration: {0}", ex.Message);
                Debug.Console(0, this, error);
                OnConfigurationError(error);
                return null;
            }
        }

        /// <summary>
        /// Load configuration asynchronously
        /// </summary>
        public void LoadConfigurationAsync(string serverIP, int port, string fileName, int timeoutMs = DEFAULT_TIMEOUT_MS)
        {
            // Use CTimer to perform async operation
            new CTimer((obj) => LoadConfiguration(serverIP, port, fileName, timeoutMs), 1);
        }

        /// <summary>
        /// Validate JSON configuration content
        /// </summary>
        public bool ValidateConfiguration(RemoteConfiguration config)
        {
            if (config == null)
            {
                Debug.Console(0, this, "Configuration is null");
                return false;
            }

            if (config.MSUUnits == null || config.MSUUnits.Count == 0)
            {
                Debug.Console(0, this, "No MSU units found in configuration");
                return false;
            }

            bool isValid = true;
            int validMSUs = 0;

            foreach (var msu in config.MSUUnits)
            {
                bool msuValid = true;

                if (string.IsNullOrEmpty(msu.MSU_UID))
                {
                    Debug.Console(0, this, "MSU UID is required");
                    msuValid = false;
                }

                if (string.IsNullOrEmpty(msu.MSU_NAME))
                {
                    Debug.Console(0, this, "MSU Name is required");
                    msuValid = false;
                }

                if (string.IsNullOrEmpty(msu.MSU_MAC))
                {
                    Debug.Console(0, this, "MSU MAC address is required");
                    msuValid = false;
                }
                else if (!IsValidMACAddress(msu.MSU_MAC))
                {
                    Debug.Console(0, this, "Invalid MAC address format: {0}", msu.MSU_MAC);
                    msuValid = false;
                }

                if (msu.X_COORD < -50 || msu.X_COORD > 50)
                {
                    Debug.Console(0, this, "X coordinate must be between -50 and 50");
                    msuValid = false;
                }

                if (msu.Y_COORD < -50 || msu.Y_COORD > 50)
                {
                    Debug.Console(0, this, "Y coordinate must be between -50 and 50");
                    msuValid = false;
                }

                if (msu.HVAC_ID <= 0 || msu.HVAC_ID > 255)
                {
                    Debug.Console(0, this, "HVAC ID must be between 1 and 255");
                    msuValid = false;
                }

                if (msuValid)
                {
                    validMSUs++;
                }
                else
                {
                    isValid = false;
                    Debug.Console(0, this, "MSU validation failed: {0}", msu.MSU_NAME ?? "Unknown");
                }
            }

            Debug.Console(1, this, "Configuration validation: {0}/{1} MSUs valid", validMSUs, config.MSUUnits.Count);
            return isValid;
        }

        /// <summary>
        /// Test connectivity to configuration server
        /// </summary>
        public bool TestServerConnectivity(string serverIP, int port, int timeoutMs = 5000)
        {
            try
            {
                string testUrl = string.Format("http://{0}:{1}/", serverIP, port);
                Debug.Console(1, this, "Testing connectivity to: {0}", testUrl);

                using (WebClient testClient = new WebClient())
                {
                    testClient.Headers.Add("User-Agent", "Crestron MSU Controller Test");
                    
                    // Setup timeout
                    bool completed = false;
                    string result = null;
                    Exception testException = null;

                    CTimer timeoutTimer = new CTimer((o) =>
                    {
                        if (!completed)
                        {
                            Debug.Console(0, this, "Server connectivity test timed out");
                            testClient.CancelAsync();
                        }
                    }, timeoutMs);

                    try
                    {
                        result = testClient.DownloadString(testUrl);
                        completed = true;
                        timeoutTimer.Stop();
                        Debug.Console(1, this, "Server connectivity test successful");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        completed = true;
                        timeoutTimer.Stop();
                        testException = ex;
                    }

                    if (testException != null)
                    {
                        Debug.Console(0, this, "Server connectivity test failed: {0}", testException.Message);
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error testing server connectivity: {0}", ex.Message);
                return false;
            }
        }

        private string BuildConfigurationUrl(string serverIP, int port, string fileName)
        {
            // Build HTTP URL following RFC specification
            return string.Format("http://{0}:{1}/{2}", serverIP, port, fileName);
        }

        private string PerformHttpRequest(string url, int timeoutMs)
        {
            try
            {
                _requestInProgress = true;
                string content = null;
                Exception requestException = null;
                bool requestCompleted = false;

                // Create WebClient with proper headers
                using (_webClient = new WebClient())
                {
                    _webClient.Headers.Add("User-Agent", "Crestron MSU Controller");
                    _webClient.Headers.Add("Accept", "application/json, text/plain, */*");
                    _webClient.Headers.Add("Cache-Control", "no-cache");

                    // Setup timeout timer
                    _timeoutTimer = new CTimer((o) =>
                    {
                        if (!requestCompleted)
                        {
                            Debug.Console(0, this, "HTTP request timed out after {0}ms", timeoutMs);
                            try
                            {
                                _webClient.CancelAsync();
                            }
                            catch { /* Ignore cancel errors */ }
                        }
                    }, timeoutMs);

                    try
                    {
                        content = _webClient.DownloadString(url);
                        requestCompleted = true;
                        _timeoutTimer.Stop();
                        
                        Debug.Console(2, this, "HTTP request completed successfully. Content length: {0}", 
                            content?.Length ?? 0);
                    }
                    catch (WebException webEx)
                    {
                        requestCompleted = true;
                        _timeoutTimer?.Stop();
                        
                        if (webEx.Status == WebExceptionStatus.Timeout)
                        {
                            Debug.Console(0, this, "HTTP request timed out");
                        }
                        else
                        {
                            Debug.Console(0, this, "HTTP request failed: {0}", webEx.Message);
                        }
                        requestException = webEx;
                    }
                    catch (Exception ex)
                    {
                        requestCompleted = true;
                        _timeoutTimer?.Stop();
                        requestException = ex;
                    }
                }

                _requestInProgress = false;

                if (requestException != null)
                {
                    throw requestException;
                }

                return content;
            }
            catch (Exception ex)
            {
                _requestInProgress = false;
                Debug.Console(0, this, "Error performing HTTP request: {0}", ex.Message);
                return null;
            }
        }

        private RemoteConfiguration ParseJsonContent(string jsonContent)
        {
            try
            {
                // Clean whitespace as specified in requirements
                string cleanedJson = jsonContent.Trim();
                
                Debug.Console(2, this, "Parsing JSON content ({0} characters)", cleanedJson.Length);
                
                // According to Client-Scope.md, the JSON file contains an array of MSU objects
                // Try to parse as direct array first (Client-Scope.md format)
                if (cleanedJson.StartsWith("["))
                {
                    Debug.Console(2, this, "Parsing as array format (Client-Scope.md specification)");
                    var msuList = JsonConvert.DeserializeObject<List<MSUConfiguration>>(cleanedJson);
                    
                    var config = new RemoteConfiguration();
                    config.MSUUnits = msuList ?? new List<MSUConfiguration>();
                    
                    Debug.Console(2, this, "Successfully parsed {0} MSU units from JSON array", config.MSUUnits.Count);
                    return config;
                }
                else
                {
                    // Try to parse as object with msu_units property (fallback)
                    Debug.Console(2, this, "Parsing as object format (fallback)");
                    RemoteConfiguration config = JsonConvert.DeserializeObject<RemoteConfiguration>(cleanedJson);
                    
                    if (config?.MSUUnits != null)
                    {
                        Debug.Console(2, this, "Successfully parsed {0} MSU units from JSON object", config.MSUUnits.Count);
                    }
                    
                    return config;
                }
            }
            catch (JsonException jsonEx)
            {
                Debug.Console(0, this, "JSON parsing error: {0}", jsonEx.Message);
                Debug.Console(2, this, "JSON content causing error: {0}", jsonContent.Substring(0, Math.Min(200, jsonContent.Length)));
                return null;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error parsing JSON content: {0}", ex.Message);
                return null;
            }
        }

        private bool IsValidMACAddress(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress))
                return false;

            // Remove common delimiters and check length
            string cleaned = macAddress.Replace(":", "").Replace("-", "").Replace(" ", "");
            
            if (cleaned.Length != 12)
                return false;

            // Check if all characters are hex
            foreach (char c in cleaned)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }

        private void LogConfigurationSummary(RemoteConfiguration config)
        {
            Debug.Console(1, this, "JSON Configuration Summary:");
            Debug.Console(1, this, "  Total MSU Units: {0}", config.MSUUnits?.Count ?? 0);
            
            if (config.MSUUnits != null)
            {
                foreach (var msu in config.MSUUnits)
                {
                    Debug.Console(1, this, "  MSU: {0} ({1}) at ({2},{3}) HVAC:{4}", 
                        msu.MSU_NAME, msu.MSU_UID, msu.X_COORD, msu.Y_COORD, msu.HVAC_ID);
                }
            }
        }

        protected virtual void OnConfigurationLoaded(RemoteConfiguration config)
        {
            ConfigurationLoaded?.Invoke(this, new JsonConfigurationLoadedEventArgs { Configuration = config });
        }

        protected virtual void OnConfigurationError(string errorMessage)
        {
            ConfigurationError?.Invoke(this, new JsonConfigurationErrorEventArgs { ErrorMessage = errorMessage });
        }

        public void Dispose()
        {
            _timeoutTimer?.Stop();
            _timeoutTimer = null;
            _webClient?.Dispose();
            _webClient = null;
            _requestInProgress = false;
        }
    }

    /// <summary>
    /// Event arguments for JSON configuration loaded
    /// </summary>
    public class JsonConfigurationLoadedEventArgs : EventArgs
    {
        public RemoteConfiguration Configuration { get; set; }
    }

    /// <summary>
    /// Event arguments for JSON configuration errors
    /// </summary>
    public class JsonConfigurationErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
    }
}

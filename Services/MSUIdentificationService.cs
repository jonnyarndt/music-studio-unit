using System;
using Crestron.SimplSharp;
using core_tools;
using musicStudioUnit.Configuration;

namespace musicStudioUnit.Services
{
    /// <summary>
    /// MSU Identification Service - Uses processor MAC address to identify and configure the MSU
    /// Based on Client-Scope.md requirements for MAC-based identification
    /// </summary>
    public class MSUIdentificationService : IKeyName, IDisposable
    {
        private readonly string _key;
        private string _processorMacAddress;
        private MSUConfiguration _identifiedMSU;
        private LocalConfiguration _localConfig;
        private RemoteConfiguration _remoteConfig;
        private SystemInformationMethods _systemInfo;

        public string Key => _key;
        public string Name => "MSU Identification Service";

        // Properties
        public string ProcessorMacAddress => _processorMacAddress;
        public MSUConfiguration IdentifiedMSU => _identifiedMSU;
        public bool IsIdentified => _identifiedMSU != null;

        // Events
        public event EventHandler<MSUIdentifiedEventArgs> MSUIdentified;
        public event EventHandler<MSUIdentificationErrorEventArgs> IdentificationError;

        public MSUIdentificationService(string key)
        {
            _key = key;
            _systemInfo = new SystemInformationMethods();
            
            DeviceManager.AddDevice(key, this);
            Debug.Console(1, this, "MSU Identification Service created");
        }

        /// <summary>
        /// Initialize and perform MSU identification using processor MAC address
        /// </summary>
        public bool Initialize()
        {
            try
            {
                Debug.Console(1, this, "Starting MSU identification process");

                // Step 1: Get processor MAC address
                if (!GetProcessorMacAddress())
                {
                    var error = "Failed to retrieve processor MAC address";
                    Debug.Console(0, this, error);
                    IdentificationError?.Invoke(this, new MSUIdentificationErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                Debug.Console(1, this, "Processor MAC Address: {0}", _processorMacAddress);
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("MSU identification initialization failed: {0}", ex.Message);
                Debug.Console(0, this, error);
                IdentificationError?.Invoke(this, new MSUIdentificationErrorEventArgs { ErrorMessage = error });
                return false;
            }
        }

        /// <summary>
        /// Identify this MSU using configurations and MAC address matching
        /// </summary>
        public bool IdentifyMSU(LocalConfiguration localConfig, RemoteConfiguration remoteConfig)
        {
            try
            {
                _localConfig = localConfig;
                _remoteConfig = remoteConfig;

                Debug.Console(1, this, "Attempting to identify MSU from {0} configured units", 
                    _remoteConfig?.MSUUnits?.Count ?? 0);

                // Step 1: Normalize processor MAC for comparison
                string normalizedProcessorMac = NormalizeMacAddress(_processorMacAddress);
                Debug.Console(2, this, "Normalized processor MAC: {0}", normalizedProcessorMac);

                // Step 2: Search through MSU configurations for MAC match
                if (_remoteConfig?.MSUUnits != null)
                {
                    foreach (var msuConfig in _remoteConfig.MSUUnits)
                    {
                        string normalizedConfigMac = NormalizeMacAddress(msuConfig.MSU_MAC);
                        Debug.Console(2, this, "Comparing with MSU {0} MAC: {1}", 
                            msuConfig.MSU_NAME, normalizedConfigMac);

                        if (normalizedProcessorMac.Equals(normalizedConfigMac, StringComparison.OrdinalIgnoreCase))
                        {
                            _identifiedMSU = msuConfig;
                            Debug.Console(1, this, "MSU IDENTIFIED: {0} (UID: {1}) at coordinates ({2},{3})", 
                                _identifiedMSU.MSU_NAME, 
                                _identifiedMSU.MSU_UID,
                                _identifiedMSU.X_COORD, 
                                _identifiedMSU.Y_COORD);

                            // Fire identification success event
                            MSUIdentified?.Invoke(this, new MSUIdentifiedEventArgs 
                            { 
                                IdentifiedMSU = _identifiedMSU,
                                ProcessorMac = _processorMacAddress
                            });

                            return true;
                        }
                    }
                }

                // No matching MSU found
                var error = string.Format("No MSU configuration found for processor MAC: {0}", _processorMacAddress);
                Debug.Console(0, this, error);
                IdentificationError?.Invoke(this, new MSUIdentificationErrorEventArgs { ErrorMessage = error });
                return false;
            }
            catch (Exception ex)
            {
                var error = string.Format("MSU identification failed: {0}", ex.Message);
                Debug.Console(0, this, error);
                IdentificationError?.Invoke(this, new MSUIdentificationErrorEventArgs { ErrorMessage = error });
                return false;
            }
        }

        /// <summary>
        /// Get processor MAC address using core_tools SystemInformationMethods
        /// </summary>
        private bool GetProcessorMacAddress()
        {
            try
            {
                Debug.Console(2, this, "Retrieving processor ethernet information");
                
                // Get ethernet information which includes MAC address
                _systemInfo.GetEthernetInfo();
                
                if (_systemInfo.Adapter != null && !string.IsNullOrEmpty(_systemInfo.Adapter.MacAddress))
                {
                    _processorMacAddress = _systemInfo.Adapter.MacAddress;
                    Debug.Console(2, this, "Raw MAC address retrieved: {0}", _processorMacAddress);
                    return true;
                }
                else
                {
                    Debug.Console(0, this, "MAC address is null or empty");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error retrieving MAC address: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Normalize MAC address for comparison - removes delimiters and converts to uppercase
        /// Handles various MAC address formats from different sources
        /// </summary>
        private string NormalizeMacAddress(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress))
                return string.Empty;

            // Remove common delimiters and convert to uppercase
            string normalized = macAddress
                .Replace(":", "")
                .Replace("-", "")
                .Replace(" ", "")
                .Replace(".", "")
                .ToUpper();

            Debug.Console(2, this, "Normalized MAC '{0}' to '{1}'", macAddress, normalized);
            return normalized;
        }

        /// <summary>
        /// Get adjacent MSU coordinates for combination logic
        /// </summary>
        public List<MSUConfiguration> GetAdjacentMSUs()
        {
            var adjacentMSUs = new List<MSUConfiguration>();

            if (_identifiedMSU == null || _remoteConfig?.MSUUnits == null)
                return adjacentMSUs;

            int currentX = _identifiedMSU.X_COORD;
            int currentY = _identifiedMSU.Y_COORD;

            // Find MSUs that are directly adjacent (north, south, east, west)
            foreach (var msu in _remoteConfig.MSUUnits)
            {
                // Skip self
                if (msu.MSU_UID == _identifiedMSU.MSU_UID)
                    continue;

                // Check if adjacent (north, south, east, west - not diagonal)
                bool isAdjacent = (Math.Abs(msu.X_COORD - currentX) == 1 && msu.Y_COORD == currentY) ||
                                 (Math.Abs(msu.Y_COORD - currentY) == 1 && msu.X_COORD == currentX);

                if (isAdjacent)
                {
                    adjacentMSUs.Add(msu);
                    Debug.Console(2, this, "Adjacent MSU found: {0} at ({1},{2})", 
                        msu.MSU_NAME, msu.X_COORD, msu.Y_COORD);
                }
            }

            Debug.Console(1, this, "Found {0} adjacent MSUs", adjacentMSUs.Count);
            return adjacentMSUs;
        }

        /// <summary>
        /// Validate MSU configuration completeness
        /// </summary>
        public bool ValidateMSUConfiguration()
        {
            if (_identifiedMSU == null)
                return false;

            // Check required fields
            bool isValid = !string.IsNullOrEmpty(_identifiedMSU.MSU_UID) &&
                          !string.IsNullOrEmpty(_identifiedMSU.MSU_NAME) &&
                          !string.IsNullOrEmpty(_identifiedMSU.MSU_MAC) &&
                          _identifiedMSU.HVAC_ID > 0;

            if (!isValid)
            {
                Debug.Console(0, this, "MSU configuration validation failed - missing required fields");
            }

            return isValid;
        }

        public void Dispose()
        {
            if (DeviceManager.ContainsKey(Key))
                DeviceManager.RemoveDevice(Key);
            
            _systemInfo = null;
        }
    }

    /// <summary>
    /// Event arguments for MSU identification success
    /// </summary>
    public class MSUIdentifiedEventArgs : EventArgs
    {
        public MSUConfiguration IdentifiedMSU { get; set; }
        public string ProcessorMac { get; set; }
    }

    /// <summary>
    /// Event arguments for MSU identification errors
    /// </summary>
    public class MSUIdentificationErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
    }
}

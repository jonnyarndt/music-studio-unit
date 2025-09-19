
using System;
using System.Collections.Generic;
using Crestron.SimplSharp;
using core_tools;
using musicStudioUnit.Configuration;
using musicStudioUnit.Services;
using musicStudioUnit.Devices;
using MusicSystemControllerNS = musicStudioUnit.MusicSystemController;
using HvacControllerNS = musicStudioUnit.HvacController;

namespace musicStudioUnit.Services
{
    /// <summary>
    /// Main MSU Controller that coordinates all system components
    /// </summary>
    public class MSUController : core_tools.IKeyName, IDisposable
    {
        private readonly string _key;
        
        // Core Services
        private ConfigurationManager _configManager;
        private MSUIdentificationService _identificationService;
        private UserManager _userManager;
    private StudioCombinationManager _combinationManager;
        
        // Device Controllers
        private EnhancedHVACController _hvacController;
        private EnhancedMusicSystemController _musicController;
        
        // Current configuration
        private MSUConfiguration _currentMSUConfig;
        private LocalConfiguration _localConfig;
        private RemoteConfiguration _remoteConfig;
        
        // System state
        private bool _isInitialized;
        private DateTime _systemStartTime;

        public string Key => _key;
        public string Name => "MSU Controller";

        // Properties
        public bool IsInitialized => _isInitialized;
        public MSUConfiguration CurrentMSUConfig => _currentMSUConfig;
        public ConfigurationManager ConfigManager => _configManager;
        public MSUIdentificationService IdentificationService => _identificationService;
        public UserManager UserManager => _userManager;
        public StudioCombinationManager CombinationManager => _combinationManager;
        public EnhancedHVACController HVACController => _hvacController;
        public EnhancedMusicSystemController MusicController => _musicController;        // Events
        public event EventHandler<MSUInitializedEventArgs> MSUInitialized;
        public event EventHandler<MSUErrorEventArgs> MSUError;

        public MSUController(string key, ConfigurationManager configManager)
        {
            _key = key;
            _systemStartTime = DateTime.Now;
            _configManager = configManager;
            
            DeviceManager.AddDevice(key, this);
            core_tools.Debug.Console(1, "MSUController", "MSU Controller created");
        }

        /// <summary>
        /// Set the MSU identification service (called by SystemInitializationService)
        /// </summary>
        public void SetIdentificationService(MSUIdentificationService identificationService)
        {
            _identificationService = identificationService;
            if (_identificationService?.IsIdentified == true)
            {
                _currentMSUConfig = _identificationService.IdentifiedMSU;
                core_tools.Debug.Console(1, "MSUController", "MSU identification data set: {0}", _currentMSUConfig.MSU_NAME);
            }
        }

        /// <summary>
        /// Initialize the MSU system
        /// </summary>
        public bool Initialize()
        {
            core_tools.Debug.Console(1, "MSUController", "Initializing MSU Controller");

            try
            {
                // Verify we have a valid ConfigurationManager (passed from SystemInitializationService)
                if (_configManager == null)
                {
                    var error = "Configuration Manager is null - cannot initialize MSU Controller";
                    core_tools.Debug.Console(0, "MSUController", error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                // Hook up event handler
                _configManager.ConfigurationLoaded += OnConfigurationLoaded;

                // Get configurations (should already be loaded by SystemInitializationService)
                _localConfig = _configManager.LocalConfig;
                if (_localConfig == null)
                {
                    var error = "Local configuration is null";
                    core_tools.Debug.Console(0, "MSUController", error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                _remoteConfig = _configManager.RemoteConfig;
                if (_remoteConfig == null)
                {
                    core_tools.Debug.Console(1, "MSUController", "Remote configuration is null - operating in standalone mode");
                    // In standalone mode, we'll create a default/empty remote config
                    _remoteConfig = new musicStudioUnit.Configuration.RemoteConfiguration
                    {
                        MSUUnits = new List<musicStudioUnit.Configuration.MSUConfiguration>()
                    };
                }

                // Find Current MSU Configuration
                _currentMSUConfig = _configManager.GetCurrentMSUConfiguration();
                if (_currentMSUConfig == null)
                {
                    core_tools.Debug.Console(1, "MSUController", "Could not find MSU configuration - creating standalone MSU configuration");
                    // Create a default MSU configuration for standalone operation
                    var sysInfo = new core_tools.SystemInformationMethods();
                    sysInfo.GetProcessorInfo();
                    sysInfo.GetEthernetInfo();
                    
                    _currentMSUConfig = new musicStudioUnit.Configuration.MSUConfiguration
                    {
                        MSU_NAME = "Standalone MSU",
                        MSU_UID = sysInfo.Adapter.MacAddress?.Replace(":", "").Replace("-", "") ?? "STANDALONE",
                        MSU_MAC = sysInfo.Adapter.MacAddress ?? "00:00:00:00:00:00",
                        X_COORD = 0,
                        Y_COORD = 0,
                        HVAC_ID = 1
                    };
                    
                    core_tools.Debug.Console(1, "MSUController", "Created standalone MSU configuration: {0} (MAC: {1})", 
                        _currentMSUConfig.MSU_NAME, _currentMSUConfig.MSU_MAC);
                }

                core_tools.Debug.Console(1, "MSUController", "Found MSU configuration: {0} at ({1},{2})",
                    _currentMSUConfig.MSU_NAME, _currentMSUConfig.X_COORD, _currentMSUConfig.Y_COORD);

                // Step 1: Initialize Core Services
                core_tools.Debug.Console(1, "MSUController", "Starting Step 1: Initialize Core Services");
                InitializeCoreServices();

                // Step 2: Initialize Device Controllers
                core_tools.Debug.Console(1, "MSUController", "Starting Step 2: Initialize Device Controllers");
                InitializeDeviceControllers();

                // Step 3: Start all services
                core_tools.Debug.Console(1, "MSUController", "Starting Step 3: Start Services");
                StartServices();

                _isInitialized = true;
                
                core_tools.Debug.Console(1, "MSUController", "MSU Controller initialization complete");

                // Fire initialized event
                MSUInitialized?.Invoke(this, new MSUInitializedEventArgs
                {
                    MSUConfig = _currentMSUConfig,
                    InitializationTime = DateTime.Now - _systemStartTime
                });

                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("Error during MSU initialization: {0}", ex.Message);
                core_tools.Debug.Console(0, "MSUController", error);
                core_tools.Debug.Console(0, "MSUController", "Stack trace: {0}", ex.StackTrace);
                MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                return false;
            }
        }

        /// <summary>
        /// Reload configuration files
        /// </summary>
        public bool ReloadConfiguration()
        {
            core_tools.Debug.Console(1, "MSUController", "Reloading MSU configuration");

            try
            {
                if (_configManager != null)
                {
                    return _configManager.ReloadConfiguration();
                }
                return false;
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "MSUController", "Error reloading configuration: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Get system information for display
        /// </summary>
        public MSUSystemInfo GetSystemInfo()
        {
            var sysInfo = new SystemInformationMethods();
            sysInfo.GetProcessorInfo();
            sysInfo.GetEthernetInfo();

            return new MSUSystemInfo
            {
                MSUName = _currentMSUConfig?.MSU_NAME ?? "Unknown",
                MSU_UID = _currentMSUConfig?.MSU_UID ?? "Unknown",
                ProcessorModel = sysInfo.Processor.Model,
                FirmwareVersion = sysInfo.Processor.Firmware,
                MACAddress = sysInfo.Adapter.MacAddress,
                IPAddress = sysInfo.Adapter.IpAddress,
                Hostname = sysInfo.Adapter.Hostname,
                BuildingAddress = _localConfig?.Address != null ? 
                    string.Format("{0}, {1}", _localConfig.Address.Street, _localConfig.Address.City) : "Unknown",
                TotalMSUCount = _remoteConfig?.MSUUnits.Count ?? 0,
                SystemUptime = DateTime.Now - _systemStartTime
            };
        }

        private void InitializeCoreServices()
        {
            core_tools.Debug.Console(1, "MSUController", "Initializing core services");

            // User Manager
            core_tools.Debug.Console(1, "MSUController", "Creating User Manager");
            _userManager = new UserManager(_key + "UserMgr");

            // Convert MSUConfiguration list to MusicStudioUnit dictionary
            var msuUnitCount = _remoteConfig?.MSUUnits?.Count ?? 0;
            core_tools.Debug.Console(1, "MSUController", "Converting {0} MSU configurations", msuUnitCount);
            var allMSUs = new Dictionary<string, MusicStudioUnit>();
            
            if (_remoteConfig?.MSUUnits != null)
            {
                foreach (var msuConfig in _remoteConfig.MSUUnits)
                {
                    allMSUs[msuConfig.MSU_UID] = new MusicStudioUnit
                    {
                        UID = msuConfig.MSU_UID,
                        Name = msuConfig.MSU_NAME,
                        MAC = msuConfig.MSU_MAC,
                        XCoord = msuConfig.X_COORD,
                        YCoord = msuConfig.Y_COORD,
                        HVACZoneId = (byte)msuConfig.HVAC_ID,
                        IsInUse = false,
                        IsCombined = false,
                        IsMaster = false
                    };
                }
            }
            
            // Add current MSU to the dictionary if not already present
            if (_currentMSUConfig != null && !allMSUs.ContainsKey(_currentMSUConfig.MSU_UID))
            {
                allMSUs[_currentMSUConfig.MSU_UID] = new MusicStudioUnit
                {
                    UID = _currentMSUConfig.MSU_UID,
                    Name = _currentMSUConfig.MSU_NAME,
                    MAC = _currentMSUConfig.MSU_MAC,
                    XCoord = _currentMSUConfig.X_COORD,
                    YCoord = _currentMSUConfig.Y_COORD,
                    HVACZoneId = (byte)_currentMSUConfig.HVAC_ID,
                    IsInUse = true,
                    IsCombined = false,
                    IsMaster = true
                };
                core_tools.Debug.Console(1, "MSUController", "Added current MSU to unit dictionary: {0}", _currentMSUConfig.MSU_UID);
            }

            // Studio Combination Manager
            var msuUID = _currentMSUConfig?.MSU_UID ?? "STANDALONE";
            var xCoord = _currentMSUConfig?.X_COORD ?? 0;
            var yCoord = _currentMSUConfig?.Y_COORD ?? 0;
            var hvacID = _currentMSUConfig?.HVAC_ID ?? 1;
            
            core_tools.Debug.Console(1, "MSUController", "Creating Studio Combination Manager for MSU {0}", msuUID);
            _combinationManager = new StudioCombinationManager(
                _key + "StudioComboMgr",
                msuUID,
                xCoord,
                yCoord,
                (byte)hvacID,
                allMSUs
            );
            _combinationManager.CombinationChanged += OnStudioCombinationChanged;
            
            core_tools.Debug.Console(1, "MSUController", "Core services initialization complete");
        }

        private void InitializeDeviceControllers()
        {
            core_tools.Debug.Console(1, "MSUController", "Initializing device controllers");

            // HVAC Controller
            core_tools.Debug.Console(1, "MSUController", "Creating HVAC Controller with IP: {0}, Port: {1}", 
                _localConfig.HVAC.IP, _localConfig.HVAC.Port);
            _hvacController = new EnhancedHVACController(_key + "HVAC", _localConfig.HVAC);
            _hvacController.StatusUpdated += OnHVACStatusUpdated;
            _hvacController.SetpointChanged += OnHVACSetpointChanged;

            // Convert Configuration.DMSInfo to Devices.DMSInfo
            var devicesDmsInfo = new musicStudioUnit.Devices.DMSInfo
            {
                IP = _localConfig.DMS.IP,
                Port = _localConfig.DMS.Port,
                ListenPort = _localConfig.DMS.ListenPort,
                ConnectionTimeoutMs = 5000 // Default value
            };

            // Music System Controller - only if we have MSU configuration
            if (_currentMSUConfig != null)
            {
                core_tools.Debug.Console(1, "MSUController", "Creating Music Controller with DMS IP: {0}, Port: {1}, MSU UID: {2}", 
                    devicesDmsInfo.IP, devicesDmsInfo.Port, _currentMSUConfig.MSU_UID);
                _musicController = new EnhancedMusicSystemController(_key + "Music", devicesDmsInfo, _currentMSUConfig.MSU_UID);
                _musicController.CatalogUpdated += OnMusicCatalogUpdated;
                _musicController.PlaybackStatusChanged += OnPlaybackStatusUpdated;
                _musicController.TrackTimeUpdated += OnTrackTimeUpdated;
            }
            else
            {
                core_tools.Debug.Console(1, "MSUController", "WARNING: Cannot create Music Controller - no current MSU configuration");
            }
            
            core_tools.Debug.Console(1, "MSUController", "Device controllers initialization complete");
        }

        private void StartServices()
        {
            core_tools.Debug.Console(1, "MSUController", "Starting services");

            // Start Studio Combination Manager (if needed)
            // _combinationManager.Initialize(); // No Initialize() in StudioCombinationManager, so skip

            // Start Device Controllers
            core_tools.Debug.Console(1, "MSUController", "Starting HVAC Controller");
            _hvacController.Initialize();
            
            if (_musicController != null)
            {
                core_tools.Debug.Console(1, "MSUController", "Starting Music Controller");
                _musicController.Initialize();
            }
            else
            {
                core_tools.Debug.Console(1, "MSUController", "Music Controller not available - skipping");
            }
            
            core_tools.Debug.Console(1, "MSUController", "All available services started successfully");
        }

        private void OnConfigurationLoaded(object sender, ConfigurationLoadedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "Configuration loaded event received");
        }

        private void OnStudioCombinationChanged(object sender, StudioCombinationChangedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "Studio combination changed: {0}", e.CombinationType);

            // Synchronize HVAC temperatures for combined studios
            // If you need to synchronize, use e.CombinedMSUs for the list of combined units
            // (No GetCombinationHVACZones in StudioCombinationManager, so this logic may need to be reworked)
            if (e.CombinedMSUs != null && e.CombinedMSUs.Count > 1 && e.CombinedMSUs[0].IsMaster)
            {
                // Example: collect all HVAC zones from combined MSUs
                var hvacZones = new List<byte>();
                foreach (var msu in e.CombinedMSUs)
                {
                    hvacZones.Add(msu.HVACZoneId);
                }
                var currentSetpoint = _hvacController.CurrentSetpoint;
                core_tools.Debug.Console(1, "MSUController", "Synchronizing HVAC for {0} zones at {1:F1}°C", 
                    hvacZones.Count, currentSetpoint);
                _hvacController.SetMultipleZoneTemperatures(hvacZones, currentSetpoint);
            }
        }

        private void OnHVACStatusUpdated(object sender, HVACStatusUpdatedEventArgs e)
        {
            core_tools.Debug.Console(2, "MSUController", "HVAC status updated: Ext Temp={0:F2}°C, OverTemp={1}, Pressure={2}, Voltage={3}, Airflow={4}", 
                e.ExternalTemperature, e.OverTemp, e.PressureFault, e.VoltageFault, e.AirflowBlocked);
        }

        private void OnHVACSetpointChanged(object sender, HVACSetpointChangedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "HVAC setpoint changed: Zone {0} = {1:F1}°C", e.ZoneId, e.Temperature);
        }

        private void OnMusicCatalogUpdated(object sender, MusicCatalogUpdatedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "Music catalog updated: {0} artists loaded, {1} total", e.LoadedArtists, e.TotalArtists);
        }

        private void OnPlaybackStatusUpdated(object sender, PlaybackStatusChangedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "Playback status: {0} - {1} by {2}", 
                e.IsPlaying ? "Playing" : "Stopped", e.TrackName, e.ArtistName);
        }

        private void OnTrackTimeUpdated(object sender, TrackTimeUpdatedEventArgs e)
        {
            core_tools.Debug.Console(2, "MSUController", "Track time: {0}:{1:D2} remaining", 
                e.RemainingTimeSeconds / 60, e.RemainingTimeSeconds % 60);
        }

        public void Dispose()
        {
            core_tools.Debug.Console(1, "MSUController", "Disposing MSU Controller");

            _configManager?.Dispose();
            _userManager?.Dispose();
            // _combinationManager?.Dispose(); // No Dispose() in StudioCombinationManager
            _hvacController?.Dispose();
            _musicController?.Dispose();
        }
    }

    /// <summary>
    /// MSU System Information for display
    /// </summary>
    public class MSUSystemInfo
    {
        public string? MSUName { get; set; }
        public string? MSU_UID { get; set; }
        public string? ProcessorModel { get; set; }
        public string? FirmwareVersion { get; set; }
        public string? MACAddress { get; set; }
        public string? IPAddress { get; set; }
        public string? Hostname { get; set; }
        public string? BuildingAddress { get; set; }
        public int TotalMSUCount { get; set; }
        public TimeSpan SystemUptime { get; set; }
    }

    /// <summary>
    /// Event arguments for MSU initialization complete
    /// </summary>
    public class MSUInitializedEventArgs : EventArgs
    {
        public MSUConfiguration? MSUConfig { get; set; }
        public TimeSpan InitializationTime { get; set; }
    }

    /// <summary>
    /// Event arguments for MSU errors
    /// </summary>
    public class MSUErrorEventArgs : EventArgs
    {
        public string? ErrorMessage { get; set; }
    }
}


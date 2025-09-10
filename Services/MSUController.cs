
using System;
using System.Collections.Generic;
using Crestron.SimplSharp;
using core_tools;
using musicStudioUnit.Configuration;
using musicStudioUnit.Services;
using musicStudioUnit.Devices;

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
        public EnhancedMusicSystemController MusicController => _musicController;

        // Events
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
                // Step 1: Initialize Configuration Manager
                _configManager = new ConfigurationManager(_key + "Config");
                _configManager.ConfigurationLoaded += OnConfigurationLoaded;

                // Step 2: Load Local Configuration
                if (!_configManager.LoadLocalConfiguration())
                {
                    var error = "Failed to load local configuration file";
                    core_tools.Debug.Console(0, "MSUController", error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                _localConfig = _configManager.LocalConfig;

                // Step 3: Load Remote Configuration
                if (!_configManager.LoadRemoteConfiguration())
                {
                    var error = "Failed to load remote configuration file";
                    core_tools.Debug.Console(0, "MSUController", error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                _remoteConfig = _configManager.RemoteConfig;

                // Step 4: Find Current MSU Configuration
                _currentMSUConfig = _configManager.GetCurrentMSUConfiguration();
                if (_currentMSUConfig == null)
                {
                    var error = "Could not find MSU configuration for this processor";
                    core_tools.Debug.Console(0, "MSUController", error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                core_tools.Debug.Console(1, "MSUController", "Found MSU configuration: {0} at ({1},{2})",
                    _currentMSUConfig.MSU_NAME, _currentMSUConfig.X_COORD, _currentMSUConfig.Y_COORD);

                // Step 5: Initialize Core Services
                InitializeCoreServices();

                // Step 6: Initialize Device Controllers
                InitializeDeviceControllers();

                // Step 7: Start all services
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
            _userManager = new UserManager(_key + "UserMgr");

            // Studio Combination Manager
            _combinationManager = new StudioCombinationManager(
                _key + "StudioComboMgr",
                _currentMSUConfig.MSU_UID,
                _currentMSUConfig.X_COORD,
                _currentMSUConfig.Y_COORD,
                _currentMSUConfig.HVACZoneId,
                _remoteConfig.MSUUnits // Assuming this is a Dictionary<string, MusicStudioUnit>
            );
            _combinationManager.CombinationChanged += OnStudioCombinationChanged;
        }

        private void InitializeDeviceControllers()
        {
            core_tools.Debug.Console(1, "MSUController", "Initializing device controllers");

            // HVAC Controller
            _hvacController = new EnhancedHVACController(_key + "HVAC", _localConfig.HVAC);
            _hvacController.StatusUpdated += OnHVACStatusUpdated;
            _hvacController.SetpointChanged += OnHVACSetpointChanged;

            // Music System Controller
            _musicController = new EnhancedMusicSystemController(_key + "Music", _localConfig.DMS, _currentMSUConfig.MSU_UID);
            _musicController.CatalogUpdated += OnMusicCatalogUpdated;
            _musicController.PlaybackUpdated += OnPlaybackStatusUpdated;
            _musicController.TimeUpdated += OnTrackTimeUpdated;
        }

        private void StartServices()
        {
            core_tools.Debug.Console(1, "MSUController", "Starting services");

            // Start Studio Combination Manager (if needed)
            // _combinationManager.Initialize(); // No Initialize() in StudioCombinationManager, so skip

            // Start Device Controllers
            _hvacController.Initialize();
            _musicController.Initialize();
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

        private void OnHVACStatusUpdated(object sender, musicStudioUnit.HvacController.HVACStatusUpdatedEventArgs e)
        {
            core_tools.Debug.Console(2, "MSUController", "HVAC status updated: Connected={0}", e.Status?.IsConnected);
        }

        private void OnHVACSetpointChanged(object sender, musicStudioUnit.HvacController.HVACSetpointChangedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "HVAC setpoint changed: Zone {0} = {1:F1}°C", e.ZoneId, e.Setpoint);
        }

        private void OnMusicCatalogUpdated(object sender, MusicCatalogUpdatedEventArgs e)
        {
            core_tools.Debug.Console(1, "MSUController", "Music catalog updated: {0} artists", e.ArtistCount);
        }

        private void OnPlaybackStatusUpdated(object sender, musicStudioUnit.MusicSystemController.PlaybackStatusUpdatedEventArgs e)
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
        public string MSUName { get; set; }
        public string MSU_UID { get; set; }
        public string ProcessorModel { get; set; }
        public string FirmwareVersion { get; set; }
        public string MACAddress { get; set; }
        public string IPAddress { get; set; }
        public string Hostname { get; set; }
        public string BuildingAddress { get; set; }
        public int TotalMSUCount { get; set; }
        public TimeSpan SystemUptime { get; set; }
    }

    /// <summary>
    /// Event arguments for MSU initialization complete
    /// </summary>
    public class MSUInitializedEventArgs : EventArgs
    {
        public MSUConfiguration MSUConfig { get; set; }
        public TimeSpan InitializationTime { get; set; }
    }

    /// <summary>
    /// Event arguments for MSU errors
    /// </summary>
    public class MSUErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
    }
}


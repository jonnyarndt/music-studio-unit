using System;
using System.Collections.Generic;
using Crestron.SimplSharp;
using core_tools;
using flexpod.Configuration;
using flexpod.Services;
using flexpod.Devices;

namespace flexpod.Services
{
    /// <summary>
    /// Main MSU Controller that coordinates all system components
    /// </summary>
    public class MSUController : IKeyName, IDisposable
    {
        private readonly string _key;
        
        // Core Services
        private ConfigurationManager _configManager;
        private MSUIdentificationService _identificationService;
        private UserManager _userManager;
        private StudioManager _studioManager;
        
        // Device Controllers
        private HVACController _hvacController;
        private MusicSystemController _musicController;
        
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
        public StudioManager StudioManager => _studioManager;
        public HVACController HVACController => _hvacController;
        public MusicSystemController MusicController => _musicController;

        // Events
        public event EventHandler<MSUInitializedEventArgs> MSUInitialized;
        public event EventHandler<MSUErrorEventArgs> MSUError;

        public MSUController(string key, ConfigurationManager configManager)
        {
            _key = key;
            _systemStartTime = DateTime.Now;
            _configManager = configManager;
            
            DeviceManager.AddDevice(key, this);
            Debug.Console(1, this, "MSU Controller created");
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
                Debug.Console(1, this, "MSU identification data set: {0}", _currentMSUConfig.MSU_NAME);
            }
        }

        /// <summary>
        /// Initialize the MSU system
        /// </summary>
        public bool Initialize()
        {
            Debug.Console(1, this, "Initializing MSU Controller");

            try
            {
                // Step 1: Initialize Configuration Manager
                _configManager = new ConfigurationManager(_key + "Config");
                _configManager.ConfigurationLoaded += OnConfigurationLoaded;

                // Step 2: Load Local Configuration
                if (!_configManager.LoadLocalConfiguration())
                {
                    var error = "Failed to load local configuration file";
                    Debug.Console(0, this, error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                _localConfig = _configManager.LocalConfig;

                // Step 3: Load Remote Configuration
                if (!_configManager.LoadRemoteConfiguration())
                {
                    var error = "Failed to load remote configuration file";
                    Debug.Console(0, this, error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                _remoteConfig = _configManager.RemoteConfig;

                // Step 4: Find Current MSU Configuration
                _currentMSUConfig = _configManager.GetCurrentMSUConfiguration();
                if (_currentMSUConfig == null)
                {
                    var error = "Could not find MSU configuration for this processor";
                    Debug.Console(0, this, error);
                    MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                    return false;
                }

                Debug.Console(1, this, "Found MSU configuration: {0} at ({1},{2})",
                    _currentMSUConfig.MSU_NAME, _currentMSUConfig.X_COORD, _currentMSUConfig.Y_COORD);

                // Step 5: Initialize Core Services
                InitializeCoreServices();

                // Step 6: Initialize Device Controllers
                InitializeDeviceControllers();

                // Step 7: Start all services
                StartServices();

                _isInitialized = true;
                
                Debug.Console(1, this, "MSU Controller initialization complete");

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
                Debug.Console(0, this, error);
                MSUError?.Invoke(this, new MSUErrorEventArgs { ErrorMessage = error });
                return false;
            }
        }

        /// <summary>
        /// Reload configuration files
        /// </summary>
        public bool ReloadConfiguration()
        {
            Debug.Console(1, this, "Reloading MSU configuration");

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
                Debug.Console(0, this, "Error reloading configuration: {0}", ex.Message);
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
                ProcessorModel = sysInfo.Processor.ProcessorType,
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
            Debug.Console(1, this, "Initializing core services");

            // User Manager
            _userManager = new UserManager(_key + "UserMgr");

            // Studio Manager
            _studioManager = new StudioManager(_key + "StudioMgr", _currentMSUConfig, _remoteConfig);
            _studioManager.CombinationChanged += OnStudioCombinationChanged;
        }

        private void InitializeDeviceControllers()
        {
            Debug.Console(1, this, "Initializing device controllers");

            // HVAC Controller
            _hvacController = new HVACController(_key + "HVAC", _localConfig.HVAC);
            _hvacController.StatusUpdated += OnHVACStatusUpdated;
            _hvacController.SetpointChanged += OnHVACSetpointChanged;

            // Music System Controller
            _musicController = new MusicSystemController(_key + "Music", _localConfig.DMS, _currentMSUConfig.MSU_UID);
            _musicController.CatalogUpdated += OnMusicCatalogUpdated;
            _musicController.PlaybackUpdated += OnPlaybackStatusUpdated;
            _musicController.TimeUpdated += OnTrackTimeUpdated;
        }

        private void StartServices()
        {
            Debug.Console(1, this, "Starting services");

            // Start Studio Manager
            _studioManager.Initialize();

            // Start Device Controllers
            _hvacController.Initialize();
            _musicController.Initialize();
        }

        private void OnConfigurationLoaded(object sender, ConfigurationLoadedEventArgs e)
        {
            Debug.Console(1, this, "Configuration loaded event received");
        }

        private void OnStudioCombinationChanged(object sender, StudioCombinationChangedEventArgs e)
        {
            Debug.Console(1, this, "Studio combination changed: {0}", e.CombinationType);

            // Synchronize HVAC temperatures for combined studios
            if (e.IsMainController && e.CombinedMSUs.Count > 1)
            {
                var hvacZones = _studioManager.GetCombinationHVACZones();
                var currentSetpoint = _hvacController.CurrentSetpoint;
                
                Debug.Console(1, this, "Synchronizing HVAC for {0} zones at {1:F1}°C", 
                    hvacZones.Count, currentSetpoint);
                
                _hvacController.SetMultipleZoneTemperatures(hvacZones, currentSetpoint);
            }
        }

        private void OnHVACStatusUpdated(object sender, HVACStatusUpdatedEventArgs e)
        {
            Debug.Console(2, this, "HVAC status updated: Temp={0:F2}°C", e.ExternalTemperature);
        }

        private void OnHVACSetpointChanged(object sender, HVACSetpointChangedEventArgs e)
        {
            Debug.Console(1, this, "HVAC setpoint changed: Zone {0} = {1:F1}°C", e.ZoneId, e.Temperature);
        }

        private void OnMusicCatalogUpdated(object sender, MusicCatalogUpdatedEventArgs e)
        {
            Debug.Console(1, this, "Music catalog updated: {0} artists", e.ArtistCount);
        }

        private void OnPlaybackStatusUpdated(object sender, PlaybackStatusUpdatedEventArgs e)
        {
            Debug.Console(1, this, "Playback status: {0} - {1} by {2}", 
                e.IsPlaying ? "Playing" : "Stopped", e.TrackName, e.ArtistName);
        }

        private void OnTrackTimeUpdated(object sender, TrackTimeUpdatedEventArgs e)
        {
            Debug.Console(2, this, "Track time: {0}:{1:D2} remaining", 
                e.RemainingTimeSeconds / 60, e.RemainingTimeSeconds % 60);
        }

        public void Dispose()
        {
            Debug.Console(1, this, "Disposing MSU Controller");

            _configManager?.Dispose();
            _userManager?.Dispose();
            _studioManager?.Dispose();
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

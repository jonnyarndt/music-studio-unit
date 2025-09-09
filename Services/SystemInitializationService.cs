using System;
using PepperDash.Core;
using System.Collections.Generic;
using PepperDash.Core;
using Crestron.SimplSharp;
using core_tools;
using musicStudioUnit.Configuration;
using musicStudioUnit.Services;

namespace musicStudioUnit.Services
{
    /// <summary>
    /// System Initialization Service - Orchestrates the complete MSU startup sequence
    /// Based on Client-Scope.md requirements for proper initialization order
    /// </summary>
    public class SystemInitializationService : IKeyName, IDisposable
    {
        private readonly string _key;
        
        // Core services
        private ConfigurationManager _configManager;
        private MSUIdentificationService _identificationService;
        private MSUController _msuController;
        
        // Initialization state tracking
        private bool _isInitialized;
        private DateTime _initStartTime;
        private InitializationPhase _currentPhase;
        private List<string> _initializationErrors;

        public string Key => _key;
        public string Name => "System Initialization Service";

        // Properties
        public bool IsInitialized => _isInitialized;
        public InitializationPhase CurrentPhase => _currentPhase;
        public MSUController MSUController => _msuController;
        public List<string> InitializationErrors => _initializationErrors;

        // Events
        public event EventHandler<InitializationPhaseEventArgs> PhaseChanged;
        public event EventHandler<InitializationCompleteEventArgs> InitializationComplete;
        public event EventHandler<InitializationErrorEventArgs> InitializationError;

        public SystemInitializationService(string key)
        {
            _key = key;
            _initializationErrors = new List<string>();
            _currentPhase = InitializationPhase.NotStarted;
            
            DeviceManager.AddDevice(key, this);
            Debug.Console(1, this, "System Initialization Service created");
        }

        /// <summary>
        /// Execute the complete system initialization sequence
        /// Following the order specified in Client-Scope.md
        /// </summary>
        public bool Initialize()
        {
            try
            {
                _initStartTime = DateTime.Now;
                Debug.Console(0, "====== MASTERS OF KARAOKE MSU INITIALIZATION SEQUENCE START ======");
                
                // Phase 1: Configuration Loading
                if (!ExecutePhase1_ConfigurationLoading())
                    return false;

                // Phase 2: MSU Identification
                if (!ExecutePhase2_MSUIdentification())
                    return false;

                // Phase 3: Core Services Initialization
                if (!ExecutePhase3_CoreServices())
                    return false;

                // Phase 4: Device Controllers Initialization
                if (!ExecutePhase4_DeviceControllers())
                    return false;

                // Phase 5: System Validation and Finalization
                if (!ExecutePhase5_SystemValidation())
                    return false;

                // Initialization complete
                _isInitialized = true;
                var totalTime = DateTime.Now - _initStartTime;
                
                Debug.Console(0, "====== MSU INITIALIZATION COMPLETE ({0:F1}s) ======", totalTime.TotalSeconds);
                
                InitializationComplete?.Invoke(this, new InitializationCompleteEventArgs
                {
                    InitializationTime = totalTime,
                    MSUController = _msuController,
                    IdentifiedMSU = _identificationService?.IdentifiedMSU
                });

                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("System initialization failed: {0}", ex.Message);
                Debug.Console(0, this, error);
                RecordError(error);
                InitializationError?.Invoke(this, new InitializationErrorEventArgs { ErrorMessage = error });
                return false;
            }
        }

        /// <summary>
        /// Phase 1: Load local XML configuration and attempt remote JSON configuration
        /// </summary>
        private bool ExecutePhase1_ConfigurationLoading()
        {
            SetPhase(InitializationPhase.ConfigurationLoading);
            Debug.Console(1, "PHASE 1: Configuration Loading");

            try
            {
                // Initialize Configuration Manager
                _configManager = new ConfigurationManager(_key + "Config");
                if (_configManager == null)
                {
                    RecordError("Failed to create Configuration Manager");
                    return false;
                }

                // Load local XML configuration (required)
                Debug.Console(1, "Loading local XML configuration (msu.xml)");
                if (!_configManager.LoadLocalConfiguration())
                {
                    RecordError("Failed to load local XML configuration file (msu.xml)");
                    return false;
                }

                var localConfig = _configManager.LocalConfig;
                Debug.Console(1, "Local configuration loaded successfully");
                Debug.Console(1, "Building Address: {0}, {1}", localConfig.Address?.Street, localConfig.Address?.City);
                Debug.Console(1, "Remote Server: {0}:{1}/{2}", localConfig.Remote?.IP, localConfig.Remote?.Port, localConfig.Remote?.File);

                // Attempt remote JSON configuration (optional but preferred)
                Debug.Console(1, "Attempting remote JSON configuration load");
                if (_configManager.LoadRemoteConfiguration())
                {
                    var remoteConfig = _configManager.RemoteConfig;
                    Debug.Console(1, "Remote configuration loaded successfully");
                    Debug.Console(1, "Found {0} MSU units in building configuration", remoteConfig.MSUUnits?.Count ?? 0);
                }
                else
                {
                    var warning = "Remote configuration failed - will operate with local configuration only";
                    Debug.Console(0, warning);
                    _initializationErrors.Add(warning);
                    // Continue with initialization even without remote config
                }

                Debug.Console(1, "PHASE 1: Configuration Loading - COMPLETE");
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("Phase 1 failed: {0}", ex.Message);
                RecordError(error);
                return false;
            }
        }

        /// <summary>
        /// Phase 2: Identify this MSU using processor MAC address
        /// </summary>
        private bool ExecutePhase2_MSUIdentification()
        {
            SetPhase(InitializationPhase.MSUIdentification);
            Debug.Console(1, "PHASE 2: MSU Identification");

            try
            {
                // Initialize MSU Identification Service
                _identificationService = new MSUIdentificationService(_key + "Identification");
                if (!_identificationService.Initialize())
                {
                    RecordError("Failed to initialize MSU Identification Service");
                    return false;
                }

                // Perform MSU identification
                Debug.Console(1, "Performing MSU identification using processor MAC address");
                if (!_identificationService.IdentifyMSU(_configManager.LocalConfig, _configManager.RemoteConfig))
                {
                    // If remote config failed, we still continue but with limited functionality
                    if (_configManager.RemoteConfig == null)
                    {
                        var warning = "MSU identification skipped - no remote configuration available";
                        Debug.Console(0, warning);
                        _initializationErrors.Add(warning);
                        Debug.Console(1, "PHASE 2: MSU Identification - SKIPPED (will run as standalone)");
                        return true;
                    }
                    else
                    {
                        RecordError("MSU identification failed despite having remote configuration");
                        return false;
                    }
                }

                var identifiedMSU = _identificationService.IdentifiedMSU;
                Debug.Console(1, "MSU Successfully Identified:");
                Debug.Console(1, "  Name: {0}", identifiedMSU.MSU_NAME);
                Debug.Console(1, "  UID: {0}", identifiedMSU.MSU_UID);
                Debug.Console(1, "  Coordinates: ({0}, {1})", identifiedMSU.X_COORD, identifiedMSU.Y_COORD);
                Debug.Console(1, "  HVAC Zone ID: {0}", identifiedMSU.HVAC_ID);

                // Validate MSU configuration completeness
                if (!_identificationService.ValidateMSUConfiguration())
                {
                    RecordError("MSU configuration validation failed");
                    return false;
                }

                Debug.Console(1, "PHASE 2: MSU Identification - COMPLETE");
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("Phase 2 failed: {0}", ex.Message);
                RecordError(error);
                return false;
            }
        }

        /// <summary>
        /// Phase 3: Initialize core services (User Manager, Studio Manager)
        /// </summary>
        private bool ExecutePhase3_CoreServices()
        {
            SetPhase(InitializationPhase.CoreServices);
            Debug.Console(1, "PHASE 3: Core Services Initialization");

            try
            {
                // Initialize MSU Controller
                Debug.Console(1, "Initializing MSU Controller");
                _msuController = new MSUController(_key + "MSUController", _configManager);
                if (!_msuController.Initialize())
                {
                    RecordError("Failed to initialize MSU Controller");
                    return false;
                }

                // Pass identification service to MSU controller if available
                if (_identificationService?.IsIdentified == true)
                {
                    _msuController.SetIdentificationService(_identificationService);
                    Debug.Console(1, "MSU identification data provided to controller");
                }

                Debug.Console(1, "PHASE 3: Core Services - COMPLETE");
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("Phase 3 failed: {0}", ex.Message);
                RecordError(error);
                return false;
            }
        }

        /// <summary>
        /// Phase 4: Initialize device controllers (HVAC, Music)
        /// </summary>
        private bool ExecutePhase4_DeviceControllers()
        {
            SetPhase(InitializationPhase.DeviceControllers);
            Debug.Console(1, "PHASE 4: Device Controllers Initialization");

            try
            {
                // Device controller initialization is handled by MSU Controller
                // This phase validates that they initialized correctly
                
                var hvacController = _msuController.HVACController;
                var musicController = _msuController.MusicController;

                if (hvacController == null)
                {
                    var warning = "HVAC Controller not initialized - HVAC functionality will be disabled";
                    Debug.Console(0, warning);
                    _initializationErrors.Add(warning);
                }
                else
                {
                    Debug.Console(1, "HVAC Controller initialized successfully");
                }

                if (musicController == null)
                {
                    var warning = "Music Controller not initialized - Music functionality will be disabled";
                    Debug.Console(0, warning);
                    _initializationErrors.Add(warning);
                }
                else
                {
                    Debug.Console(1, "Music Controller initialized successfully");
                }

                Debug.Console(1, "PHASE 4: Device Controllers - COMPLETE");
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("Phase 4 failed: {0}", ex.Message);
                RecordError(error);
                return false;
            }
        }

        /// <summary>
        /// Phase 5: System validation and final checks
        /// </summary>
        private bool ExecutePhase5_SystemValidation()
        {
            SetPhase(InitializationPhase.SystemValidation);
            Debug.Console(1, "PHASE 5: System Validation");

            try
            {
                // Validate MSU Controller state
                if (_msuController == null || !_msuController.IsInitialized)
                {
                    RecordError("MSU Controller is not properly initialized");
                    return false;
                }

                // Validate configuration availability
                if (_configManager?.LocalConfig == null)
                {
                    RecordError("Local configuration is not available");
                    return false;
                }

                // Check for any critical errors that would prevent operation
                int criticalErrors = 0;
                foreach (var error in _initializationErrors)
                {
                    if (error.Contains("Failed") || error.Contains("not available"))
                        criticalErrors++;
                }

                if (criticalErrors > 2) // Allow some non-critical failures
                {
                    RecordError(string.Format("Too many critical errors ({0}) - system may not function properly", criticalErrors));
                }

                // Display final system status
                Debug.Console(1, "System Validation Results:");
                Debug.Console(1, "  Configuration Manager: {0}", _configManager != null ? "OK" : "FAILED");
                Debug.Console(1, "  MSU Identification: {0}", _identificationService?.IsIdentified == true ? "OK" : "DISABLED");
                Debug.Console(1, "  MSU Controller: {0}", _msuController?.IsInitialized == true ? "OK" : "FAILED");
                Debug.Console(1, "  Initialization Errors: {0}", _initializationErrors.Count);

                if (_initializationErrors.Count > 0)
                {
                    Debug.Console(1, "Initialization warnings/errors:");
                    foreach (var error in _initializationErrors)
                    {
                        Debug.Console(1, "  - {0}", error);
                    }
                }

                Debug.Console(1, "PHASE 5: System Validation - COMPLETE");
                return true;
            }
            catch (Exception ex)
            {
                var error = string.Format("Phase 5 failed: {0}", ex.Message);
                RecordError(error);
                return false;
            }
        }

        /// <summary>
        /// Record an error and set the current phase
        /// </summary>
        private void SetPhase(InitializationPhase phase)
        {
            _currentPhase = phase;
            PhaseChanged?.Invoke(this, new InitializationPhaseEventArgs { Phase = phase });
        }

        /// <summary>
        /// Record an initialization error
        /// </summary>
        private void RecordError(string error)
        {
            Debug.Console(0, this, "INIT ERROR: {0}", error);
            _initializationErrors.Add(error);
        }

        /// <summary>
        /// Force reload of configuration and re-identification
        /// </summary>
        public bool ReloadConfiguration()
        {
            try
            {
                Debug.Console(1, this, "Reloading configuration");

                // Reload configurations
                if (_configManager != null)
                {
                    if (_configManager.LoadLocalConfiguration())
                    {
                        Debug.Console(1, this, "Local configuration reloaded");
                        _configManager.LoadRemoteConfiguration(); // Optional
                    }
                }

                // Re-identify MSU if possible
                if (_identificationService != null && _configManager != null)
                {
                    _identificationService.IdentifyMSU(_configManager.LocalConfig, _configManager.RemoteConfig);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Configuration reload failed: {0}", ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            _configManager?.Dispose();
            _identificationService?.Dispose();
            _msuController?.Dispose();

            if (DeviceManager.ContainsKey(Key))
                DeviceManager.RemoveDevice(Key);
        }
    }

    /// <summary>
    /// Initialization phases for tracking progress
    /// </summary>
    public enum InitializationPhase
    {
        NotStarted,
        ConfigurationLoading,
        MSUIdentification,
        CoreServices,
        DeviceControllers,
        SystemValidation,
        Complete,
        Failed
    }

    /// <summary>
    /// Event arguments for phase changes
    /// </summary>
    public class InitializationPhaseEventArgs : EventArgs
    {
        public InitializationPhase Phase { get; set; }
    }

    /// <summary>
    /// Event arguments for initialization completion
    /// </summary>
    public class InitializationCompleteEventArgs : EventArgs
    {
        public TimeSpan InitializationTime { get; set; }
        public MSUController MSUController { get; set; }
        public MSUConfiguration IdentifiedMSU { get; set; }
    }

    /// <summary>
    /// Event arguments for initialization errors
    /// </summary>
    public class InitializationErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
    }
}

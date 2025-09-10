using core_tools;
using System.Globalization;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Configuration;
using musicStudioUnit.Services;
using System;
namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// Settings Screen UI Handler for MSU
    /// Displays system information and configuration details per Client-Scope.md
    /// Shows on boot and provides configuration reload functionality
    /// </summary>
    public class SettingsScreenUI : IDisposable
    {
        /// <summary>
        /// Show the settings screen (entry point from MSU TouchPanel)
        /// </summary>
        public void Show()
        {
            // Optionally update display or make visible
            UpdateSettingsDisplay();
            Debug.Console(1, "SettingsScreenUI", "Settings screen shown");
        }

        /// <summary>
        /// Hide the settings screen
        /// </summary>
        public void Hide()
        {
            // Optionally clear display or make invisible
            Debug.Console(1, "SettingsScreenUI", "Settings screen hidden");
        }
        private readonly BasicTriList _panel;
        private readonly MSUController _msuController;
        private readonly SystemInitializationService _initService;
        private CTimer _timeUpdateTimer;

        // Update intervals
        private const int TimeUpdateInterval = 1000; // Update time every second

        public event EventHandler<ConfigurationReloadEventArgs> ConfigurationReloadRequested;

        public SettingsScreenUI(BasicTriList panel, MSUController msuController, 
                               SystemInitializationService initService)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _msuController = msuController ?? throw new ArgumentNullException(nameof(msuController));
            _initService = initService ?? throw new ArgumentNullException(nameof(initService));

            Debug.Console(1, "SettingsScreenUI", "Initializing Settings screen UI");

            // Setup event handlers
            SetupTouchPanelEvents();

            // Subscribe to configuration events
            if (_initService != null)
            {
                _initService.ConfigurationLoaded += OnConfigurationLoaded;
                _initService.ConfigurationError += OnConfigurationError;
            }

            // Start time update timer
            StartTimeUpdateTimer();

            // Initial UI update
            UpdateSettingsDisplay();

            Debug.Console(1, "SettingsScreenUI", "Settings screen UI initialized successfully");
        }

        /// <summary>
        /// Setup touch panel button events
        /// </summary>
        private void SetupTouchPanelEvents()
        {
        
            _panel.SigChange += (device, args) =>
            {
                if (args.Sig.Number == (uint)MSUTouchPanelJoins.SettingsScreen.ReloadConfigButton && 
                    args.Sig.BoolValue)
                {
                    OnReloadConfigurationPressed();
                }
            };

            Debug.Console(2, "SettingsScreenUI", "Touch panel events configured");
        }

        /// <summary>
        /// Start timer for time/date updates
        /// </summary>
        private void StartTimeUpdateTimer()
        {
            _timeUpdateTimer = new CTimer(UpdateTimeDisplay, null, 
                TimeUpdateInterval, TimeUpdateInterval);
        }

        /// <summary>
        /// Handle reload configuration button press
        /// </summary>
        private void OnReloadConfigurationPressed()
        {
            Debug.Console(1, "SettingsScreenUI", "Configuration reload requested by user");
            
            try
            {
                // Update status
                UpdateConfigurationStatus("Reloading configuration...");

                // Trigger configuration reload event
                ConfigurationReloadRequested?.Invoke(this, EventArgs.Empty);

                // The actual reload will be handled by the initialization service
                if (_initService != null)
                {
                    _initService.ReloadConfiguration();
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error requesting configuration reload: {0}", ex.Message);
                UpdateConfigurationStatus($"Reload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Update time and date display (called by timer)
        /// </summary>
        private void UpdateTimeDisplay(object state = null)
        {
            try
            {
                var now = DateTime.Now;

                // Current time (12 hour format - H:mm a/p)
                string timeText = now.ToString("h:mm tt", CultureInfo.InvariantCulture);
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.CurrentTimeText].StringValue = timeText;

                // Current date (long format - day of week, month, day, year)
                string dateText = now.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.CurrentDateText].StringValue = dateText;
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error updating time display: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update all settings display information
        /// </summary>
        public void UpdateSettingsDisplay()
        {
            try
            {
                Debug.Console(2, "SettingsScreenUI", "Updating settings display");

                // Time and date (updated by timer)
                UpdateTimeDisplay();

                // MSU Information
                UpdateMSUInformation();

                // Processor Information  
                UpdateProcessorInformation();

                // Building Information
                UpdateBuildingInformation();

                // Configuration Status
                UpdateConfigurationStatus("Configuration loaded successfully");

                Debug.Console(2, "SettingsScreenUI", "Settings display updated successfully");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error updating settings display: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update MSU-specific information
        /// </summary>
        private void UpdateMSUInformation()
        {
            try
            {
                var config = _msuController?.GetCurrentConfiguration();
                if (config?.LocalConfig != null)
                {
                    
                    // MSU Name from remote configuration
                    var remoteConfig = _msuController.GetRemoteConfiguration();
                    var msuInfo = remoteConfig?.GetMSUByMAC(InitializationManager.ProcessorMAC);
                    
                    string msuName = msuInfo?.MSU_NAME ?? "Unknown MSU";
                    _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.MSUNameText].StringValue = msuName;

                    // MSU UID (processor MAC address)
                    string msuUID = InitializationManager.ProcessorMAC ?? "Unknown";
                    _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.MSUUIDText].StringValue = msuUID;
                }
                else
                {
                    _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.MSUNameText].StringValue = "Configuration Loading...";
                    _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.MSUUIDText].StringValue = "Loading...";
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error updating MSU information: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update processor information
        /// </summary>
        private void UpdateProcessorInformation()
        {
            try
            {
                // Processor model (should be RMC4 per Client-Scope.md)
                var processorInfo = InitializationManager.ProcessorInformation;
                string processorModel = processorInfo?.Series ?? "RMC4";
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.ProcessorModelText].StringValue = processorModel;

                // Firmware version
                string firmwareVersion = processorInfo?.Version ?? CrestronEnvironment.OSVersion.ToString();
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.FirmwareVersionText].StringValue = firmwareVersion;

                // Processor MAC address
                string processorMAC = InitializationManager.ProcessorMAC ?? "Unknown";
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.ProcessorMACText].StringValue = processorMAC;

                // Current IP address
                string currentIP = InitializationManager.GetCurrentIPAddress() ?? "DHCP Pending";
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.ProcessorIPText].StringValue = currentIP;
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error updating processor information: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update building information from configuration
        /// </summary>
        private void UpdateBuildingInformation()
        {
            try
            {
                var config = _msuController?.GetCurrentConfiguration();
                if (config?.LocalConfig?.Address != null)
                {
                    // Building address
                    var address = config.LocalConfig.Address;
                    string buildingAddress = $"{address.Street}, {address.City}";
                    _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.BuildingAddressText].StringValue = buildingAddress;
                }
                else
                {
                    _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.BuildingAddressText].StringValue = "Address not configured";
                }

                // Number of MSUs in building
                var remoteConfig = _msuController?.GetRemoteConfiguration();
                int msuCount = remoteConfig?.MSUUnits?.Count ?? 0;
                string msuCountText = msuCount > 0 ? $"{msuCount} MSUs" : "MSU count unknown";
                _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.MSUCountText].StringValue = msuCountText;
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error updating building information: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update configuration status message
        /// </summary>
        private void UpdateConfigurationStatus(string status)
        {
            _panel.StringInput[(uint)MSUTouchPanelJoins.SettingsScreen.ConfigStatusText].StringValue = status;
            Debug.Console(1, "SettingsScreenUI", "Configuration status: {0}", status);
        }

        /// <summary>
        /// Handle configuration loaded event
        /// </summary>
        private void OnConfigurationLoaded(object sender, ConfigurationLoadedEventArgs args)
        {
            Debug.Console(1, "SettingsScreenUI", "Configuration loaded - updating display");
            UpdateSettingsDisplay();
        }

        /// <summary>
        /// Handle configuration error event
        /// </summary>
        private void OnConfigurationError(object sender, ConfigurationErrorEventArgs args)
        {
            Debug.Console(0, "SettingsScreenUI", "Configuration error: {0}", args.ErrorMessage);
            UpdateConfigurationStatus($"Configuration error: {args.ErrorMessage}");
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Stop timer
                _timeUpdateTimer?.Stop();
                _timeUpdateTimer?.Dispose();
                _timeUpdateTimer = null;

                // Unsubscribe from events
                if (_initService != null)
                {
                    _initService.ConfigurationLoaded -= OnConfigurationLoaded;
                    _initService.ConfigurationError -= OnConfigurationError;
                }

                Debug.Console(1, "SettingsScreenUI", "Settings screen UI disposed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "SettingsScreenUI", "Error disposing: {0}", ex.Message);
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Event arguments for configuration reload requests
    /// </summary>
    public class ConfigurationReloadEventArgs : EventArgs
    {
        public string Reason { get; }

        public ConfigurationReloadEventArgs(string reason)
        {
            Reason = reason;
        }
    }
}

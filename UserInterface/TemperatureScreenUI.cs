using PepperDash.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Configuration;
using musicStudioUnit.Services;
using musicStudioUnit.Devices;

namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// Enhanced Temperature Control Screen UI Handler for MSU
    /// Extends HVAC Temperature UI with MSU-specific functionality per Client-Scope.md
    /// Handles combination mode synchronization and nonvolatile setpoint storage
    /// </summary>
    public class TemperatureScreenUI : IDisposable
    {
        private readonly BasicTriList _panel;
        private readonly EnhancedHVACController _hvacController;
        private readonly MSUController _msuController;
        private readonly Dictionary<byte, float> _currentSetpoints = new Dictionary<byte, float>();
        private readonly List<TemperaturePreset> _presets;
        
        private float _currentDisplayTemp;
        private bool _isUpdatingUI;
        private bool _isCombinedMode = false;
        private List<byte> _controlledZones = new List<byte>();
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        // Nonvolatile storage for setpoints
        private readonly Dictionary<byte, float> _savedSetpoints = new Dictionary<byte, float>();

        // Events
        public event EventHandler<TemperatureChangedEventArgs> TemperatureChanged;
        public event EventHandler<TemperatureFaultEventArgs> TemperatureFault;

        public float CurrentSetpoint => _currentDisplayTemp;
        public bool IsConnected => _hvacController?.IsConnected == true;
        public bool IsCombinedMode => _isCombinedMode;

        public TemperatureScreenUI(BasicTriList panel, EnhancedHVACController hvacController, 
                                  MSUController msuController, List<TemperaturePreset> presets = null)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _hvacController = hvacController ?? throw new ArgumentNullException(nameof(hvacController));
            _msuController = msuController ?? throw new ArgumentNullException(nameof(msuController));
            _presets = presets ?? CreateDefaultPresets();

            PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Initializing Temperature screen UI");

            // Subscribe to HVAC events
            _hvacController.StatusUpdated += OnHVACStatusUpdated;
            _hvacController.SetpointChanged += OnSetpointChanged;
            _hvacController.Connected += OnHVACConnected;
            _hvacController.Disconnected += OnHVACDisconnected;
            _hvacController.HVACError += OnHVACError;

            // Subscribe to MSU combination events
            if (_msuController != null)
            {
                _msuController.CombinationChanged += OnCombinationChanged;
            }

            // Setup touch panel event handlers
            SetupTouchPanelEvents();

            // Load saved setpoints from nonvolatile storage
            LoadSavedSetpoints();

            // Initialize controlled zones
            UpdateControlledZones();

            // Initialize UI display
            UpdateUI();

            PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature screen UI initialized successfully");
        }

        /// <summary>
        /// Setup touch panel button events per MSU joins
        /// </summary>
        private void SetupTouchPanelEvents()
        {
            _panel.SigChange += (device, args) =>
            {
                if (!args.Sig.BoolValue) return; // Only handle button press (true)

                switch (args.Sig.Number)
                {
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.TempUpButton:
                        OnTemperatureUpPressed();
                        break;
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.TempDownButton:
                        OnTemperatureDownPressed();
                        break;
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.PresetButton1:
                        OnPresetButtonPressed(0);
                        break;
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.PresetButton2:
                        OnPresetButtonPressed(1);
                        break;
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.PresetButton3:
                        OnPresetButtonPressed(2);
                        break;
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.PresetButton4:
                        OnPresetButtonPressed(3);
                        break;
                    case (uint)MSUTouchPanelJoins.TemperatureScreen.PresetButton5:
                        OnPresetButtonPressed(4);
                        break;
                }
            };

            PepperDash.Core.PepperDash.Core.Debug.Console(2, "TemperatureScreenUI", "Touch panel events configured");
        }

        /// <summary>
        /// Handle temperature up button press (+0.5°C per Client-Scope.md)
        /// </summary>
        private void OnTemperatureUpPressed()
        {
            lock (_lockObject)
            {
                try
                {
                    float newTemp = _currentDisplayTemp + 0.5f;
                    
                    // Validate against maximum temperature per Client-Scope.md
                    if (newTemp > 50.0f)
                    {
                        PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature {0}°C exceeds maximum (50°C)", newTemp);
                        ShowTemperatureError("Maximum temperature reached");
                        return;
                    }

                    PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature UP: {0:F1}°C -> {1:F1}°C", 
                        _currentDisplayTemp, newTemp);
                    
                    SetTemperature(newTemp);
                }
                catch (Exception ex)
                {
                    PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error handling temperature up: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Handle temperature down button press (-0.5°C per Client-Scope.md)
        /// </summary>
        private void OnTemperatureDownPressed()
        {
            lock (_lockObject)
            {
                try
                {
                    float newTemp = _currentDisplayTemp - 0.5f;
                    
                    // Validate against minimum temperature per Client-Scope.md
                    if (newTemp < -40.0f)
                    {
                        PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature {0}°C below minimum (-40°C)", newTemp);
                        ShowTemperatureError("Minimum temperature reached");
                        return;
                    }

                    PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature DOWN: {0:F1}°C -> {1:F1}°C", 
                        _currentDisplayTemp, newTemp);
                    
                    SetTemperature(newTemp);
                }
                catch (Exception ex)
                {
                    PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error handling temperature down: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Handle preset button press
        /// </summary>
        private void OnPresetButtonPressed(int presetIndex)
        {
            lock (_lockObject)
            {
                try
                {
                    if (presetIndex >= 0 && presetIndex < _presets.Count)
                    {
                        var preset = _presets[presetIndex];
                        PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Preset '{0}' selected: {1:F1}°C", 
                            preset.Name, preset.Temperature);

                        SetTemperature(preset.Temperature);
                    }
                }
                catch (Exception ex)
                {
                    PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error handling preset button: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Set temperature for current zones with nonvolatile storage
        /// </summary>
        private void SetTemperature(float temperature)
        {
            try
            {
                if (!_hvacController.IsConnected)
                {
                    PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Cannot set temperature - HVAC not connected");
                    ShowTemperatureError("HVAC system not connected");
                    return;
                }

                bool success = false;
                
                if (_controlledZones.Count == 1)
                {
                    // Single zone control
                    success = _hvacController.SetZoneTemperature(_controlledZones[0], temperature);
                }
                else if (_controlledZones.Count > 1)
                {
                    // Multiple zones (combination mode)
                    success = _hvacController.SetMultipleZoneTemperatures(_controlledZones, temperature);
                }

                if (success)
                {
                    // Update local display immediately for responsive UI
                    _currentDisplayTemp = temperature;
                    UpdateTemperatureDisplay();

                    // Save setpoint to nonvolatile storage per Client-Scope.md
                    SaveSetpointToNonvolatile(temperature);

                    // Notify listeners
                    TemperatureChanged?.Invoke(this, new TemperatureChangedEventArgs(temperature, _controlledZones));

                    PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature set successfully: {0:F1}°C for zones: {1}", 
                        temperature, string.Join(",", _controlledZones));
                }
                else
                {
                    PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Failed to set temperature");
                    ShowTemperatureError("Failed to set temperature");
                }
            }
            catch (Exception ex)
            {
                PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error setting temperature: {0}", ex.Message);
                ShowTemperatureError("Temperature control error");
            }
        }

        /// <summary>
        /// Update controlled zones based on combination mode
        /// </summary>
        private void UpdateControlledZones()
        {
            try
            {
                _controlledZones.Clear();

                var config = _msuController?.GetCurrentConfiguration();
                var remoteConfig = _msuController?.GetRemoteConfiguration();
                
                if (config?.LocalConfig != null && remoteConfig != null)
                {
                    // Get current MSU configuration
                    var currentMSU = remoteConfig.GetMSUByMAC(InitializationManager.ProcessorMAC);
                    if (currentMSU != null)
                    {
                        // Add primary zone
                        _controlledZones.Add((byte)currentMSU.HVAC_ID);

                        // Add combined zones if in combination mode
                        if (_isCombinedMode)
                        {
                            var combinedMSUs = _msuController.GetCombinedMSUs();
                            foreach (var msu in combinedMSUs.Where(m => m.MSU_UID != currentMSU.MSU_UID))
                            {
                                _controlledZones.Add((byte)msu.HVAC_ID);
                            }
                        }
                    }
                }

                // If no configuration available, use default zone 1
                if (_controlledZones.Count == 0)
                {
                    _controlledZones.Add(1);
                }

                PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Controlled zones updated: {0}", 
                    string.Join(",", _controlledZones));
                
                UpdateZoneDisplay();
            }
            catch (Exception ex)
            {
                PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error updating controlled zones: {0}", ex.Message);
                _controlledZones.Clear();
                _controlledZones.Add(1); // Fallback to zone 1
            }
        }

        /// <summary>
        /// Update zone display text
        /// </summary>
        private void UpdateZoneDisplay()
        {
            try
            {
                string zoneText;
                if (_controlledZones.Count == 1)
                {
                    zoneText = $"Zone {_controlledZones[0]}";
                }
                else
                {
                    zoneText = $"Zones {string.Join(", ", _controlledZones)}";
                }

                _panel.StringInput[(uint)MSUTouchPanelJoins.TemperatureScreen.ZoneDisplayText].StringValue = zoneText;
            }
            catch (Exception ex)
            {
                PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error updating zone display: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Load saved setpoints from nonvolatile storage
        /// </summary>
        private void LoadSavedSetpoints()
        {
            try
            {
                // Use CrestronEnvironment.GetKeyValue for nonvolatile storage
                foreach (var zoneId in _controlledZones)
                {
                    string key = $"MSU_Setpoint_Zone_{zoneId}";
                    string savedValue = CrestronEnvironment.GetKeyValue(key);
                    
                    if (!string.IsNullOrEmpty(savedValue) && float.TryParse(savedValue, out float setpoint))
                    {
                        _savedSetpoints[zoneId] = setpoint;
                        PepperDash.Core.PepperDash.Core.Debug.Console(2, "TemperatureScreenUI", "Loaded saved setpoint for zone {0}: {1:F1}°C", 
                            zoneId, setpoint);
                    }
                    else
                    {
                        // Use idle setpoint from configuration as default
                        var config = _msuController?.GetCurrentConfiguration();
                        float idleSetpoint = config?.LocalConfig?.HVAC?.IdleSetpoint ?? 21.5f;
                        _savedSetpoints[zoneId] = idleSetpoint;
                        PepperDash.Core.PepperDash.Core.Debug.Console(2, "TemperatureScreenUI", "Using idle setpoint for zone {0}: {1:F1}°C", 
                            zoneId, idleSetpoint);
                    }
                }

                // Set current display temp to primary zone setpoint
                if (_controlledZones.Count > 0 && _savedSetpoints.ContainsKey(_controlledZones[0]))
                {
                    _currentDisplayTemp = _savedSetpoints[_controlledZones[0]];
                }
            }
            catch (Exception ex)
            {
                PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error loading saved setpoints: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Save setpoint to nonvolatile storage per Client-Scope.md requirement
        /// </summary>
        private void SaveSetpointToNonvolatile(float temperature)
        {
            try
            {
                foreach (var zoneId in _controlledZones)
                {
                    string key = $"MSU_Setpoint_Zone_{zoneId}";
                    CrestronEnvironment.SetKeyValue(key, temperature.ToString("F1"));
                    _savedSetpoints[zoneId] = temperature;
                    
                    PepperDash.Core.PepperDash.Core.Debug.Console(2, "TemperatureScreenUI", "Saved setpoint for zone {0}: {1:F1}°C", 
                        zoneId, temperature);
                }
            }
            catch (Exception ex)
            {
                PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error saving setpoint to nonvolatile storage: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update UI with current status
        /// </summary>
        private void UpdateUI()
        {
            lock (_lockObject)
            {
                if (_isUpdatingUI) return;
                _isUpdatingUI = true;

                try
                {
                    // Get current HVAC status
                    var status = _hvacController.GetCurrentStatus();

                    // Update temperature displays
                    UpdateTemperatureDisplay();
                    UpdateExternalTemperatureDisplay(status.ExternalTemperature);

                    // Update status indicators per Client-Scope.md
                    UpdateStatusIndicators(status);

                    // Update connection status
                    UpdateConnectionStatus(status.IsConnected);

                    // Update zone display
                    UpdateZoneDisplay();

                    PepperDash.Core.PepperDash.Core.Debug.Console(2, "TemperatureScreenUI", "UI updated successfully");
                }
                catch (Exception ex)
                {
                    PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error updating UI: {0}", ex.Message);
                }
                finally
                {
                    _isUpdatingUI = false;
                }
            }
        }

        /// <summary>
        /// Update temperature display
        /// </summary>
        private void UpdateTemperatureDisplay()
        {
            string tempText = string.Format("{0:F1}°C", _currentDisplayTemp);
            _panel.StringInput[(uint)MSUTouchPanelJoins.TemperatureScreen.CurrentTempText].StringValue = tempText;
            
            // Also update analog value (multiply by 10 for 0.1°C resolution)
            _panel.UShortInput[(uint)MSUTouchPanelJoins.TemperatureScreen.CurrentTempAnalog].UShortValue = 
                (ushort)((_currentDisplayTemp + 40.0f) * 10); // Offset for negative temperatures
            
            PepperDash.Core.PepperDash.Core.Debug.Console(2, "TemperatureScreenUI", "Temperature display updated: {0}", tempText);
        }

        /// <summary>
        /// Update external temperature display with two-digit decimal precision per Client-Scope.md
        /// </summary>
        private void UpdateExternalTemperatureDisplay(float externalTemp)
        {
            string extTempText = string.Format("External: {0:F2}°C", externalTemp);
            _panel.StringInput[(uint)MSUTouchPanelJoins.TemperatureScreen.ExternalTempText].StringValue = extTempText;
            
            // Also update analog value
            _panel.UShortInput[(uint)MSUTouchPanelJoins.TemperatureScreen.ExternalTempAnalog].UShortValue = 
                (ushort)((externalTemp + 40.0f) * 10); // Offset for negative temperatures
        }

        /// <summary>
        /// Update status indicators per Client-Scope.md fault conditions
        /// Display four error flags as on/off with text labels
        /// </summary>
        private void UpdateStatusIndicators(HVACStatus status)
        {
            // Individual fault indicators with text labels per Client-Scope.md
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.TemperatureScreen.OverTempFault].BoolValue = status.OverTemp;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.TemperatureScreen.PressureFault].BoolValue = status.PressureFault;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.TemperatureScreen.VoltageFault].BoolValue = status.VoltageFault;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.TemperatureScreen.AirflowFault].BoolValue = status.AirflowBlocked;

            // Overall status text
            string statusText;
            if (!status.IsConnected)
                statusText = "HVAC Disconnected";
            else if (status.OverTemp)
                statusText = "Over Temperature Fault";
            else if (status.PressureFault)
                statusText = "Pressure Fault";
            else if (status.VoltageFault)
                statusText = "Voltage Fault";
            else if (status.AirflowBlocked)
                statusText = "Airflow Blocked Fault";
            else
                statusText = "Normal Operation";

            _panel.StringInput[(uint)MSUTouchPanelJoins.TemperatureScreen.StatusText].StringValue = statusText;

            // Notify fault events
            if (status.OverTemp || status.PressureFault || status.VoltageFault || status.AirflowBlocked)
            {
                TemperatureFault?.Invoke(this, new TemperatureFaultEventArgs(statusText, status));
            }
        }

        /// <summary>
        /// Update connection status indicator
        /// </summary>
        private void UpdateConnectionStatus(bool isConnected)
        {
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.TemperatureScreen.ConnectedIndicator].BoolValue = isConnected;
        }

        /// <summary>
        /// Show temperature error message
        /// </summary>
        private void ShowTemperatureError(string message)
        {
            _panel.StringInput[(uint)MSUTouchPanelJoins.TemperatureScreen.StatusText].StringValue = message;
            PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature error: {0}", message);
        }

        /// <summary>
        /// Create default temperature presets
        /// </summary>
        private List<TemperaturePreset> CreateDefaultPresets()
        {
            return new List<TemperaturePreset>
            {
                new TemperaturePreset { Name = "Cool", Temperature = 18.0f, Description = "Cool recording" },
                new TemperaturePreset { Name = "Comfort", Temperature = 21.0f, Description = "Comfortable" },
                new TemperaturePreset { Name = "Warm", Temperature = 24.0f, Description = "Warm environment" },
                new TemperaturePreset { Name = "Hot", Temperature = 27.0f, Description = "Hot recording" },
                new TemperaturePreset { Name = "Custom", Temperature = 22.0f, Description = "Custom setting" }
            };
        }

        #region Event Handlers

        private void OnHVACStatusUpdated(object sender, HVACStatusUpdatedEventArgs args)
        {
            UpdateUI();
        }

        private void OnSetpointChanged(object sender, HVACSetpointChangedEventArgs args)
        {
            if (_controlledZones.Contains(args.ZoneId))
            {
                _currentSetpoints[args.ZoneId] = args.Temperature;
                _currentDisplayTemp = args.Temperature;
                UpdateTemperatureDisplay();
            }
        }

        private void OnHVACConnected(object sender, HVACConnectedEventArgs args)
        {
            PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "HVAC connected - updating UI");
            UpdateUI();
        }

        private void OnHVACDisconnected(object sender, HVACDisconnectedEventArgs args)
        {
            PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "HVAC disconnected - updating UI");
            UpdateUI();
        }

        private void OnHVACError(object sender, HVACErrorEventArgs args)
        {
            PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "HVAC error: {0}", args.ErrorMessage);
            ShowTemperatureError(args.ErrorMessage);
        }

        private void OnCombinationChanged(object sender, CombinationChangedEventArgs args)
        {
            PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Combination changed - updating controlled zones");
            _isCombinedMode = args.IsCombined;
            UpdateControlledZones();
            UpdateUI();
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from HVAC events
                if (_hvacController != null)
                {
                    _hvacController.StatusUpdated -= OnHVACStatusUpdated;
                    _hvacController.SetpointChanged -= OnSetpointChanged;
                    _hvacController.Connected -= OnHVACConnected;
                    _hvacController.Disconnected -= OnHVACDisconnected;
                    _hvacController.HVACError -= OnHVACError;
                }

                // Unsubscribe from MSU events
                if (_msuController != null)
                {
                    _msuController.CombinationChanged -= OnCombinationChanged;
                }

                PepperDash.Core.PepperDash.Core.Debug.Console(1, "TemperatureScreenUI", "Temperature screen UI disposed");
            }
            catch (Exception ex)
            {
                PepperDash.Core.PepperDash.Core.Debug.Console(0, "TemperatureScreenUI", "Error disposing: {0}", ex.Message);
            }
            finally
            {
                _disposed = true;
            }
        }
    }

    #region Event Arguments

    public class TemperatureChangedEventArgs : EventArgs
    {
        public float Temperature { get; }
        public List<byte> ZoneIds { get; }

        public TemperatureChangedEventArgs(float temperature, List<byte> zoneIds)
        {
            Temperature = temperature;
            ZoneIds = new List<byte>(zoneIds);
        }
    }

    public class TemperatureFaultEventArgs : EventArgs
    {
        public string FaultMessage { get; }
        public HVACStatus Status { get; }

        public TemperatureFaultEventArgs(string faultMessage, HVACStatus status)
        {
            FaultMessage = faultMessage;
            Status = status;
        }
    }

    public class CombinationChangedEventArgs : EventArgs
    {
        public bool IsCombined { get; }
        public List<MSUConfiguration> CombinedMSUs { get; }

        public CombinationChangedEventArgs(bool isCombined, List<MSUConfiguration> combinedMSUs)
        {
            IsCombined = isCombined;
            CombinedMSUs = combinedMSUs ?? new List<MSUConfiguration>();
        }
    }

    #endregion
}




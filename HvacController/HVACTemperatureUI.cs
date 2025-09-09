using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Configuration;

namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// HVAC Temperature Control UI Handler
    /// Manages touch panel integration for temperature controls per Client-Scope.md
    /// </summary>
    public class HVACTemperatureUI : IDisposable
    {
        private readonly EnhancedHVACController _hvacController;
        private readonly BasicTriList _panel;
        private readonly Dictionary<byte, float> _currentSetpoints = new Dictionary<byte, float>();
        private readonly List<TemperaturePreset> _presets;
        
        // Touch panel join definitions for temperature screen
        private const uint TempDisplayJoin = 301;        // Temperature display text
        private const uint TempUpButtonJoin = 302;       // Temperature up button
        private const uint TempDownButtonJoin = 303;     // Temperature down button
        private const uint ExtTempDisplayJoin = 304;     // External temperature display
        private const uint StatusIconJoin = 305;         // Status icon (normal/fault)
        private const uint StatusTextJoin = 306;         // Status text description
        
        // Status indicator joins
        private const uint OverTempIndicatorJoin = 310;  // Over-temperature warning
        private const uint PressureFaultJoin = 311;      // Pressure fault indicator
        private const uint VoltageFaultJoin = 312;       // Voltage fault indicator
        private const uint AirflowBlockedJoin = 313;     // Airflow blocked indicator
        private const uint ConnectedIndicatorJoin = 314; // Connection status
        
        // Preset buttons (joins 320-329 for up to 10 presets)
        private const uint PresetButtonBaseJoin = 320;
        
        // Current state
        private float _currentDisplayTemp;
        private bool _isUpdatingUI;
        private readonly object _lockObject = new object();

        public EnhancedHVACController HVACController => _hvacController;
        public bool IsConnected => _hvacController?.IsConnected == true;

        public HVACTemperatureUI(EnhancedHVACController hvacController, BasicTriList panel, 
                                List<TemperaturePreset> presets = null)
        {
            _hvacController = hvacController ?? throw new ArgumentNullException(nameof(hvacController));
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _presets = presets ?? CreateDefaultPresets();

            Debug.Console(1, "HVACTemperatureUI", "Initializing HVAC temperature UI");

            // Subscribe to HVAC events
            _hvacController.StatusUpdated += OnHVACStatusUpdated;
            _hvacController.SetpointChanged += OnSetpointChanged;
            _hvacController.Connected += OnHVACConnected;
            _hvacController.Disconnected += OnHVACDisconnected;
            _hvacController.HVACError += OnHVACError;

            // Setup touch panel event handlers
            SetupTouchPanelEvents();

            // Initialize UI display
            UpdateUI();

            Debug.Console(1, "HVACTemperatureUI", "HVAC temperature UI initialized successfully");
        }

        /// <summary>
        /// Setup touch panel button events
        /// </summary>
        private void SetupTouchPanelEvents()
        {
            // Temperature up button (±0.5°C increments per Client-Scope.md)
            _panel.SigChange += (device, args) =>
            {
                if (args.Sig.Number == TempUpButtonJoin && args.Sig.BoolValue)
                {
                    OnTemperatureUpPressed();
                }
                else if (args.Sig.Number == TempDownButtonJoin && args.Sig.BoolValue)
                {
                    OnTemperatureDownPressed();
                }
                // Handle preset buttons
                else if (args.Sig.Number >= PresetButtonBaseJoin && 
                         args.Sig.Number < PresetButtonBaseJoin + _presets.Count && 
                         args.Sig.BoolValue)
                {
                    int presetIndex = (int)(args.Sig.Number - PresetButtonBaseJoin);
                    OnPresetButtonPressed(presetIndex);
                }
            };

            Debug.Console(2, "HVACTemperatureUI", "Touch panel events configured");
        }

        /// <summary>
        /// Handle temperature up button press (+0.5°C)
        /// </summary>
        private void OnTemperatureUpPressed()
        {
            lock (_lockObject)
            {
                try
                {
                    float newTemp = _currentDisplayTemp + 0.5f;
                    
                    // Validate against maximum temperature
                    if (newTemp > 50.0f)
                    {
                        Debug.Console(1, "HVACTemperatureUI", "Temperature {0}°C exceeds maximum (50°C)", newTemp);
                        ShowTemperatureError("Maximum temperature reached");
                        return;
                    }

                    Debug.Console(1, "HVACTemperatureUI", "Temperature UP: {0:F1}°C -> {1:F1}°C", 
                        _currentDisplayTemp, newTemp);

                    SetTemperature(newTemp);
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "HVACTemperatureUI", "Error handling temperature up: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Handle temperature down button press (-0.5°C)
        /// </summary>
        private void OnTemperatureDownPressed()
        {
            lock (_lockObject)
            {
                try
                {
                    float newTemp = _currentDisplayTemp - 0.5f;
                    
                    // Validate against minimum temperature
                    if (newTemp < -40.0f)
                    {
                        Debug.Console(1, "HVACTemperatureUI", "Temperature {0}°C below minimum (-40°C)", newTemp);
                        ShowTemperatureError("Minimum temperature reached");
                        return;
                    }

                    Debug.Console(1, "HVACTemperatureUI", "Temperature DOWN: {0:F1}°C -> {1:F1}°C", 
                        _currentDisplayTemp, newTemp);

                    SetTemperature(newTemp);
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "HVACTemperatureUI", "Error handling temperature down: {0}", ex.Message);
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
                        Debug.Console(1, "HVACTemperatureUI", "Preset '{0}' selected: {1:F1}°C", 
                            preset.Name, preset.Temperature);

                        SetTemperature(preset.Temperature);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "HVACTemperatureUI", "Error handling preset button: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Set temperature for current zones
        /// </summary>
        private void SetTemperature(float temperature)
        {
            try
            {
                if (!_hvacController.IsConnected)
                {
                    Debug.Console(0, "HVACTemperatureUI", "Cannot set temperature - HVAC not connected");
                    ShowTemperatureError("HVAC system not connected");
                    return;
                }

                // TODO: Get current zone IDs from studio configuration
                // For now, use zone 1 as default
                List<byte> currentZones = new List<byte> { 1 };

                bool success;
                if (currentZones.Count == 1)
                {
                    success = _hvacController.SetZoneTemperature(currentZones[0], temperature);
                }
                else
                {
                    success = _hvacController.SetMultipleZoneTemperatures(currentZones, temperature);
                }

                if (success)
                {
                    // Update local display immediately for responsive UI
                    _currentDisplayTemp = temperature;
                    UpdateTemperatureDisplay();
                }
                else
                {
                    Debug.Console(0, "HVACTemperatureUI", "Failed to set temperature");
                    ShowTemperatureError("Failed to set temperature");
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "HVACTemperatureUI", "Error setting temperature: {0}", ex.Message);
                ShowTemperatureError("Temperature control error");
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
                    _currentDisplayTemp = status.CurrentSetpoint;
                    UpdateTemperatureDisplay();
                    UpdateExternalTemperatureDisplay(status.ExternalTemperature);

                    // Update status indicators
                    UpdateStatusIndicators(status);

                    // Update connection status
                    UpdateConnectionStatus(status.IsConnected);

                    Debug.Console(2, "HVACTemperatureUI", "UI updated successfully");
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "HVACTemperatureUI", "Error updating UI: {0}", ex.Message);
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
            _panel.StringInput[TempDisplayJoin].StringValue = tempText;
            Debug.Console(2, "HVACTemperatureUI", "Temperature display updated: {0}", tempText);
        }

        /// <summary>
        /// Update external temperature display
        /// </summary>
        private void UpdateExternalTemperatureDisplay(float externalTemp)
        {
            string extTempText = string.Format("Ext: {0:F1}°C", externalTemp);
            _panel.StringInput[ExtTempDisplayJoin].StringValue = extTempText;
        }

        /// <summary>
        /// Update status indicators per Client-Scope.md fault conditions
        /// </summary>
        private void UpdateStatusIndicators(HVACStatus status)
        {
            // Individual fault indicators
            _panel.BooleanInput[OverTempIndicatorJoin].BoolValue = status.OverTemp;
            _panel.BooleanInput[PressureFaultJoin].BoolValue = status.PressureFault;
            _panel.BooleanInput[VoltageFaultJoin].BoolValue = status.VoltageFault;
            _panel.BooleanInput[AirflowBlockedJoin].BoolValue = status.AirflowBlocked;

            // Overall status
            bool hasAnyFault = status.OverTemp || status.PressureFault || 
                              status.VoltageFault || status.AirflowBlocked;

            _panel.BooleanInput[StatusIconJoin].BoolValue = !hasAnyFault;

            // Status text
            string statusText;
            if (!status.IsConnected)
                statusText = "HVAC Disconnected";
            else if (status.OverTemp)
                statusText = "Over Temperature";
            else if (status.PressureFault)
                statusText = "Pressure Fault";
            else if (status.VoltageFault)
                statusText = "Voltage Fault";
            else if (status.AirflowBlocked)
                statusText = "Airflow Blocked";
            else
                statusText = "Normal Operation";

            _panel.StringInput[StatusTextJoin].StringValue = statusText;
        }

        /// <summary>
        /// Update connection status indicator
        /// </summary>
        private void UpdateConnectionStatus(bool isConnected)
        {
            _panel.BooleanInput[ConnectedIndicatorJoin].BoolValue = isConnected;
        }

        /// <summary>
        /// Show temperature error message
        /// </summary>
        private void ShowTemperatureError(string message)
        {
            // Flash the status text with error message
            _panel.StringInput[StatusTextJoin].StringValue = message;
            
            // TODO: Add timer to clear error message after a few seconds
            Debug.Console(1, "HVACTemperatureUI", "Temperature error: {0}", message);
        }

        /// <summary>
        /// Handle HVAC status updates
        /// </summary>
        private void OnHVACStatusUpdated(object sender, HVACStatusUpdatedEventArgs args)
        {
            Debug.Console(2, "HVACTemperatureUI", "HVAC status updated - updating UI");
            UpdateUI();
        }

        /// <summary>
        /// Handle setpoint changes
        /// </summary>
        private void OnSetpointChanged(object sender, HVACSetpointChangedEventArgs args)
        {
            Debug.Console(1, "HVACTemperatureUI", "Setpoint changed for zone {0}: {1:F1}°C", 
                args.ZoneId, args.Temperature);
            
            _currentSetpoints[args.ZoneId] = args.Temperature;
            _currentDisplayTemp = args.Temperature;
            UpdateTemperatureDisplay();
        }

        /// <summary>
        /// Handle HVAC connection
        /// </summary>
        private void OnHVACConnected(object sender, HVACConnectedEventArgs args)
        {
            Debug.Console(1, "HVACTemperatureUI", "HVAC connected - updating UI");
            UpdateUI();
        }

        /// <summary>
        /// Handle HVAC disconnection
        /// </summary>
        private void OnHVACDisconnected(object sender, HVACDisconnectedEventArgs args)
        {
            Debug.Console(1, "HVACTemperatureUI", "HVAC disconnected - updating UI");
            UpdateUI();
        }

        /// <summary>
        /// Handle HVAC errors
        /// </summary>
        private void OnHVACError(object sender, HVACErrorEventArgs args)
        {
            Debug.Console(0, "HVACTemperatureUI", "HVAC error: {0}", args.ErrorMessage);
            ShowTemperatureError(args.ErrorMessage);
        }

        /// <summary>
        /// Create default temperature presets
        /// </summary>
        private List<TemperaturePreset> CreateDefaultPresets()
        {
            return new List<TemperaturePreset>
            {
                new TemperaturePreset 
                { 
                    Name = "Cool", 
                    Temperature = 18.0f, 
                    Description = "Cool recording temperature",
                    Icon = "cool_icon"
                },
                new TemperaturePreset 
                { 
                    Name = "Comfort", 
                    Temperature = 21.0f, 
                    Description = "Comfortable working temperature",
                    Icon = "comfort_icon"
                },
                new TemperaturePreset 
                { 
                    Name = "Warm", 
                    Temperature = 24.0f, 
                    Description = "Warm environment",
                    Icon = "warm_icon"
                }
            };
        }

        public void Dispose()
        {
            if (_hvacController != null)
            {
                _hvacController.StatusUpdated -= OnHVACStatusUpdated;
                _hvacController.SetpointChanged -= OnSetpointChanged;
                _hvacController.Connected -= OnHVACConnected;
                _hvacController.Disconnected -= OnHVACDisconnected;
                _hvacController.HVACError -= OnHVACError;
            }
        }
    }
}

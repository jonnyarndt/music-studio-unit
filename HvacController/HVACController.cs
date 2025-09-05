using System;
using System.Collections.Generic;
using Crestron.SimplSharp;
using core_tools;
using flexpod.Configuration;

namespace flexpod.Devices
{
    /// <summary>
    /// HVAC Controller for Borden Air Multi-Zone HVAC System
    /// </summary>
    public class HVACController : IKeyName, IDisposable
    {
        private readonly Core_tools_tcpClient _tcpClient;
        private readonly HVACInfo _config;
        private readonly string _key;
        private float _currentSetpoint;
        private float _externalTemperature;
        private byte _statusFlags;
        private readonly Dictionary<byte, float> _zoneSetpoints = new Dictionary<byte, float>();

        public string Key => _key;
        public string Name => "HVAC Controller";

        // Properties for status monitoring
        public float CurrentSetpoint => _currentSetpoint;
        public float ExternalTemperature => _externalTemperature;
        public bool OverTemp => (_statusFlags & 0x01) != 0;
        public bool PressureFault => (_statusFlags & 0x02) != 0;
        public bool VoltageFault => (_statusFlags & 0x04) != 0;
        public bool AirflowBlocked => (_statusFlags & 0x08) != 0;

        // Events
        public event EventHandler<HVACStatusUpdatedEventArgs> StatusUpdated;
        public event EventHandler<HVACSetpointChangedEventArgs> SetpointChanged;

        public HVACController(string key, HVACInfo config)
        {
            _key = key;
            _config = config;

            // Create TCP client
            _tcpClient = new Core_tools_tcpClient(
                key + "Client",
                "HVAC TCP Client",
                config.IP,
                config.Port);

            // Register with device manager
            DeviceManager.AddDevice(key, this);

            // Set initial setpoint from configuration
            _currentSetpoint = config.IdleSetpoint;

            // Register for data received events
            _tcpClient.DataReceived += OnDataReceived;

            Debug.Console(1, this, "HVAC Controller initialized for {0}:{1}", config.IP, config.Port);
        }

        /// <summary>
        /// Set temperature for a specific zone
        /// </summary>
        public void SetZoneTemperature(byte zoneId, float temperature)
        {
            Debug.Console(1, this, "Setting zone {0} temperature to {1:F1}°C", zoneId, temperature);

            try
            {
                // Validate temperature range (-40 to +50°C)
                if (temperature < -40.0f || temperature > 50.0f)
                {
                    Debug.Console(0, this, "Temperature {0}°C is out of range (-40 to +50)", temperature);
                    return;
                }

                // Store the setpoint
                _zoneSetpoints[zoneId] = temperature;
                _currentSetpoint = temperature;

                // Build the command
                byte[] command = BuildSetTemperatureCommand(zoneId, temperature);

                // Send the command
                if (_tcpClient.IsConnected)
                {
                    _tcpClient.Send(command);
                }
                else
                {
                    Debug.Console(1, this, "TCP client not connected. Connecting...");
                    _tcpClient.Connect();
                    CTimer.Wait(100, () => _tcpClient.Send(command));
                }

                // Fire setpoint changed event
                SetpointChanged?.Invoke(this, new HVACSetpointChangedEventArgs
                {
                    ZoneId = zoneId,
                    Temperature = temperature
                });
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting zone temperature: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Set temperature for multiple zones (for combined studios)
        /// </summary>
        public void SetMultipleZoneTemperatures(List<byte> zoneIds, float temperature)
        {
            Debug.Console(1, this, "Setting temperature {0:F1}°C for {1} zones", temperature, zoneIds.Count);

            try
            {
                // Build command for multiple zones
                byte[] command = BuildMultiZoneTemperatureCommand(zoneIds, temperature);

                // Send the command
                if (_tcpClient.IsConnected)
                {
                    _tcpClient.Send(command);
                }
                else
                {
                    _tcpClient.Connect();
                    CTimer.Wait(100, () => _tcpClient.Send(command));
                }

                // Update stored setpoints
                foreach (byte zoneId in zoneIds)
                {
                    _zoneSetpoints[zoneId] = temperature;
                }
                _currentSetpoint = temperature;

                // Fire setpoint changed events
                foreach (byte zoneId in zoneIds)
                {
                    SetpointChanged?.Invoke(this, new HVACSetpointChangedEventArgs
                    {
                        ZoneId = zoneId,
                        Temperature = temperature
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting multiple zone temperatures: {0}", ex.Message);
                throw;
            }
        }

        private byte[] BuildSetTemperatureCommand(byte zoneId, float temperature)
        {
            List<byte> packet = new List<byte>();

            // Calculate temperature bytes
            ushort tempValue = (ushort)((temperature + 50) * 500);
            byte tempLSB = (byte)(tempValue & 0xFF);
            byte tempMSB = (byte)((tempValue >> 8) & 0xFF);

            // Header
            packet.Add(0x1B); // ESC
            packet.Add(0x00); // Length placeholder
            packet.Add(0x4A); // Unit ID byte 1
            packet.Add(0x41); // Unit ID byte 2
            packet.Add(0x31); // Unit ID byte 3
            packet.Add(0x00); // NUL

            // Zone data
            packet.Add(0x10); // DLE
            packet.Add(zoneId); // Zone
            packet.Add(tempLSB); // Setpoint LSB
            packet.Add(tempMSB); // Setpoint MSB
            packet.Add(0x00); // NUL

            // Footer
            packet.Add(0x17); // ETB

            // Update length
            packet[1] = (byte)packet.Count;

            return packet.ToArray();
        }

        private byte[] BuildMultiZoneTemperatureCommand(List<byte> zoneIds, float temperature)
        {
            List<byte> packet = new List<byte>();

            // Calculate temperature bytes
            ushort tempValue = (ushort)((temperature + 50) * 500);
            byte tempLSB = (byte)(tempValue & 0xFF);
            byte tempMSB = (byte)((tempValue >> 8) & 0xFF);

            // Header
            packet.Add(0x1B); // ESC
            packet.Add(0x00); // Length placeholder
            packet.Add(0x4A); // Unit ID byte 1
            packet.Add(0x41); // Unit ID byte 2
            packet.Add(0x31); // Unit ID byte 3
            packet.Add(0x00); // NUL

            // Add zone data for each zone
            foreach (byte zoneId in zoneIds)
            {
                packet.Add(0x10); // DLE
                packet.Add(zoneId); // Zone
                packet.Add(tempLSB); // Setpoint LSB
                packet.Add(tempMSB); // Setpoint MSB
                packet.Add(0x00); // NUL
            }

            // Footer
            packet.Add(0x17); // ETB

            // Update length
            packet[1] = (byte)packet.Count;

            return packet.ToArray();
        }

        private void OnDataReceived(byte[] data)
        {
            try
            {
                Debug.Console(2, this, "HVAC data received: {0} bytes", data.Length);

                if (data.Length >= 6)
                {
                    // Parse external temperature (bytes 2-3)
                    ushort tempValue = (ushort)((data[3] << 8) | data[2]);
                    _externalTemperature = (tempValue / 500.0f) - 50.0f;

                    // Parse status flags (byte 4)
                    _statusFlags = data[4];

                    Debug.Console(2, this, "HVAC Status: Ext Temp={0:F2}°C, Flags={1:X2}",
                        _externalTemperature, _statusFlags);

                    // Notify listeners
                    StatusUpdated?.Invoke(this, new HVACStatusUpdatedEventArgs
                    {
                        ExternalTemperature = _externalTemperature,
                        OverTemp = OverTemp,
                        PressureFault = PressureFault,
                        VoltageFault = VoltageFault,
                        AirflowBlocked = AirflowBlocked
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error processing HVAC data: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Initialize the HVAC controller with the idle setpoint
        /// </summary>
        public void Initialize()
        {
            Debug.Console(1, this, "Initializing HVAC Controller with idle setpoint: {0:F1}°C", _config.IdleSetpoint);
            
            // Connect to HVAC system
            _tcpClient.Connect();
        }

        public void Dispose()
        {
            _tcpClient?.Dispose();
        }
    }

    /// <summary>
    /// Event arguments for HVAC status updates
    /// </summary>
    public class HVACStatusUpdatedEventArgs : EventArgs
    {
        public float ExternalTemperature { get; set; }
        public bool OverTemp { get; set; }
        public bool PressureFault { get; set; }
        public bool VoltageFault { get; set; }
        public bool AirflowBlocked { get; set; }
    }

    /// <summary>
    /// Event arguments for HVAC setpoint changes
    /// </summary>
    public class HVACSetpointChangedEventArgs : EventArgs
    {
        public byte ZoneId { get; set; }
        public float Temperature { get; set; }
    }
}

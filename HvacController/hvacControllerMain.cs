using System;
using System.Collections.Generic;
using System.Text;
using Crestron.SimplSharp;
using core_tools;

namespace Masters.Karaoke.Devices
{
    public class HVACController : IKeyName, IDisposable
    {
        // TCP client for communication
        private readonly Core_tools_tcpClient _tcpClient;
        private readonly HVACInfo _config;
        private readonly string _key;
        private float _currentSetpoint;
        private float _externalTemperature;
        private byte _statusFlags;
        
        // Dictionary to store zone setpoints
        private Dictionary<byte, float> _zoneSetpoints = new Dictionary<byte, float>();
        
        // Events
        public event EventHandler<HVACStatusUpdatedEventArgs> StatusUpdated;
        
        public string Key => _key;
        public string Name => "HVAC Controller";
        
        public float ExternalTemperature => _externalTemperature;
        public bool OverTemp => (_statusFlags & 0x01) != 0;
        public bool PressureFault => (_statusFlags & 0x02) != 0;
        public bool VoltageFault => (_statusFlags & 0x04) != 0;
        public bool AirflowBlocked => (_statusFlags & 0x08) != 0;
        
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
        }
        
        public void SetZoneTemperature(byte zoneId, float temperature)
        {
            Debug.Console(1, this, "Setting zone {0} temperature to {1}°C", zoneId, temperature);
            
            try
            {
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
                    Debug.Console(0, this, "TCP client not connected. Connecting...");
                    _tcpClient.Connect();
                    _tcpClient.Send(command);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting zone temperature: {0}", ex.Message);
                throw;
            }
        }
        
        private byte[] BuildSetTemperatureCommand(byte zoneId, float temperature)
        {
            // Following the HVAC protocol specification
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
        
        private void OnDataReceived(byte[] data)
        {
            try
            {
                if (data.Length >= 6)
                {
                    // Parse external temperature
                    ushort tempValue = (ushort)((data[3] << 8) | data[2]);
                    _externalTemperature = (tempValue / 500.0f) - 50.0f;
                    
                    // Parse status flags
                    _statusFlags = data[4];
                    
                    Debug.Console(2, this, "HVAC Status: Ext Temp={0}°C, Flags={1:X2}", 
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
        
        public void Dispose()
        {
            _tcpClient.Dispose();
        }
    }
    
    public class HVACStatusUpdatedEventArgs : EventArgs
    {
        public float ExternalTemperature { get; set; }
        public bool OverTemp { get; set; }
        public bool PressureFault { get; set; }
        public bool VoltageFault { get; set; }
        public bool AirflowBlocked { get; set; }
    }
}
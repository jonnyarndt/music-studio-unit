
using core_tools;
using Crestron.SimplSharp;
using musicStudioUnit.Configuration;

namespace musicStudioUnit.Devices
{
    /// <summary>
    /// Enhanced HVAC Controller for Borden Air Multi-Zone HVAC System
    /// Implements complete binary protocol per Client-Scope.md Appendix B
    /// </summary>
    public class EnhancedHVACController : IKeyName, IDisposable
    {
        private readonly string _key;
        private readonly HVACInfo _config;
        private readonly HVACTcpClient _tcpClient;
        private readonly CTimer _responseTimeoutTimer;
        
        // Temperature data
        private float _currentSetpoint;
        private float _externalTemperature;
        private readonly Dictionary<byte, float> _zoneSetpoints = new Dictionary<byte, float>();
        
        // Status flags (bit-mapped per Client-Scope.md)
        private byte _statusFlags;
        private bool _overTemp;
        private bool _pressureFault;
        private bool _voltageFault;
        private bool _airflowBlocked;
        
        // State management
        private bool _isInitialized;
        private bool _waitingForResponse;
        private readonly object _lockObject = new object();
        
        // Persistence for nonvolatile setpoints
        private readonly string _setpointFilePath;

        public string Key => _key;
        public string Name => "HVAC Controller";

        // Properties for status monitoring
        public float CurrentSetpoint => _currentSetpoint;
        public float ExternalTemperature => _externalTemperature;
        public bool OverTemp => _overTemp;
        public bool PressureFault => _pressureFault;
        public bool VoltageFault => _voltageFault;
        public bool AirflowBlocked => _airflowBlocked;
        public bool IsInitialized => _isInitialized;
        public bool IsConnected => _tcpClient?.IsConnected == true;

        // Events
        public event EventHandler<HVACStatusUpdatedEventArgs> StatusUpdated;
        public event EventHandler<HVACSetpointChangedEventArgs> SetpointChanged;
        public event EventHandler<HVACErrorEventArgs> HVACError;
        public event EventHandler<HVACConnectedEventArgs> Connected;
        public event EventHandler<HVACDisconnectedEventArgs> Disconnected;

        public EnhancedHVACController(string key, HVACInfo config)
        {
            _key = key;
            _config = config;
            _setpointFilePath = GetSetpointFilePath(key);

            Debug.Console(1, this, "Initializing HVAC Controller for {0}:{1}", config.IP, config.Port);

            // Create specialized TCP client for binary HVAC protocol
            _tcpClient = new HVACTcpClient(key + "_Client", config.IP, config.Port);
            _tcpClient.DataReceived += OnDataReceived;
            _tcpClient.Connected += OnConnected;
            _tcpClient.Disconnected += OnDisconnected;

            // Response timeout timer (5 seconds)
            _responseTimeoutTimer = new CTimer(OnResponseTimeout, 5000);

            // Load persisted setpoints or use idle setpoint from config
            LoadPersistedSetpoints();

            DeviceManager.AddDevice(key, this);
            Debug.Console(1, this, "HVAC Controller created successfully");
        }

        /// <summary>
        /// Get the file path for HVAC setpoint persistence using Crestron-compatible path format
        /// </summary>
        private string GetSetpointFilePath(string key)
        {
            try
            {
                // Try to get the application root directory using Crestron API
                var rootDir = Crestron.SimplSharp.CrestronIO.Directory.GetApplicationRootDirectory();
                if (!string.IsNullOrEmpty(rootDir))
                {
                    Debug.Console($"[{Key}] Using GetApplicationRootDirectory: '{rootDir}'");
                    var settingsDir = Crestron.SimplSharp.CrestronIO.Path.Combine(rootDir, "hvac_setpoints");
                    var path = Crestron.SimplSharp.CrestronIO.Path.Combine(settingsDir, $"hvac_setpoints_{key}.dat");
                    return path;
                }
                // Fallback to Crestron internal format
                Debug.Console($"[{Key}] GetApplicationRootDirectory returned null/empty, using fallback");
                return $@"USER\hvac_setpoints_{key}.dat";
            }
            catch (Exception ex)
            {
                Debug.Console($"[{Key}] Error in GetSetpointFilePath: {ex.Message}");
                return $@"USER\hvac_setpoints_{key}.dat";
            }
        }

        /// <summary>
        /// Initialize the HVAC controller
        /// </summary>
        public bool Initialize()
        {
            try
            {
                Debug.Console(1, this, "Initializing HVAC Controller");

                // Connect to HVAC system
                if (_tcpClient.Connect())
                {
                    _isInitialized = true;
                    Debug.Console(1, this, "HVAC Controller initialized successfully");
                    return true;
                }
                else
                {
                    Debug.Console(0, this, "Failed to connect to HVAC system");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error initializing HVAC controller: {0}", ex.Message);
                HVACError?.Invoke(this, new HVACErrorEventArgs { ErrorMessage = ex.Message });
                return false;
            }
        }

        /// <summary>
        /// Set temperature for a specific zone (Client-Scope.md: +/- 0.5°C increments)
        /// </summary>
        public bool SetZoneTemperature(byte zoneId, float temperature)
        {
            lock (_lockObject)
            {
                try
                {
                    Debug.Console(1, this, "Setting zone {0} temperature to {1:F1}°C", zoneId, temperature);

                    // Validate temperature range (-40 to +50°C per Client-Scope.md)
                    if (temperature < -40.0f || temperature > 50.0f)
                    {
                        var error = string.Format("Temperature {0}°C is out of range (-40 to +50)", temperature);
                        Debug.Console(0, this, error);
                        HVACError?.Invoke(this, new HVACErrorEventArgs { ErrorMessage = error });
                        return false;
                    }

                    // Round to 0.5°C increments as required
                    temperature = (float)(Math.Round(temperature * 2.0) / 2.0);

                    // Check if already waiting for response
                    if (_waitingForResponse)
                    {
                        Debug.Console(0, this, "Cannot send command - waiting for previous response");
                        return false;
                    }

                    // Build and send command
                    byte[] command = BuildSetTemperatureCommand(new List<byte> { zoneId }, temperature);
                    bool sent = _tcpClient.SendBinaryData(command);

                    if (sent)
                    {
                        // Update local state
                        _zoneSetpoints[zoneId] = temperature;
                        _currentSetpoint = temperature;

                        // Start response timeout timer
                        _waitingForResponse = true;
                        _responseTimeoutTimer.Reset(5000);

                        // Persist setpoint (nonvolatile requirement)
                        SavePersistedSetpoints();

                        // Fire setpoint changed event
                        SetpointChanged?.Invoke(this, new HVACSetpointChangedEventArgs
                        {
                            ZoneId = zoneId,
                            Temperature = temperature
                        });

                        Debug.Console(2, this, "HVAC command sent successfully");
                        return true;
                    }
                    else
                    {
                        Debug.Console(0, this, "Failed to send HVAC command");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, this, "Error setting zone temperature: {0}", ex.Message);
                    HVACError?.Invoke(this, new HVACErrorEventArgs { ErrorMessage = ex.Message });
                    return false;
                }
            }
        }

        /// <summary>
        /// Set temperature for multiple zones (for combined studios)
        /// </summary>
        public bool SetMultipleZoneTemperatures(List<byte> zoneIds, float temperature)
        {
            lock (_lockObject)
            {
                try
                {
                    Debug.Console(1, this, "Setting temperature {0:F1}°C for {1} zones", temperature, zoneIds.Count);

                    // Validate inputs
                    if (zoneIds == null || zoneIds.Count == 0)
                    {
                        Debug.Console(0, this, "Zone ID list cannot be null or empty");
                        return false;
                    }

                    if (zoneIds.Count > 10)
                    {
                        Debug.Console(0, this, "Cannot set more than 10 zones at once");
                        return false;
                    }

                    // Validate temperature and round to 0.5°C increments
                    if (temperature < -40.0f || temperature > 50.0f)
                    {
                        var error = string.Format("Temperature {0}°C is out of range (-40 to +50)", temperature);
                        Debug.Console(0, this, error);
                        HVACError?.Invoke(this, new HVACErrorEventArgs { ErrorMessage = error });
                        return false;
                    }

                    temperature = (float)(Math.Round(temperature * 2.0) / 2.0);

                    if (_waitingForResponse)
                    {
                        Debug.Console(0, this, "Cannot send command - waiting for previous response");
                        return false;
                    }

                    // Build and send command for multiple zones
                    byte[] command = BuildSetTemperatureCommand(zoneIds, temperature);
                    bool sent = _tcpClient.SendBinaryData(command);

                    if (sent)
                    {
                        // Update local state for all zones
                        foreach (byte zoneId in zoneIds)
                        {
                            _zoneSetpoints[zoneId] = temperature;
                        }
                        _currentSetpoint = temperature;

                        // Start response timeout timer
                        _waitingForResponse = true;
                        _responseTimeoutTimer.Reset(5000);

                        // Persist setpoints
                        SavePersistedSetpoints();

                        // Fire setpoint changed events for each zone
                        foreach (byte zoneId in zoneIds)
                        {
                            SetpointChanged?.Invoke(this, new HVACSetpointChangedEventArgs
                            {
                                ZoneId = zoneId,
                                Temperature = temperature
                            });
                        }

                        Debug.Console(1, this, "Multi-zone HVAC command sent successfully");
                        return true;
                    }
                    else
                    {
                        Debug.Console(0, this, "Failed to send multi-zone HVAC command");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, this, "Error setting multiple zone temperatures: {0}", ex.Message);
                    HVACError?.Invoke(this, new HVACErrorEventArgs { ErrorMessage = ex.Message });
                    return false;
                }
            }
        }

        /// <summary>
        /// Build HVAC command packet per Client-Scope.md binary protocol specification
        /// </summary>
        private byte[] BuildSetTemperatureCommand(List<byte> zoneIds, float temperature)
        {
            var packet = new List<byte>();

            try
            {
                // Calculate temperature encoding: (temp + 50) * 500, split into LSB/MSB
                ushort tempValue = (ushort)((temperature + 50.0f) * 500.0f);
                byte tempLSB = (byte)(tempValue & 0xFF);
                byte tempMSB = (byte)((tempValue >> 8) & 0xFF);

                // Header per Client-Scope.md specification
                packet.Add(0x1B); // ESC
                packet.Add(0x00); // Length placeholder - will be updated
                packet.Add(0x4A); // Unit ID byte 1
                packet.Add(0x41); // Unit ID byte 2  
                packet.Add(0x31); // Unit ID byte 3
                packet.Add(0x00); // NUL

                // Zone data - repeat for each zone (max 10 per Client-Scope.md)
                foreach (byte zoneId in zoneIds)
                {
                    packet.Add(0x10); // DLE
                    packet.Add(zoneId); // Zone number
                    packet.Add(tempLSB); // Setpoint LSB
                    packet.Add(tempMSB); // Setpoint MSB
                    packet.Add(0x00); // NUL
                }

                // Footer
                packet.Add(0x17); // ETB

                // Update length field (total bytes including header and footer)
                packet[1] = (byte)packet.Count;

                Debug.Console(2, this, "Built HVAC command: {0} bytes for {1} zones at {2:F1}°C", 
                    packet.Count, zoneIds.Count, temperature);

                return packet.ToArray();
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error building HVAC command: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Process received HVAC response data per Client-Scope.md return protocol
        /// </summary>
        private void OnDataReceived(object? sender, HVACDataReceivedEventArgs args)
        {
            lock (_lockObject)
            {
                try
                {
                    byte[] data = args.Data;
                    Debug.Console(2, this, "HVAC response received: {0} bytes", data.Length);

                    // Cancel response timeout
                    _waitingForResponse = false;
                    _responseTimeoutTimer.Stop();

                    // Validate minimum packet size per Client-Scope.md
                    if (data.Length < 6)
                    {
                        Debug.Console(0, this, "Invalid HVAC response: too short ({0} bytes)", data.Length);
                        return;
                    }

                    // Validate header
                    if (data[0] != 0x1B) // ESC
                    {
                        Debug.Console(0, this, "Invalid HVAC response: missing ESC header");
                        return;
                    }

                    // Check length field
                    byte expectedLength = data[1];
                    if (data.Length != expectedLength)
                    {
                        Debug.Console(0, this, "Invalid HVAC response: length mismatch. Expected {0}, got {1}", 
                            expectedLength, data.Length);
                        return;
                    }

                    // Parse external temperature (bytes 2-3) per Client-Scope.md
                    ushort extTempValue = (ushort)((data[3] << 8) | data[2]);
                    float extTemp = (extTempValue / 500.0f) - 50.0f;

                    // Parse status flags (byte 4) per Client-Scope.md bit definitions
                    byte statusFlags = data[4];
                    bool overTemp = (statusFlags & 0x01) != 0;      // Bit 1: OVERTEMP
                    bool pressure = (statusFlags & 0x02) != 0;      // Bit 2: PRESSURE
                    bool voltage = (statusFlags & 0x04) != 0;       // Bit 3: VOLTAGE
                    bool airflow = (statusFlags & 0x08) != 0;       // Bit 4: AIRFLOW

                    // Update internal state
                    _externalTemperature = extTemp;
                    _statusFlags = statusFlags;
                    _overTemp = overTemp;
                    _pressureFault = pressure;
                    _voltageFault = voltage;
                    _airflowBlocked = airflow;

                    Debug.Console(1, this, "HVAC Status - Ext Temp: {0:F2}°C, Flags: {1:X2} (OT:{2} P:{3} V:{4} A:{5})",
                        _externalTemperature, _statusFlags, overTemp, pressure, voltage, airflow);

                    // Fire status update event
                    StatusUpdated?.Invoke(this, new HVACStatusUpdatedEventArgs
                    {
                        ExternalTemperature = _externalTemperature,
                        OverTemp = _overTemp,
                        PressureFault = _pressureFault,
                        VoltageFault = _voltageFault,
                        AirflowBlocked = _airflowBlocked,
                        StatusFlags = _statusFlags
                    });
                }
                catch (Exception ex)
                {
                    Debug.Console(0, this, "Error processing HVAC response: {0}", ex.Message);
                    HVACError?.Invoke(this, new HVACErrorEventArgs { ErrorMessage = ex.Message });
                }
            }
        }

        /// <summary>
        /// Handle response timeout
        /// </summary>
        private void OnResponseTimeout(object? obj)
        {
            lock (_lockObject)
            {
                if (_waitingForResponse)
                {
                    _waitingForResponse = false;
                    Debug.Console(0, this, "HVAC response timeout - no response received");
                    HVACError?.Invoke(this, new HVACErrorEventArgs 
                    { 
                        ErrorMessage = "HVAC response timeout" 
                    });
                }
            }
        }

        /// <summary>
        /// Load persisted setpoints for nonvolatile storage requirement
        /// </summary>
        private void LoadPersistedSetpoints()
        {
            try
            {
                // Log GetApplicationRootDirectory value
                var appRoot = Crestron.SimplSharp.CrestronIO.Directory.GetApplicationRootDirectory();
                core_tools.Debug.Console($"[{Key}] GetApplicationRootDirectory: '{appRoot}'");

                // Try using appRoot as prefix if available
                string appRootPath = null;
                if (!string.IsNullOrEmpty(appRoot))
                {
                    appRootPath = Crestron.SimplSharp.CrestronIO.Path.Combine(appRoot, $"hvac_setpoints_{Key}.dat");
                    try
                    {
                        if (Crestron.SimplSharp.CrestronIO.File.Exists(appRootPath))
                        {
                            core_tools.Debug.Console($"[{Key}] Found setpoint file using appRoot: {appRootPath}");
                            var fileStream = Crestron.SimplSharp.CrestronIO.File.OpenText(appRootPath);
                            var lines = new List<string>();
                            string? line;
                            while ((line = fileStream.ReadLine()) != null)
                            {
                                lines.Add(line);
                            }
                            fileStream.Close();
                            foreach (string lineContent in lines)
                            {
                                string[] parts = lineContent.Split(',');
                                if (parts.Length == 2)
                                {
                                    byte zoneId = byte.Parse(parts[0]);
                                    float temp = float.Parse(parts[1]);
                                    _zoneSetpoints[zoneId] = temp;
                                    _currentSetpoint = temp;
                                }
                            }
                            Debug.Console(1, this, $"Loaded {lines.Count} persisted setpoints from {appRootPath}");
                            return;
                        }
                        else
                        {
                            core_tools.Debug.Console($"[{Key}] appRoot path not found: {appRootPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        core_tools.Debug.Console($"[{Key}] Exception checking appRoot path {appRootPath}: {ex.Message}");
                    }
                }

                // Enumerate available directories for diagnostics
                try
                {
                    var dirs = Crestron.SimplSharp.CrestronIO.Directory.GetDirectories("");
                    core_tools.Debug.Console($"[{Key}] Available directories:");
                    foreach (var dir in dirs)
                    {
                        core_tools.Debug.Console($"[{Key}] Dir: {dir}");
                    }
                }
                catch (Exception ex)
                {
                    core_tools.Debug.Console($"[{Key}] Exception enumerating directories: {ex.Message}");
                }

                // Try well-known Crestron system folders
                string[] crestronFolders = new[] {
                    $@"\\NVRAM\\hvac_setpoints_{Key}.dat",
                    $@"\\USER\\hvac_setpoints_{Key}.dat"
                };
                string pathToLoad = string.Empty;
                foreach (var path in crestronFolders)
                {
                    try
                    {
                        if (Crestron.SimplSharp.CrestronIO.File.Exists(path))
                        {
                            pathToLoad = path;
                            core_tools.Debug.Console($"[{Key}] Found setpoint file in system folder: {path}");
                            break;
                        }
                        else
                        {
                            core_tools.Debug.Console($"[{Key}] System folder path not found: {path}");
                        }
                    }
                    catch (Exception ex)
                    {
                        core_tools.Debug.Console($"[{Key}] Exception checking system folder path {path}: {ex.Message}");
                    }
                }
                if (pathToLoad != null)
                {
                    try
                    {
                        var fileStream = Crestron.SimplSharp.CrestronIO.File.OpenText(pathToLoad);
                        var lines = new List<string>();
                        string? line;
                        while ((line = fileStream.ReadLine()) != null)
                        {
                            lines.Add(line);
                        }
                        fileStream.Close();
                        foreach (string lineContent in lines)
                        {
                            string[] parts = lineContent.Split(',');
                            if (parts.Length == 2)
                            {
                                byte zoneId = byte.Parse(parts[0]);
                                float temp = float.Parse(parts[1]);
                                _zoneSetpoints[zoneId] = temp;
                                _currentSetpoint = temp; // Use last loaded as current
                            }
                        }
                        Debug.Console(1, this, $"Loaded {lines.Count} persisted setpoints from {pathToLoad}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Console(0, this, $"Error reading setpoints from {pathToLoad}: {ex.Message}");
                        _currentSetpoint = _config.IdleSetpoint;
                    }
                }
                else
                {
                    // Use idle setpoint from configuration
                    _currentSetpoint = _config.IdleSetpoint;
                    Debug.Console(1, this, $"No persisted setpoints found in system folders, using idle setpoint: {_currentSetpoint:F1}°C");
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, $"Error loading persisted setpoints: {ex.Message}");
                _currentSetpoint = _config.IdleSetpoint;
            }
        }

        /// <summary>
        /// Save setpoints for nonvolatile storage requirement
        /// </summary>
        private void SavePersistedSetpoints()
        {
            try
            {
                string[] formats = new[] {
                    $@"USER\hvac_setpoints_{Key}.dat",
                    $@"NVRAM\hvac_setpoints_{Key}.dat",
                    $@"\\USER\\hvac_setpoints_{Key}.dat",
                    $@"\\NVRAM\\hvac_setpoints_{Key}.dat",
                    $@"user\\hvac_setpoints_{Key}.dat",
                    $@"nvram\\hvac_setpoints_{Key}.dat",
                    $@"C:\\USER\\hvac_setpoints_{Key}.dat",
                    $@"C:\\NVRAM\\hvac_setpoints_{Key}.dat"
                };
                foreach (var path in formats)
                {
                    try
                    {
                        var fileStream = Crestron.SimplSharp.CrestronIO.File.CreateText(path);
                        foreach (var kvp in _zoneSetpoints)
                        {
                            fileStream.WriteLine($"{kvp.Key},{kvp.Value}");
                        }
                        fileStream.Close();
                        Debug.Console(1, this, $"Persisted {_zoneSetpoints.Count} setpoints to {path}");
                        break; // Only write to the first format that works
                    }
                    catch (Exception ex2)
                    {
                        Debug.Console(0, this, $"Error saving setpoints to {path}: {ex2.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, $"Error saving persisted setpoints: {ex.Message}");
            }
        }

        private void OnConnected(object? sender, EventArgs args)
        {
            Debug.Console(1, this, "HVAC TCP connection established");
            Connected?.Invoke(this, new HVACConnectedEventArgs());
        }

        private void OnDisconnected(object? sender, EventArgs args)
        {
            Debug.Console(1, this, "HVAC TCP connection lost");
            _waitingForResponse = false;
            Disconnected?.Invoke(this, new HVACDisconnectedEventArgs());
        }

        /// <summary>
        /// Get current status for UI display
        /// </summary>
        public HVACStatus GetCurrentStatus()
        {
            return new HVACStatus
            {
                CurrentSetpoint = _currentSetpoint,
                ExternalTemperature = _externalTemperature,
                OverTemp = _overTemp,
                PressureFault = _pressureFault,
                VoltageFault = _voltageFault,
                AirflowBlocked = _airflowBlocked,
                IsConnected = IsConnected,
                ZoneSetpoints = new Dictionary<byte, float>(_zoneSetpoints)
            };
        }

        public void Dispose()
        {
            _responseTimeoutTimer?.Dispose();
            _tcpClient?.Dispose();
            
            if (DeviceManager.ContainsKey(Key))
                DeviceManager.RemoveDevice(Key);
        }
    }
}

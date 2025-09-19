using core_tools;
using Crestron.SimplSharp;                          	// For Basic SIMPL# Classes
using Crestron.SimplSharpPro;                       	// For Basic SIMPL#Pro classes
using Crestron.SimplSharpPro.DeviceSupport;         	// For Generic Device Support
using Crestron.SimplSharpPro.Diagnostics;		    	// For System Monitor Access
using Crestron.SimplSharpPro.UI;
using musicStudioUnit.Configuration;
using musicStudioUnit.Devices;
using musicStudioUnit.Services;
using musicStudioUnit.UserInterface;
using System.Reflection;

namespace musicStudioUnit
{
    public class ControlSystem : CrestronControlSystem
    {      
        private readonly uint _touchPanelOneIPID = 0x2a;
        private SystemInitializationService _initializationService;
        private TP01 _touchPanel;
        private MSUTouchPanel _msuTouchPanel;
        private MSUController _msuController;
        private EnhancedHVACController _hvacController;
        private EnhancedMusicSystemController _musicController;

        /// <summary>
        /// ControlSystem Constructor. Starting point for the SIMPL#Pro program.
        /// Use the constructor to:
        /// * Initialize the maximum number of threads (max = 400)
        /// * Register devices
        /// * Register event handlers
        /// * Add Console Commands
        /// 
        /// Please be aware that the constructor needs to exit quickly; if it doesn't
        /// exit in time, the SIMPL#Pro program will exit.
        /// 
        /// You cannot send / receive data in the constructor
        /// </summary>
        public ControlSystem() : base()
        {
            try
            {
                Crestron.SimplSharpPro.CrestronThread.Thread.MaxNumberOfUserThreads = 400;
                Global.ControlSystem = this;
                Global.DIO = new DigitalIO();

                //Subscribe to the controller events (System, Program, and Ethernet)
                CrestronEnvironment.SystemEventHandler += new SystemEventHandler(ControllerSystemEventHandler);
                CrestronEnvironment.ProgramStatusEventHandler += new ProgramStatusEventHandler(ControllerProgramEventHandler);
                CrestronEnvironment.EthernetEventHandler += new EthernetEventHandler(ControllerEthernetEventHandler);
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in the constructor: {0}", e.Message);
            }
        }

        /// <summary>
        /// InitializeSystem - this method gets called after the constructor 
        /// has finished. 
        /// 
        /// Use InitializeSystem to:
        /// * Start threads
        /// * Configure ports, such as serial and verisports
        /// * Start and initialize socket connections
        /// * Send initial device configurations
        /// 
        /// Please be aware that InitializeSystem needs to exit quickly also; 
        /// if it doesn't exit in time, the SIMPL#Pro program will exit.
        /// </summary>
        public override void InitializeSystem()
        {
            DeterminePlatform();

            try
            {
                Debug.Console(0, "******************* InitializeSystem() **********************");
                // Console starts to show the "......" at this time
                // Read config
                // Build your touch screen
                // UI print-outs: Step 1-Config Read
                // Build lighting clients
                // Build comm clients
                // After this class, unless events happen, nothing else automation wise will happen

                var panel = GetPanelForType(_touchPanelOneIPID);
                
                if (panel == null)
                {
                    Debug.Console(0, "ERROR: Failed to create touch panel - system initialization will continue but touch panel will not be available");
                    ErrorLog.Error("Touch panel creation failed - IP ID may be in use or invalid");
                }
                else
                {
                    Debug.Console(1, "Touch panel created successfully");
                }
                
                var dIO = Global.DIO;

                CrestronConsole.AddNewConsoleCommand(PrintDevMon, "printDevMon", "Print all Device Monitor devices", ConsoleAccessLevelEnum.AccessOperator);

                // Initialize touch panel first (only if panel creation succeeded)
                if (panel != null)
                {
                    Debug.Console(0, "INIT: Initializing Touch Panel Interface");
                    _touchPanel = new TP01("tp01", "TP01", panel);
                }
                else
                {
                    Debug.Console(0, "INIT: Skipping Touch Panel Interface initialization due to panel creation failure");
                }
                var sysInfo = new SystemInformationMethods();
                Debug.Console(2, "INIT: InitializeSystem().sysInfo - Check");
                var sysProcessorInfo = new ProcessorInfo();
                Debug.Console(2, "INIT: InitializeSystem().sysProcessorInfo - Check");
                var sysEthernetInfo = new EthernetInfo();
                Debug.Console(2, "INIT: InitializeSystem().sysEthernetInfo - Check");
                var sysConstants = new SystemInfoConstants();
                Debug.Console(2, "INIT: InitializeSystem().sysConstants - Check");

                sysInfo.GetProcessorInfo();
                Debug.Console(2, "INIT: InitializeSystem().sysInfo.GetProcessorInfo() - Check");
                sysInfo.GetEthernetInfo();
                Debug.Console(2, "INIT: InitializeSystem().sysInfo.GetEthernetInfo() - Check");

                Debug.Console(0, "*********************************************************\n");
                Debug.Console(0, "INIT: Processor Firmware:       {0}", sysInfo.Processor.Firmware);
                Debug.Console(0, "INIT: Processor MAC Address:    {0}", sysInfo.Adapter.MacAddress);
                Debug.Console(0, "INIT: Processor IP Address:     {0}", sysInfo.Adapter.IpAddress);
                Debug.Console(0, "INIT: Processor Subnet Mask:    {0}", sysInfo.Adapter.Subnet);
                Debug.Console(0, "INIT: Processor Gateway:        {0}", sysInfo.Adapter.Gateway);
                Debug.Console(0, "INIT: Processor Hostname:       {0}", sysInfo.Adapter.Hostname);
                Debug.Console(0, "*********************************************************\n");                
                
                // Initialize User Database
                Debug.Console(0, "INIT: Initializing User Database");
                InitializeUserDatabase();
                
                // Initialize HVAC controller
                Debug.Console(0, "INIT: Initializing HVAC Temperature Controller");
                if (panel != null) InitializeHVACController(panel);
       
                // Initialize Music System controller
                Debug.Console(0, "INIT: Initializing Music System Controller");
                if (panel != null) InitializeMusicController(panel);
                
                // Initialize comprehensive MSU system using new initialization service
                Debug.Console(0, "INIT: Starting Masters of Karaoke MSU System Initialization");
                
                _initializationService = new SystemInitializationService("SystemInit");
                _initializationService.InitializationComplete += OnSystemInitializationComplete;
                _initializationService.InitializationError += OnSystemInitializationError;
                _initializationService.PhaseChanged += OnInitializationPhaseChanged;
                
                // Execute complete initialization sequence
                if (_initializationService.Initialize())
                {
                    Debug.Console(1, "INIT: MSU System initialization completed successfully");
                    
                    // Get MSU controller
                    _msuController = _initializationService.MSUController;
                    
                    // Initialize MSU TouchPanel with all components
                    Debug.Console(0, "INIT: Initializing MSU TouchPanel with integrated screens");
                    if(panel != null) InitializeMSUTouchPanel(panel);
                    
                    // Connect MSU controller to original touch panel if available
                    if (_msuController != null && _touchPanel != null)
                    {
                        _touchPanel.SetMSUController(_msuController);
                        Debug.Console(1, "INIT: Original touch panel connected to MSU controller");
                    }
                }
                else
                {
                    Debug.Console(0, "INIT: MSU System initialization failed - see errors above");
                }
                
                // Add console command for configuration reload
                CrestronConsole.AddNewConsoleCommand(ReloadConfiguration, "reloadConfig", 
                    "Reload MSU configuration files", ConsoleAccessLevelEnum.AccessOperator);
                
                // Add console commands for MSU TouchPanel
                CrestronConsole.AddNewConsoleCommand(SetMSUPage, "msupage", 
                    "Set MSU TouchPanel page (Settings, User, Music, Temperature, Combine)", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(ShowMSUStatus, "msustatus", 
                    "Show MSU TouchPanel status", ConsoleAccessLevelEnum.AccessOperator);
                
                Debug.Console(0, "******************* InitializeSystem() Complete **********************");

                SystemMonitor.ProgramInitialization.ProgramInitializationComplete = true;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in InitializeSystem: {0}", e.Message);
            }
        }

        /// <summary>
        /// DeterminePlatform method
        /// </summary>
        public void DeterminePlatform()
        {
            try
            {
                Debug.Console(0, "Determining Platform...");

                string filePathPrefix;

                var dirSeparator = Global.DirectorySeparator;

                string directoryPrefix;

                directoryPrefix = Crestron.SimplSharp.CrestronIO.Directory.GetApplicationRootDirectory();

                var executingAssembly = Assembly.GetExecutingAssembly();
                var assemblyName = executingAssembly?.GetName();
                var version = assemblyName?.Version?.ToString() ?? "unknown";
                Global.SetAssemblyVersion(version);

                if (CrestronEnvironment.DevicePlatform != eDevicePlatform.Server)   // Handles 3-series running Windows CE OS
                {
                    string userFolder;
                    string nvramFolder;
                    bool is4series = false;

                    if (eCrestronSeries.Series4 == (Global.ProcessorSeries & eCrestronSeries.Series4)) // Handle 4-series
                    {
                        is4series = true;
                        // Set path to user/
                        userFolder = "user";
                        nvramFolder = "nvram";
                    }
                    else
                    {
                        userFolder = "User";
                        nvramFolder = "Nvram";
                    }

                    var ts = string.Format("Starting App v{0} on {1} Appliance", Global.AssemblyVersion, is4series ? "4-series" : "3-series");
                    Debug.Console(0, ts);

                    // Check if User/ProgramX exists
                    if (Directory.Exists(Global.ApplicationDirectoryPathPrefix + dirSeparator + userFolder
                        + dirSeparator + string.Format("program{0}", InitialParametersClass.ApplicationNumber)))
                    {
                        var tempString = string.Format("{0}/program{1} directory found", userFolder, InitialParametersClass.ApplicationNumber);
                        Debug.Console(0, tempString);
                        filePathPrefix = directoryPrefix + dirSeparator + userFolder
                        + dirSeparator + string.Format("program{0}", InitialParametersClass.ApplicationNumber) + dirSeparator;
                    }
                    // Check if Nvram/Programx exists
                    else if (Directory.Exists(directoryPrefix + dirSeparator + nvramFolder
                        + dirSeparator + string.Format("program{0}", InitialParametersClass.ApplicationNumber)))
                    {
                        var tempString = string.Format("{0}/program{1} directory found", nvramFolder, InitialParametersClass.ApplicationNumber);
                        Debug.Console(0, tempString);

                        filePathPrefix = directoryPrefix + dirSeparator + nvramFolder
                        + dirSeparator + string.Format("program{0}", InitialParametersClass.ApplicationNumber) + dirSeparator;
                    }
                    // If neither exists, set path to User/ProgramX
                    else
                    {
                        var tempString = string.Format("{0}/program{1} directory found", userFolder, InitialParametersClass.ApplicationNumber);
                        Debug.Console(0, tempString);

                        filePathPrefix = directoryPrefix + dirSeparator + userFolder
                        + dirSeparator + string.Format("program{0}", InitialParametersClass.ApplicationNumber) + dirSeparator;
                    }
                }
                else   // Handles Linux OS (Virtual Control)
                {
                    //Debug.SetDebugLevel(2);
                    Debug.Console(0, "Starting Essentials v{version:l} on Virtual Control Server", Global.AssemblyVersion);

                    // Set path to User/
                    filePathPrefix = directoryPrefix + dirSeparator + "User" + dirSeparator;
                }

                Global.SetFilePathPrefix(filePathPrefix);
            }
            catch (Exception e)
            {
                Debug.Console(0, "Unable to determine platform due to exception: {0}", e.StackTrace);
            }
        }

        /// <summary>
        /// Returns a BasicTriListWithSmartObject panel
        /// </summary>
        /// <param name="ipId"></param>
        /// <returns></returns>
        private static BasicTriListWithSmartObject? GetPanelForType (uint ipId)
        {
            try
            {
                Debug.Console(1, "Creating touch panel with IP ID: 0x{0:X2} ({1})", ipId, ipId);
                var tsw1070 = new Tsw1070(ipId, Global.ControlSystem);
                
                if (tsw1070.Register() != eDeviceRegistrationUnRegistrationResponse.Success)
                {
                    Debug.Console(0, "ERROR: Failed to register touch panel at IP ID 0x{0:X2}", ipId);
                    ErrorLog.Error("Touch panel registration failed for IP ID 0x{0:X2}", ipId);
                }
                
                Debug.Console(1, "Touch panel registered successfully at IP ID: 0x{0:X2}", ipId);
                return tsw1070;
            }
            catch (Exception ex)
            {
                Debug.Console(0, "ERROR: Exception creating touch panel: {0}", ex.Message);
                ErrorLog.Error("Touch panel creation error: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// This method will be called when console command "printDevMon" is issued
        /// </summary>
        /// <param name="message"></param>
        private void PrintDevMon(string message) { DeviceManager.PrintDevices(); }

        /// <summary>
        /// Event handler for configuration loaded events
        /// </summary>
        private void OnConfigurationLoaded(object sender, ConfigurationLoadedEventArgs args)
        {
            Debug.Console(1, "Configuration loaded - Local and Remote configurations available");
            
            if (args.LocalConfig != null)
            {
                Debug.Console(1, "Local Config - Processor MAC: {0}", args.LocalConfig.ProcessorMAC);
                if (args.LocalConfig.Remote != null)
                {
                    Debug.Console(1, "Local Config - Remote Server: {0}:{1}",
                        args.LocalConfig.Remote.IP, args.LocalConfig.Remote.Port);
                }
                else
                {
                    Debug.Console(1, "Local Config - Remote Server: <not configured>");
                }
            }

            if (args.RemoteConfig != null)
            {
                Debug.Console(1, "Remote Config - Found {0} MSU units configured",
                    args.RemoteConfig.MSUUnits?.Count ?? 0);
            }
        }

        /// <summary>
        /// Event handler for system initialization completion
        /// </summary>
        private void OnSystemInitializationComplete(object? sender, InitializationCompleteEventArgs args)
        {
            Debug.Console(1, "System initialization completed in {0:F1} seconds", args.InitializationTime.TotalSeconds);
            
            if (args.IdentifiedMSU != null)
            {
                Debug.Console(1, "This MSU is identified as: {0} at coordinates ({1},{2})", 
                    args.IdentifiedMSU.MSU_NAME, args.IdentifiedMSU.X_COORD, args.IdentifiedMSU.Y_COORD);
            }
            else
            {
                Debug.Console(1, "This MSU is running in standalone mode");
            }
        }

        /// <summary>
        /// Event handler for system initialization errors
        /// </summary>
        private void OnSystemInitializationError(object? sender, InitializationErrorEventArgs args)
        {
            Debug.Console(0, "System initialization error: {0}", args.ErrorMessage);
            ErrorLog.Error("MSU System Initialization Error: {0}", args.ErrorMessage);
        }

        /// <summary>
        /// Event handler for initialization phase changes
        /// </summary>
        private void OnInitializationPhaseChanged(object? sender, InitializationPhaseEventArgs args)
        {
            Debug.Console(2, "Initialization phase changed to: {0}", args.Phase);
        }

        /// <summary>
        /// Console command to reload configuration
        /// </summary>
        private void ReloadConfiguration(string message)
        {
            if (_initializationService != null)
            {
                Debug.Console(1, "Reloading configuration as requested from console");
                if (_initializationService.ReloadConfiguration())
                {
                    Debug.Console(1, "Configuration reload completed");
                }
                else
                {
                    Debug.Console(0, "Configuration reload failed");
                }
            }
            else
            {
                Debug.Console(0, "Cannot reload configuration - initialization service not available");
            }
        }

        /// <summary>
        /// Initialize HVAC temperature controller per Client-Scope.md specifications
        /// </summary>
        private void InitializeHVACController(BasicTriList panel)
        {
            try
            {
                Debug.Console(1, "Initializing HVAC temperature controller...");

                // Create HVAC configuration
                var hvacConfig = new HVACInfo
                {
                    IP = "10.0.0.100", // TODO: Read from configuration file
                    Port = 4001, // Default port per Client-Scope.md
                    IdleSetpoint = 21.0f,
                    ZoneIds = new List<byte> { 1, 2, 3 }, // TODO: Configure per studio zones
                    DebugMode = true
                };

                // Create HVAC controller
                _hvacController = new EnhancedHVACController("MainHVAC", hvacConfig);

                // Create temperature presets
                var presets = new List<TemperaturePreset>
                {
                    new TemperaturePreset { Name = "Cool Recording", Temperature = 18.0f, Description = "Cool for intense recording sessions" },
                    new TemperaturePreset { Name = "Comfortable", Temperature = 21.0f, Description = "Standard comfortable temperature" },
                    new TemperaturePreset { Name = "Warm Vocals", Temperature = 24.0f, Description = "Warmer for vocal sessions" },
                    new TemperaturePreset { Name = "Idle", Temperature = 19.0f, Description = "Energy saving when not in use" }
                };

                // Initialize HVAC controller
                if (_hvacController.Initialize())
                {
                    Debug.Console(1, "HVAC controller initialized successfully");
                    
                    // Add console commands for HVAC control
                    CrestronConsole.AddNewConsoleCommand(PrintHVACStatus, "hvacstatus", 
                        "Print current HVAC status", ConsoleAccessLevelEnum.AccessOperator);
                    CrestronConsole.AddNewConsoleCommand(SetHVACTemp, "hvactemp", 
                        "Set HVAC temperature (e.g., hvactemp 21.5)", ConsoleAccessLevelEnum.AccessOperator);
                }
                else
                {
                    Debug.Console(0, "HVAC controller initialization failed");
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "Error initializing HVAC controller: {0}", ex.Message);
                ErrorLog.Error("HVAC Initialization Error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Console command to print HVAC status
        /// </summary>
        private void PrintHVACStatus(string message)
        {
            if (_hvacController != null)
            {
                var status = _hvacController.GetCurrentStatus();
                CrestronConsole.PrintLine("HVAC Status:");
                CrestronConsole.PrintLine("  Connected: {0}", status.IsConnected);
                CrestronConsole.PrintLine("  Current Setpoint: {0:F1}°C", status.CurrentSetpoint);
                CrestronConsole.PrintLine("  External Temperature: {0:F1}°C", status.ExternalTemperature);
                CrestronConsole.PrintLine("  Status Flags:");
                CrestronConsole.PrintLine("    Over Temperature: {0}", status.OverTemp ? "YES" : "NO");
                CrestronConsole.PrintLine("    Pressure Fault: {0}", status.PressureFault ? "YES" : "NO");
                CrestronConsole.PrintLine("    Voltage Fault: {0}", status.VoltageFault ? "YES" : "NO");
                CrestronConsole.PrintLine("    Airflow Blocked: {0}", status.AirflowBlocked ? "YES" : "NO");
                
                if (status.ZoneSetpoints.Count > 0)
                {
                    CrestronConsole.PrintLine("  Zone Setpoints:");
                    foreach (var zone in status.ZoneSetpoints)
                    {
                        CrestronConsole.PrintLine("    Zone {0}: {1:F1}°C", zone.Key, zone.Value);
                    }
                }
            }
            else
            {
                CrestronConsole.PrintLine("HVAC controller not initialized");
            }
        }

        /// <summary>
        /// Console command to set HVAC temperature
        /// </summary>
        private void SetHVACTemp(string message)
        {
            if (_hvacController == null)
            {
                CrestronConsole.PrintLine("HVAC controller not initialized");
                return;
            }

            try
            {
                string[] parts = message.Split(' ');
                if (parts.Length < 2)
                {
                    CrestronConsole.PrintLine("Usage: hvactemp <temperature> [zone_id]");
                    CrestronConsole.PrintLine("Example: hvactemp 21.5");
                    CrestronConsole.PrintLine("Example: hvactemp 23.0 1");
                    return;
                }

                float temperature = float.Parse(parts[1]);
                
                if (parts.Length >= 3)
                {
                    // Set specific zone
                    byte zoneId = byte.Parse(parts[2]);
                    if (_hvacController.SetZoneTemperature(zoneId, temperature))
                    {
                        CrestronConsole.PrintLine("Temperature set to {0:F1}°C for zone {1}", temperature, zoneId);
                    }
                    else
                    {
                        CrestronConsole.PrintLine("Failed to set temperature for zone {0}", zoneId);
                    }
                }
                else
                {
                    // Set all zones (default zone 1)
                    if (_hvacController.SetZoneTemperature(1, temperature))
                    {
                        CrestronConsole.PrintLine("Temperature set to {0:F1}°C", temperature);
                    }
                    else
                    {
                        CrestronConsole.PrintLine("Failed to set temperature");
                    }
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("Error setting temperature: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Initialize Music System controller per Client-Scope.md Appendix C
        /// </summary>
        private void InitializeMusicController(BasicTriList panel)
        {
            try
            {
                Debug.Console(1, "Initializing Music System controller...");

                // Get MSU UID from MAC address (as specified in Client-Scope.md)
                var sysInfo = new SystemInformationMethods();
                sysInfo.GetEthernetInfo();
                string macAddress = sysInfo.Adapter.MacAddress.Replace(":", "").Replace("-", "").ToUpper();
                string msuUID = macAddress; // Use MAC as MSU UID per spec

                // Create DMS configuration
                var dmsConfig = new musicStudioUnit.Devices.DMSInfo
                {
                    IP = "10.0.0.200", // TODO: Read from configuration file
                    Port = 4010, // TODO: Read from configuration file
                };

                // Create music controller
                _musicController = new EnhancedMusicSystemController("MainMusic", dmsConfig, msuUID);

                // Initialize music controller
                if (_musicController.Initialize())
                {
                    Debug.Console(1, "Music System controller initialized successfully");
                    Debug.Console(1, "MSU UID: {0}", msuUID);
                    
                    // Add console commands for music control
                    CrestronConsole.AddNewConsoleCommand(PrintMusicStatus, "musicstatus", 
                        "Print current music system status", ConsoleAccessLevelEnum.AccessOperator);
                    CrestronConsole.AddNewConsoleCommand(RefreshMusicCatalog, "musicrefresh", 
                        "Refresh music catalog from DMS", ConsoleAccessLevelEnum.AccessOperator);
                    CrestronConsole.AddNewConsoleCommand(PlayTrackCommand, "playtrack", 
                        "Play track by ID (e.g., playtrack 1001)", ConsoleAccessLevelEnum.AccessOperator);
                    CrestronConsole.AddNewConsoleCommand(StopTrackCommand, "stoptrack", 
                        "Stop current track playback", ConsoleAccessLevelEnum.AccessOperator);
                }
                else
                {
                    Debug.Console(0, "Music System controller initialization failed");
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "Error initializing Music System controller: {0}", ex.Message);
                ErrorLog.Error("Music System Initialization Error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Console command to print music system status
        /// </summary>
        private void PrintMusicStatus(string message)
        {
            if (_musicController != null)
            {
                var status = _musicController.GetPlaybackStatus();
                CrestronConsole.PrintLine("Music System Status:");
                CrestronConsole.PrintLine("  Connected: {0}", status.IsConnected);
                CrestronConsole.PrintLine("  Total Artists: {0}", _musicController.TotalArtistCount);
                CrestronConsole.PrintLine("  Loaded Artists: {0}", _musicController.ArtistCount);
                CrestronConsole.PrintLine("  Currently Playing: {0}", status.IsPlaying ? "YES" : "NO");
                
                if (status.IsPlaying)
                {
                    CrestronConsole.PrintLine("  Current Track: {0} by {1}", status.CurrentTrackName, status.CurrentArtistName);
                    CrestronConsole.PrintLine("  Remaining Time: {0}", status.FormattedRemainingTime);
                }
            }
            else
            {
                CrestronConsole.PrintLine("Music System controller not initialized");
            }
        }

        /// <summary>
        /// Console command to refresh music catalog
        /// </summary>
        private void RefreshMusicCatalog(string message)
        {
            if (_musicController != null)
            {
                CrestronConsole.PrintLine("Refreshing music catalog from DMS...");
                _musicController.LoadMusicCatalog();
            }
            else
            {
                CrestronConsole.PrintLine("Music System controller not initialized");
            }
        }

        /// <summary>
        /// Console command to play track
        /// </summary>
        private void PlayTrackCommand(string message)
        {
            if (_musicController == null)
            {
                CrestronConsole.PrintLine("Music System controller not initialized");
                return;
            }

            try
            {
                string[] parts = message.Split(' ');
                if (parts.Length < 2)
                {
                    CrestronConsole.PrintLine("Usage: playtrack <track_id>");
                    CrestronConsole.PrintLine("Example: playtrack 1001");
                    return;
                }

                int trackId = int.Parse(parts[1]);
                
                // For console command, we'll use placeholder names
                // In real usage, the UI would provide the actual track and artist names
                if (_musicController.PlayTrack(trackId, "Track " + trackId, "Unknown Artist"))
                {
                    CrestronConsole.PrintLine("Started playback of track {0}", trackId);
                }
                else
                {
                    CrestronConsole.PrintLine("Failed to start playback of track {0}", trackId);
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("Error playing track: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Console command to stop track
        /// </summary>
        private void StopTrackCommand(string message)
        {
            if (_musicController == null)
            {
                CrestronConsole.PrintLine("Music System controller not initialized");
                return;
            }

            try
            {
                if (_musicController.StopTrack())
                {
                    CrestronConsole.PrintLine("Track playback stopped");
                }
                else
                {
                    CrestronConsole.PrintLine("Failed to stop track playback");
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("Error stopping track: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Initialize User Database per Client-Scope.md Appendix A
        /// </summary>
        private void InitializeUserDatabase()
        {
            try
            {
                Debug.Console(1, "User Database initialization skipped - loyalty system removed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "Error initializing User Database: {0}", ex.Message);
                ErrorLog.Error("User Database Initialization Error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Initialize MSU TouchPanel with all integrated screen handlers
        /// </summary>
        private void InitializeMSUTouchPanel(BasicTriListWithSmartObject panel)
        {
            try
            {
                Debug.Console(1, "Initializing MSU TouchPanel with integrated screens...");
                
                if (_msuController == null)
                {
                    Debug.Console(0, "Cannot initialize MSU TouchPanel - MSU Controller not available");
                    return;
                }

                if (_hvacController == null)
                {
                    Debug.Console(0, "Cannot initialize MSU TouchPanel - HVAC Controller not available");
                    return;
                }

                if (_musicController == null)
                {
                    Debug.Console(0, "Cannot initialize MSU TouchPanel - Music Controller not available");
                    return;
                }

                // Create Studio Combination Manager
                var combinationManager = new musicStudioUnit.Services.StudioCombinationManager(
                    "studioCombination", _msuController?.Key ?? "default_msu", 0, 0, 1, new Dictionary<string, musicStudioUnit.Services.MusicStudioUnit>());

                // Create MSU TouchPanel with all components
                _msuTouchPanel = new MSUTouchPanel("msuTouchPanel", "MSU TouchPanel", panel,
                    _msuController, _initializationService, _hvacController, _musicController, combinationManager);

                Debug.Console(1, "MSU TouchPanel initialized successfully with all screen handlers");
                Debug.Console(1, "Available screens: Settings, User, Music, Temperature, Combine");
                Debug.Console(1, "Default screen: Settings (per Client-Scope.md requirement)");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "Error initializing MSU TouchPanel: {0}", ex.Message);
                ErrorLog.Error("MSU TouchPanel Initialization Error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Console command to set MSU TouchPanel page
        /// </summary>
        private void SetMSUPage(string message)
        {
            if (_msuTouchPanel != null)
            {
                _msuTouchPanel.SetMSUPage(message);
            }
            else
            {
                CrestronConsole.PrintLine("MSU TouchPanel not initialized");
            }
        }

        /// <summary>
        /// Console command to show MSU TouchPanel status
        /// </summary>
        private void ShowMSUStatus(string message)
        {
            if (_msuTouchPanel != null)
            {
                _msuTouchPanel.ShowMSUStatus();
            }
            else
            {
                CrestronConsole.PrintLine("MSU TouchPanel not initialized");
            }
        }

        /// <summary>
        /// Event Handler for Ethernet events: Link Up and Link Down. 
        /// Use these events to close / re-open sockets, etc. 
        /// </summary>
        /// <param name="ethernetEventArgs">This parameter holds the values 
        /// such as whether it's a Link Up or Link Down event. It will also indicate 
        /// wich Ethernet adapter this event belongs to.
        /// </param>
        void ControllerEthernetEventHandler(EthernetEventArgs ethernetEventArgs)
        {
            switch (ethernetEventArgs.EthernetEventType)
            { // Determine the event type Link Up or Link Down
                case (eEthernetEventType.LinkDown):
                    // Next need to determine which adapter the event is for. 
                    // LAN is the adapter is the port connected to external networks.
                    if (ethernetEventArgs.EthernetAdapter == EthernetAdapterType.EthernetLANAdapter)
                    { }
                    break;
                case (eEthernetEventType.LinkUp):
                    if (ethernetEventArgs.EthernetAdapter == EthernetAdapterType.EthernetLANAdapter)
                    { }
                    break;
            }
        }

        /// <summary>
        /// Event Handler for Programmatic events: Stop, Pause, Resume.
        /// Use this event to clean up when a program is stopping, pausing, and resuming.
        /// This event only applies to this SIMPL#Pro program, it doesn't receive events
        /// for other programs stopping
        /// </summary>
        /// <param name="programStatusEventType"></param>
        void ControllerProgramEventHandler(eProgramStatusEventType programStatusEventType)
        {
            switch (programStatusEventType)
            {
                case (eProgramStatusEventType.Paused):
                    //The program has been paused.  Pause all user threads/timers as needed.
                    break;
                case (eProgramStatusEventType.Resumed):
                    //The program has been resumed. Resume all the user threads/timers as needed.
                    break;
                case (eProgramStatusEventType.Stopping):
                    //The program has been stopped.
                    //Close all threads. 
                    //Shutdown all Client/Servers in the system.
                    //General cleanup.
                    //Unsubscribe to all System Monitor events
                    // FlightTelemetry.Dispose(); // Not static
                    // Lighting.Dispose(); // Not static  
                    // TP01.Dispose(); // Not static
                    break;
            }
        }

        /// <summary>
        /// Event Handler for system events, Disk Inserted/Ejected, and Reboot
        /// Use this event to clean up when someone types in reboot, or when your SD /USB
        /// removable media is ejected / re-inserted.
        /// </summary>
        /// <param name="systemEventType"></param>
        void ControllerSystemEventHandler(eSystemEventType systemEventType)
        {
            switch (systemEventType)
            {
                case (eSystemEventType.DiskInserted):
                    //Removable media was detected on the system
                    break;
                case (eSystemEventType.DiskRemoved):
                    //Removable media was detached from the system
                    break;
                case (eSystemEventType.Rebooting):
                    //The system is rebooting. 
                    //Very limited time to preform clean up and save any settings to disk.
                    break;
            }
        }
    }
}

/// Hebrews 10 26-35
/// 26 If we deliberately keep on sinning after we have received the knowledge of the truth, no sacrifice for sins is left,
/// 27 but only a fearful expectation of judgment and of raging fire that will consume the enemies of God. 
/// 28 Anyone who rejected the law of Moses died without mercy on the testimony of two or three witnesses. 
/// 29 How much more severely do you think someone deserves to be punished who has trampled the Son of God underfoot, 
/// who has treated as an unholy thing the blood of the covenant that sanctified them, and who has insulted the Spirit of grace? 
/// 30 For we know him who said, �It is mine to avenge; I will repay,�[d] and again, �The Lord will judge his people.�[e] 
/// 31 It is a dreadful thing to fall into the hands of the living God.
/// 32 Remember those earlier days after you had received the light, when you endured in a great conflict full of suffering.
/// 33 Sometimes you were publicly exposed to insult and persecution; at other times you stood side by side with those who were so treated.
/// 34 You suffered along with those in prison and joyfully accepted the confiscation of your property, because you knew that you yourselves had better and lasting possessions. 
/// 35 So do not throw away your confidence; it will be richly rewarded.


using System;
using Crestron.SimplSharp;                          	// For Basic SIMPL# Classes
using Crestron.SimplSharpPro;                       	// For Basic SIMPL#Pro classes
using Crestron.SimplSharpPro.CrestronThread;        	// For Threading
using core_tools;
using Crestron.SimplSharpPro.Diagnostics;		    	// For System Monitor Access
using Crestron.SimplSharpPro.DeviceSupport;         	// For Generic Device Support
using Crestron.SimplSharpPro.UI;
using flexpod.Services;
using flexpod.Controllers;

namespace flexpod
{
    public class ControlSystem : CrestronControlSystem
    {      
        private readonly uint _touchPanelOneIPID = 0x2a;
        private SystemInitializationService _initializationService;
        private TP01 _touchPanel;

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
                Thread.MaxNumberOfUserThreads = 400;
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
                var dIO = Global.DIO;

                var flightTelemetry = new FlightTelemetry("flightTelemtry", "Flight-Telemtry");
                CrestronConsole.AddNewConsoleCommand(PrintDevMon, "printDevMon", "Print all Device Monitor devices", ConsoleAccessLevelEnum.AccessOperator);

                var tp01 = new TP01("tp01", "TP01", panel, flightTelemetry);
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
                
                // Initialize touch panel first
                Debug.Console(0, "INIT: Initializing Touch Panel Interface");
                _touchPanel = new TP01("tp01", "TP01", panel, flightTelemetry);
                
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
                    
                    // Connect MSU controller to touch panel if available
                    var msuController = _initializationService.MSUController;
                    if (msuController != null)
                    {
                        _touchPanel.SetMSUController(msuController);
                        Debug.Console(1, "INIT: Touch panel connected to MSU controller");
                    }
                }
                else
                {
                    Debug.Console(0, "INIT: MSU System initialization failed - see errors above");
                }
                
                // Add console command for configuration reload
                CrestronConsole.AddNewConsoleCommand(ReloadConfiguration, "reloadConfig", 
                    "Reload MSU configuration files", ConsoleAccessLevelEnum.AccessOperator);
                
                Debug.Console(0, "******************* InitializeSystem() Complete **********************");

                SystemMonitor.ProgramInitialization.ProgramInitializationComplete = true;
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in InitializeSystem: {0}", e.Message);
                ErrorLog.Error("Error in InitializeSystem: {0}", e.StackTrace);
            }
        }

        /// <summary>
        /// Returns a BasicTriListWithSmartObject panel
        /// </summary>
        /// <param name="ipId"></param>
        /// <returns></returns>
        private BasicTriListWithSmartObject GetPanelForType (uint ipId)
        {
            var tsw1070 = new Tsw1070(ipId, Global.ControlSystem);
            tsw1070.Register();
            return tsw1070;
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
                Debug.Console(1, "Local Config - Remote Server: {0}:{1}", 
                    args.LocalConfig.Remote.IP, args.LocalConfig.Remote.Port);
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
        private void OnSystemInitializationComplete(object sender, InitializationCompleteEventArgs args)
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
        private void OnSystemInitializationError(object sender, InitializationErrorEventArgs args)
        {
            Debug.Console(0, "System initialization error: {0}", args.ErrorMessage);
            ErrorLog.Error("MSU System Initialization Error: {0}", args.ErrorMessage);
        }

        /// <summary>
        /// Event handler for initialization phase changes
        /// </summary>
        private void OnInitializationPhaseChanged(object sender, InitializationPhaseEventArgs args)
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
                    FlightTelemetry.Dispose();
                    Lighting.Dispose();
                    TP01.Dispose();
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


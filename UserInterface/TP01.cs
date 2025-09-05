using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using core_tools;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharp.CrestronIO;
using Crestron.SimplSharpPro.DeviceSupport;
using flexpod.Controllers;

namespace flexpod
{
    internal class TP01 : TouchPanelBase, IDisposable
    {
        #region Global Variables, Properties, Events     
        
        private bool alreadyDisposed = false;
        private uint maxPageCount = 10;
        private CTimer onlinePageTransitionTimer;
        private readonly LoyaltyID loyaltyID;
        internal override uint SubPage { get; set; }
        internal override uint MaxNumItems
        {
            get { return maxPageCount; }
            set { maxPageCount = value; }
        }
        internal override ConcurrentDictionary<uint, bool> PageDictionary { get; set; }
        internal override ConcurrentDictionary<uint, bool> PopupPageDictionary { get; set; }
        internal override ConcurrentDictionary<uint, bool> PanelWidePopupPageDictionary { get; set; }
        private uint currentPageKey;
        private readonly string sgdFileNamePattern = "_flex*.sgd*";
        readonly StringBuilder PinEntryBuilder = new StringBuilder(5);
        readonly StringBuilder PinEntryStarBuilder = new StringBuilder(5);
        internal SmartObjectNumeric PinKeypad;
        internal SmartObjectDynamicList TelemetryList, PassengersList, MediaItemsList;
        internal FlightTelemetry FlightTelemetryInfo { get; }
        private MSUController _msuController;
        #endregion

        #region Global Constants
        private const uint StartupTimeOut = 30000;
        #endregion

        #region Global Constants: Touch Panel Page, Subpage, Popup, PanelWidePopup
        private const uint PageJoinStart = 101;                     // Start of assigned page joins
        private readonly uint SubPageJoinStart = 9999;              // See constructor for value assignment
        private readonly uint PopupPageJoinStart = 9999;            // See constructor for value assignment
        private readonly uint PanelWidePopupPageJoinStart = 9999;   // See constructor for value assignment
        private readonly uint SubPageJoinEnd = 9999;                // See constructor for value assignment
        private readonly uint PopupPageJoinEnd = 9999;              // See constructor for value assignment
        private readonly uint PanelWidePopupPageJoinEnd = 9999;     // See constructor for value assignment
        #endregion

        #region Console Commands
        internal void SetTp01Page(string command)
        {
            var targets = command.Split(' ');
            var stringTarget = targets[0].Trim();
            var target = uint.Parse(stringTarget);
            SetPage(target);
        }

        internal void SetTp01SubPage(string command)
        {
            var targets = command.Split(' ');
            var stringTarget = targets[0].Trim();
            var target = uint.Parse(stringTarget);
            SetSubPage(target);
        }

        internal void SetTp01PopupPage(string command)
        {
            var targets = command.Split(' ');
            var stringTarget = targets[0].Trim();
            var target = uint.Parse(stringTarget);
            SetPopupPage(target);
        }

        internal void SetTp01PanelWidePopupPage(string command)
        {
            var targets = command.Split(' ');
            var stringTarget = targets[0].Trim();
            var target = uint.Parse(stringTarget);
            SetPanelWidePopupPage(target);
        }
        #endregion

        /// <summary>
        /// Default Constructor for TP01
        /// </summary>
        internal TP01(string keyId, string friendlyId, BasicTriListWithSmartObject panel, FlightTelemetry flightTelemetry) : base(keyId, friendlyId, panel)
        {
            try
            {
                Key = keyId;
                Name = friendlyId;
                
                DeviceManager.AddDevice(Key, this);             
                Panel = panel;
                FlightTelemetryInfo = flightTelemetry;

                SubPageJoinStart = PageJoinStart + MaxNumItems;
                PopupPageJoinStart = SubPageJoinStart + MaxNumItems;
                PanelWidePopupPageJoinStart = PopupPageJoinStart + MaxNumItems;
                SubPageJoinEnd = SubPageJoinStart + MaxNumItems - 1;
                PopupPageJoinEnd = PopupPageJoinStart + MaxNumItems - 1;
                PanelWidePopupPageJoinEnd = PanelWidePopupPageJoinStart + MaxNumItems - 1;
                PageDictionary = new ConcurrentDictionary<uint, bool>();
                PopupPageDictionary = new ConcurrentDictionary<uint, bool>();
                PanelWidePopupPageDictionary = new ConcurrentDictionary<uint, bool>();
                loyaltyID = new LoyaltyID();

                Panel.OnlineStatusChange += (sender, args) => OnlineStatusChangeHandler(args);

                Panel.SetSigTrueAction((uint)TouchPanelJoins.Digital.FooterPageNext, () => SetNextPageDictionaryItem());
                Panel.SetSigTrueAction((uint)TouchPanelJoins.Digital.FooterPagePrevious, () => SetPreviousPageDictionaryItem());
                Panel.SetSigTrueAction((uint)TouchPanelJoins.PanelWidePopUps.ResetAll, () => ResetAllPanelWidePopups());
                Panel.SetSigTrueAction((uint)TouchPanelJoins.PanelWidePopUps.Signin, () => SetPanelWidePopupPage((uint)TouchPanelJoins.PanelWidePopUps.Signin));
                Panel.SetSigTrueAction((uint)TouchPanelJoins.PanelWidePopUps.ReliefStation, () => SetPanelWidePopupPage((uint)TouchPanelJoins.PanelWidePopUps.ReliefStation));

                CrestronConsole.AddNewConsoleCommand(SetTp01Page, "setTp01Page", "Single uint value to set the Page", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(SetTp01SubPage, "setTp01SubPage", "Single uint value to set the Sub Page", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(SetTp01PopupPage, "setTp01PopupPage", "Single uint value to set the PopUp Page", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(SetTp01PanelWidePopupPage, "setTp01WarningPage", "Single uint value to set the Warning Page", ConsoleAccessLevelEnum.AccessOperator);

                CreateSubPageMap();
      
                string[] matchingFiles = Directory.GetFiles(Global.ApplicationDirectoryPathPrefix, sgdFileNamePattern);

                if (matchingFiles.Length == 0)
                {   
                        Debug.Console(0, this, "Unable to find matching SGD file in User or application SGD folder. Exiting touchpanel load.");
                        return;                    
                }

                var sgdName = matchingFiles[0];
                Debug.Console(0, this, "Loading Smart Object file: {0}...", sgdName);
                Panel.LoadSmartObjects(sgdName);

                if(Panel.SmartObjects.Contains((uint)TouchPanelJoins.SmartObject.Keypad)) { PinKeypad = new SmartObjectNumeric(Panel.SmartObjects[(uint)TouchPanelJoins.SmartObject.Keypad], true); }
                SetupPinModal();

                if (Panel.SmartObjects.Contains((uint)TouchPanelJoins.SmartObject.FlightTelemetry)) { TelemetryList = new SmartObjectDynamicList(Panel.SmartObjects[(uint)TouchPanelJoins.SmartObject.FlightTelemetry], true, 3100); }
                ResetTelemetryList();
                PopulateTelemetryList();
                PopulateTelemetryTopBarIcons();

                // add subscribe to FlightTelemetryDataReceived event
                flightTelemetry.DataReceived += (s, e) => { Debug.Console(2, this, "FlightTelemetryDataReceived event raised"); };
            }
            catch (Exception e)
            {
                Debug.Console(2, this, "Error in TP01 Constructor: {0}", e.Message);
            }
        }

        /// <summary>
        /// Set the MSU Controller for this touch panel
        /// </summary>
        /// <param name="msuController">MSU Controller instance</param>
        internal void SetMSUController(MSUController msuController)
        {
            try
            {
                _msuController = msuController;
                Debug.Console(1, this, "MSU Controller connected to touch panel");
                
                if (_msuController != null)
                {
                    // Subscribe to MSU events for UI updates
                    _msuController.StatusChanged += OnMSUStatusChanged;
                    _msuController.ConfigurationChanged += OnMSUConfigurationChanged;
                    
                    // Update initial UI state based on MSU status
                    UpdateMSUStatusDisplay();
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting MSU Controller: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Event handler for MSU status changes
        /// </summary>
        private void OnMSUStatusChanged(object sender, MSUStatusEventArgs args)
        {
            try
            {
                Debug.Console(1, this, "MSU Status changed: {0}", args.Status);
                UpdateMSUStatusDisplay();
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error handling MSU status change: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Event handler for MSU configuration changes
        /// </summary>
        private void OnMSUConfigurationChanged(object sender, MSUConfigEventArgs args)
        {
            try
            {
                Debug.Console(1, this, "MSU Configuration changed");
                UpdateMSUConfigurationDisplay();
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error handling MSU configuration change: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update the UI to reflect current MSU status
        /// </summary>
        private void UpdateMSUStatusDisplay()
        {
            try
            {
                if (_msuController == null || Panel == null) return;

                // Update status indicators on the touch panel
                // This would map to specific joins in your SGD file
                var status = _msuController.GetCurrentStatus();
                
                // Example: Update HVAC status
                Panel.BooleanInput[5001].BoolValue = status.HVACConnected;
                Panel.StringInput[5001].StringValue = status.HVACStatus;
                
                // Example: Update Music status
                Panel.BooleanInput[5002].BoolValue = status.MusicConnected;
                Panel.StringInput[5002].StringValue = status.MusicStatus;
                
                Debug.Console(2, this, "MSU status display updated");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error updating MSU status display: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update the UI to reflect current MSU configuration
        /// </summary>
        private void UpdateMSUConfigurationDisplay()
        {
            try
            {
                if (_msuController == null || Panel == null) return;

                // Update configuration information on the touch panel
                Debug.Console(2, this, "MSU configuration display updated");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error updating MSU configuration display: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Dispose of Class
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            if (DeviceManager.ContainsKey(Key)) { DeviceManager.RemoveDevice(Key); }
        }

        /// <summary>
        /// Part of the implementation of the IDisposable pattern
        /// </summary>
        /// <param name="disposing">indicates whether the method is being called from the Dispose method (with disposing set to true) or from the finalizer (with disposing set to false).</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!alreadyDisposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here
                    // Managed Resources would include:
                    // - Managed objects that implement IDisposable
                    // - Crestron Simpl# Pro wrappers that wrap unmanaged resources
                    // - Other objects holding references to resources that should be released

                    if (Panel != null)
                    {
                        Panel.Dispose();
                        Panel = null;
                    }
                    if (onlinePageTransitionTimer != null)
                    {
                        onlinePageTransitionTimer.Stop();
                        onlinePageTransitionTimer.Dispose();
                        onlinePageTransitionTimer = null;
                    }

                    if (PinKeypad != null)
                    {
                        //PinKeypad.Dispose();
                        PinKeypad = null;
                    }

                    if (TelemetryList != null)
                    {
                        //TelemetryList.Dispose();
                        TelemetryList = null;
                    }

                    if (PassengersList != null)
                    {
                        //PassengersList.Dispose();
                        PassengersList = null;
                    }

                    if (MediaItemsList != null)
                    {
                        //MediaItemsList.Dispose();
                        MediaItemsList = null;
                    }
                }
                // Dispose unmanaged resources here, meaning Release them explicitly (e.g., close file handles, free memory)
                // Unmanaged Resources would include:
                // - COM objects that are not part of the .NET Framework
                // - Pointers to blocks of memory created using local memory allocation
                // - Handles to OS resources such as files, devices, GDI objects, etc.
                // - Subscriptions to external system events
                // - Anything that implements IDisposable
                // - Network connections, database connections
                alreadyDisposed = true;
            }
        }

        /// <summary>
        /// Destructor for TP01 to ensure resources are released if Dispose is not called explicitly
        /// </summary>
        ~TP01()
        {
            Dispose(false);
        }

        /// <summary>
        /// Event handler for online status change
        /// </summary>
        /// <param name="args"></param>
        internal void OnlineStatusChangeHandler(OnlineOfflineEventArgs args)
        {
            Debug.Console(2, this, "Panel OnlineOfflineEventArgs Triggered");
            if (args.DeviceOnLine)
            {
                Debug.Console(2, this, "Panel is Online");
                SetPage((uint)TouchPanelJoins.Pages.Startup);
                onlinePageTransitionTimer = new CTimer(_ => SetPage((uint)TouchPanelJoins.Pages.Telemetry), null, StartupTimeOut);
            }
            else
            {
                Debug.Console(2, this, "Panel is Offline");
                onlinePageTransitionTimer?.Stop();
            }
        }
        /// <summary>
        /// Create initial collection of PopUp and PanelWidePopUp pages. Set all pages to false.
        /// </summary>
        internal void CreateSubPageMap()
        {
            for (uint i = PopupPageJoinStart; i < PopupPageJoinEnd; i++)
                { PopupPageDictionary.AddOrUpdate(i, false, (key, fakeBool) => false); }

            for (uint i = PanelWidePopupPageJoinStart; i < PanelWidePopupPageJoinEnd; i++)
            { PanelWidePopupPageDictionary.AddOrUpdate(i, false, (key, fakeBool) => false); }
            
            PageDictionary.AddOrUpdate((uint)TouchPanelJoins.Pages.Telemetry, false, (key, fakeBool) => false);
            PageDictionary.AddOrUpdate((uint)TouchPanelJoins.Pages.Lighting, false, (key, fakeBool) => false);
            PageDictionary.AddOrUpdate((uint)TouchPanelJoins.Pages.MediaRouter, false, (key, fakeBool) => false);
            PageDictionary.AddOrUpdate((uint)TouchPanelJoins.Pages.MediaPlayer, false, (key, fakeBool) => false);
            PageDictionary.AddOrUpdate((uint)TouchPanelJoins.Pages.Game, false, (key, fakeBool) => false);
        }

        /// <summary>
        /// Recall the next page dictionary item
        /// </summary>
        internal void SetNextPageDictionaryItem()
        {
            // Check if the dictionary is empty
            if (PageDictionary.Count == 0)
            {
                Debug.Console(2, this, "PageDictionary is empty. Previous page cannot be set.");
                return;
            }

            // Check if the currentPageKey is 0. If so, set it to the FirstOrDefault value.
            if (currentPageKey == 0)
            {
                Debug.Console(2, this, "currentPageKey is 0. Setting to FirstOrDefault value: {0}", PageDictionary.Keys.FirstOrDefault());
                currentPageKey = PageDictionary.Keys.FirstOrDefault();
            }

            // Get the list of keys
            var keys = new List<uint>(PageDictionary.Keys);
            int currentIndex = keys.IndexOf(currentPageKey);

            Debug.Console(2, this, "SetPreviousPageDictionaryItem [currentIndex] value: {0}", currentIndex);

            // Check if the current key exists in the dictionary. IndexOf will return -1 if the key is not found.
            if (currentIndex == -1) { Debug.Console(1, this, "PageDctionary Index not found."); return; }

            // Set the current key's value to false
            PageDictionary[currentPageKey] = false;
            ClearPage(currentPageKey);
            Debug.Console(2, this, "SetNextPageDictionaryItem currentIndex value: {0}", currentIndex);

            // If at the Last, wrap around to the first key
            if (currentIndex == keys.Count - 1)
            { 
                currentPageKey = keys.FirstOrDefault();
                Debug.Console(2, this, "SetNextPageDictionaryItem currentPageKey set to FirstOrDefault value: {0}", currentPageKey);
            }
            else
            {
                // Move to the Next key
                currentPageKey = keys[currentIndex + 1];
                Debug.Console(2, this, "SetNextPageDictionaryItem currentPageKey set to currentIndex + 1 value: {0}", currentPageKey);
            }

            // Set the new current key's value to true
            PageDictionary[currentPageKey] = true;
            SetPage(currentPageKey);
        }

        /// <summary>
        /// Recall the previous page dictionary item
        /// </summary>
        internal void SetPreviousPageDictionaryItem()
        {
            try
            {
                // Check if the dictionary is empty
                if (PageDictionary.Count == 0)
                {
                    Debug.Console(2, this, "PageDictionary is empty. Previous page cannot be set.");
                    return;
                }

                // Check if the currentPageKey is 0. If so, set it to the FirstOrDefault value.
                if (currentPageKey == 0)
                {
                    Debug.Console(2, this, "currentPageKey is 0. Setting to FirstOrDefault value: {0}", PageDictionary.Keys.FirstOrDefault());
                    currentPageKey = PageDictionary.Keys.FirstOrDefault();
                }

                // Get the list of keys
                var keys = new List<uint>(PageDictionary.Keys);
                // Find the Index of the Current Page Key
                int currentIndex = keys.IndexOf(currentPageKey);

                Debug.Console(2, this, "SetPreviousPageDictionaryItem [currentIndex] value: {0}", currentIndex);

                // Check if the current key exists in the dictionary.
                // Note that IndexOf will return -1 if the key is not found.
                if (currentIndex == -1)
                {
                    Debug.Console(2, this, "SetPreviousPageDictionaryItem [currentPageKey] not found in PageDictionary.");
                    return;
                }
                
                // Set the current key's value to false
                PageDictionary[currentPageKey] = false;
                ClearPage(currentPageKey);                

                // If at the first key, wrap around to the last key
                if (currentIndex == 0)
                {
                    // Move to the last key
                    currentPageKey = keys.LastOrDefault();
                    Debug.Console(2, this, "SetPreviousPageDictionaryItem [currentPageKey] set to LastOrDefault value: {0}", currentPageKey);
                }
                else
                {
                    // Move to the previous key
                    currentPageKey = keys[currentIndex - 1];
                    Debug.Console(2, this, "SetPreviousPageDictionaryItem [currentPageKey] set to currentIndex - 1 value: {0}", currentPageKey);
                }

                // Set the new current key's value to true
                PageDictionary[currentPageKey] = true;
                SetPage(currentPageKey);
            }
            catch (Exception e)
            {
                Debug.Console(2, this, "Error in SetPreviousPageDictionaryItem: {0}", e.Message);
            }
        }

        /// <summary>
        /// Wire up the Smart Graphics Pin keypad and buttons
        /// </summary>
        internal void SetupPinModal()
        {
            Panel.SetSigFalseAction((uint)TouchPanelJoins.Digital.PanelWidePopUpClose, CancelPinDialog);
            PinKeypad = new SmartObjectNumeric(Panel.SmartObjects[(uint)TouchPanelJoins.SmartObject.Keypad], true);
            PinKeypad.Digit0.UserObject = new Action<bool>(b => { if (b) DialPinDigit('0'); });
            PinKeypad.Digit1.UserObject = new Action<bool>(b => { if (b) DialPinDigit('1'); });
            PinKeypad.Digit2.UserObject = new Action<bool>(b => { if (b) DialPinDigit('2'); });
            PinKeypad.Digit3.UserObject = new Action<bool>(b => { if (b) DialPinDigit('3'); });
            PinKeypad.Digit4.UserObject = new Action<bool>(b => { if (b) DialPinDigit('4'); });
            PinKeypad.Digit5.UserObject = new Action<bool>(b => { if (b) DialPinDigit('5'); });
            PinKeypad.Digit6.UserObject = new Action<bool>(b => { if (b) DialPinDigit('6'); });
            PinKeypad.Digit7.UserObject = new Action<bool>(b => { if (b) DialPinDigit('7'); });
            PinKeypad.Digit8.UserObject = new Action<bool>(b => { if (b) DialPinDigit('8'); });
            PinKeypad.Digit9.UserObject = new Action<bool>(b => { if (b) DialPinDigit('9'); });
        }

        /// <summary>
        /// Build Dial Pin via Input
        /// </summary>
        /// <param name="d"></param>
        internal void DialPinDigit(char d)
        {
            PinEntryBuilder.Append(d);
            var len = PinEntryBuilder.Length;

            PinEntryStarBuilder.Append("*");
            Panel.SetString((uint)TouchPanelJoins.Serial.PinKeypadEntryText, PinEntryStarBuilder.ToString());

            // check incoming length and action when len is appropriate
            if (len == 5)
            {
                try 
                { 
                    ushort.TryParse(PinEntryBuilder.ToString(), out ushort memberID);
                    Global.MemberName = loyaltyID.LookupID(memberID);
                }
                catch (Exception e)
                {
                    Debug.Console(2, this, "Error in DialPinDigit: {0}", e.Message);
                    return;
                }

                PinEntryBuilder.Remove(0, len); // clear it either way
                PinEntryStarBuilder.Remove(0, len); // clear it either way
                Panel.SetString((uint)TouchPanelJoins.Serial.PinKeypadEntryText, PinEntryStarBuilder.ToString());
            }
        }

        /// <summary>
        /// Cancel the Pin Dialog, clear the Pin entry, and close the dialog
        /// </summary>
        internal void CancelPinDialog()
        {
            PinEntryBuilder.Remove(0, PinEntryBuilder.Length);
            PinEntryStarBuilder.Remove(0, PinEntryStarBuilder.Length);
            Panel.SetString((uint)TouchPanelJoins.Serial.PinKeypadEntryText, "");
            Panel.SetBool((uint)TouchPanelJoins.PanelWidePopUps.Signin, false);
        }

        /// <summary>
        /// Reset all Telemetry List items
        /// </summary>
        internal void ResetTelemetryList()
        {
            TelemetryList.SetItemMainText(1, "Altitude: TBD");
            TelemetryList.SetItemMainText(2, "Vertical Speed: TBD");
            TelemetryList.SetItemMainText(3, "Heading: TBD");
            TelemetryList.SetItemMainText(4, "Airspeed: TBD");
            TelemetryList.SetItemMainText(5, "Ext. Temperature: TBD");
        }

        /// <summary>
        /// Populate the Telemetry List with Flight Telemetry Info
        /// </summary>
        internal void PopulateTelemetryList()
        {
            TelemetryList.SetItemMainText(1, $"Altitude: {FlightTelemetryInfo.Altitude}");
            TelemetryList.SetItemMainText(2, $"Vertical Speed: {FlightTelemetryInfo.VerticalSpeed}");
            TelemetryList.SetItemMainText(3, $"Heading: {FlightTelemetryInfo.CompassDirection}");
            TelemetryList.SetItemMainText(4, $"Airspeed: {FlightTelemetryInfo.Airspeed}");
            TelemetryList.SetItemMainText(5, $"Ext. Temperature: {FlightTelemetryInfo.Temperature}");
        }

        /// <summary>
        /// Populate the Top Bar Icons with Flight Telemetry Info
        /// </summary>
        internal void PopulateTelemetryTopBarIcons()
        {
            if(FlightTelemetryInfo.CrewAnnouncementInProgressFlag)
                Panel.SetUshort((uint)TouchPanelJoins.Analog.CrewAnnouncementStatus, (ushort)TouchPanelJoins.TopBarIconMap.OrangeMessage);
            else
                Panel.SetUshort((uint)TouchPanelJoins.Analog.CrewAnnouncementStatus, (ushort)TouchPanelJoins.TopBarIconMap.GreyedOutHeadphones);

            if (FlightTelemetryInfo.FastenSeatbeltsFlag)
                Panel.SetUshort((uint)TouchPanelJoins.Analog.SeatbeltStatus, (ushort)TouchPanelJoins.TopBarIconMap.RedSeatbeltsRequired);
            else
                Panel.SetUshort((uint)TouchPanelJoins.Analog.SeatbeltStatus, (ushort)TouchPanelJoins.TopBarIconMap.GreenPersonWalking);

            if (FlightTelemetryInfo.InFlightFlag)
                Panel.SetUshort((uint)TouchPanelJoins.Analog.FlightStatus, (ushort)TouchPanelJoins.TopBarIconMap.BlueFlightStatusInFlight);
            else
                Panel.SetUshort((uint)TouchPanelJoins.Analog.FlightStatus, (ushort)TouchPanelJoins.TopBarIconMap.OrangeFlightStatusOnGround);

            if (FlightTelemetryInfo.CabinPressureOxygenFlag)
                Panel.SetUshort((uint)TouchPanelJoins.Analog.OxygenStatus, (ushort)TouchPanelJoins.TopBarIconMap.GreyedOutBlankInactive);
            else
                Panel.SetUshort((uint)TouchPanelJoins.Analog.OxygenStatus, (ushort)TouchPanelJoins.TopBarIconMap.RedOxygenRequired);

            if(FlightTelemetryInfo.CabinSecuredFlag)
                Panel.SetUshort((uint)TouchPanelJoins.Analog.CabinStatus, (ushort)TouchPanelJoins.TopBarIconMap.OrangeLock);
            else
                Panel.SetUshort((uint)TouchPanelJoins.Analog.CabinStatus, (ushort)TouchPanelJoins.TopBarIconMap.GreyedOutLock);
        }
    }
}
using core_tools;
using System.Text;
using System.Collections.Concurrent;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Services;
using musicStudioUnit.UserInterface;

namespace musicStudioUnit
{
    internal class TP01 : TouchPanelBase, IDisposable
    {
        #region Global Variables, Properties, Events     
        
        private bool alreadyDisposed = false;
        private uint maxPageCount = 10;

        private CTimer? onlinePageTransitionTimer;

        internal override uint SubPage { get; set; }
        internal override uint MaxNumItems
        {
            get { return maxPageCount; }
            set { maxPageCount = value; }
        }
        // Change from nullable to non-nullable to match base class signature
        internal override ConcurrentDictionary<uint, bool>? PageDictionary { get; set; }
        internal override ConcurrentDictionary<uint, bool>? PopupPageDictionary { get; set; }
        internal override ConcurrentDictionary<uint, bool>? PanelWidePopupPageDictionary { get; set; }
        private uint currentPageKey;
        readonly StringBuilder PinEntryBuilder = new StringBuilder(5);
        readonly StringBuilder PinEntryStarBuilder = new StringBuilder(5);
        private MSUController? _msuController;
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
        /// Default Constructor for touch panel
        /// </summary>
        internal TP01(string keyId, string friendlyId, BasicTriListWithSmartObject panel) : base(keyId, friendlyId, panel)
        {
            try
            {
                Key = keyId;
                Name = friendlyId;
                
                DeviceManager.AddDevice(Key, this);             
                Panel = panel;

                SubPageJoinStart = PageJoinStart + MaxNumItems;
                PopupPageJoinStart = SubPageJoinStart + MaxNumItems;
                PanelWidePopupPageJoinStart = PopupPageJoinStart + MaxNumItems;
                SubPageJoinEnd = SubPageJoinStart + MaxNumItems - 1;
                PopupPageJoinEnd = PopupPageJoinStart + MaxNumItems - 1;
                PanelWidePopupPageJoinEnd = PanelWidePopupPageJoinStart + MaxNumItems - 1;
                PageDictionary = new ConcurrentDictionary<uint, bool>();
                PopupPageDictionary = new ConcurrentDictionary<uint, bool>();
                PanelWidePopupPageDictionary = new ConcurrentDictionary<uint, bool>();

                Panel.OnlineStatusChange += (sender, args) => OnlineStatusChangeHandler(args);

                Panel.SetSigTrueAction((uint)TouchPanelJoins.Digital.FooterPageNext, () => SetNextPageDictionaryItem());
                Panel.SetSigTrueAction((uint)TouchPanelJoins.Digital.FooterPagePrevious, () => SetPreviousPageDictionaryItem());
                Panel.SetSigTrueAction((uint)TouchPanelJoins.PanelWidePopUps.ResetAll, () => ResetAllPanelWidePopups());

                CrestronConsole.AddNewConsoleCommand(SetTp01Page, "setTp01Page", "Single uint value to set the Page", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(SetTp01SubPage, "setTp01SubPage", "Single uint value to set the Sub Page", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(SetTp01PopupPage, "setTp01PopupPage", "Single uint value to set the PopUp Page", ConsoleAccessLevelEnum.AccessOperator);
                CrestronConsole.AddNewConsoleCommand(SetTp01PanelWidePopupPage, "setTp01WarningPage", "Single uint value to set the Warning Page", ConsoleAccessLevelEnum.AccessOperator);

                CreateSubPageMap();
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
                    _msuController.MSUInitialized += OnMSUInitialized;
                    _msuController.MSUError += OnMSUError;
                    
                    // Update initial UI state based on MSU status
                    UpdateMSUStatusDisplay();
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting MSU Controller: {0}", ex.Message);
            }
        }

        private void OnMSUInitialized(object? sender, MSUInitializedEventArgs args)
        {
            try
            {
                var msuName = args.MSUConfig?.MSU_NAME ?? "Unknown";
                Debug.Console(1, this, "MSU Initialized: {0}", msuName);
                UpdateMSUStatusDisplay();
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error handling MSU initialization: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Event handler for MSU errors
        /// </summary>
        private void OnMSUError(object? sender, MSUErrorEventArgs args)
        {
            try
            {
                Debug.Console(0, this, "MSU Error: {0}", args.ErrorMessage);
                // Update UI to show error state
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error handling MSU error: {0}", ex.Message);
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
                var systemInfo = _msuController.GetSystemInfo();
                
                // Example: Update system status
                Panel.StringInput[5001].StringValue = systemInfo.MSUName;
                Panel.StringInput[5002].StringValue = systemInfo.IPAddress;
                
                Debug.Console(2, this, "MSU status display updated");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error updating MSU status display: {0}", ex.Message);
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
                    }
                    if (onlinePageTransitionTimer != null)
                    {
                        onlinePageTransitionTimer.Stop();
                        onlinePageTransitionTimer.Dispose();
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
                onlinePageTransitionTimer = new CTimer(_ => SetPage((uint)MSUTouchPanelJoins.Pages.Settings), new object(), StartupTimeOut);
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
            { 
                if(PopupPageDictionary == null) Debug.Console(0, this, "PopupPageDictionary is null");
                else
                    PopupPageDictionary.AddOrUpdate(i, false, (key, fakeBool) => false); 
            }

            for (uint i = PanelWidePopupPageJoinStart; i < PanelWidePopupPageJoinEnd; i++)
            { 
                if(PanelWidePopupPageDictionary == null) Debug.Console(0, this, "PanelWidePopupPageDictionary is null");
                else
                    PanelWidePopupPageDictionary.AddOrUpdate(i, false, (key, fakeBool) => false); 
            }
            
            if(PageDictionary == null) Debug.Console(0, this, "PageDictionary is null");  
        }

        /// <summary>
        /// Recall the next page dictionary item
        /// </summary>
        internal void SetNextPageDictionaryItem()
        {
            // Check if the dictionary is null or empty
            
            if(PageDictionary == null)
            {
                Debug.Console(2, this, "PageDictionary is null. Next page cannot be set.");
                return;
            }

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
                // Check if the dictionary is null or count is empty
                if(PageDictionary == null)
                {
                    Debug.Console(2, this, "PageDictionary is null. Previous page cannot be set.");
                    return;
                }
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
                    Global.MemberName = $"Member{memberID}"; // Simplified member lookup
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
        }
    }
}
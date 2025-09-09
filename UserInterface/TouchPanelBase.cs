using core_tools;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using System.Collections.Generic;
using core_tools;
using System.Collections.Concurrent;
using core_tools;
using Crestron.SimplSharpPro.CrestronThread;
using Crestron.SimplSharpPro.DeviceSupport;
using System;


using core_tools;
namespace musicStudioUnit
{
    public abstract class TouchPanelBase: IKeyName, IHasBasicTriListWithSmartObject
    {
        public string Key { get; set; }
        public string Name { get; set; }
        internal abstract ConcurrentDictionary<uint, bool> PageDictionary { get; set; }
        internal abstract uint SubPage { get; set; } // Single uint for exclusive subpage
        internal abstract ConcurrentDictionary<uint, bool> PopupPageDictionary { get; set; }
        internal abstract ConcurrentDictionary<uint, bool> PanelWidePopupPageDictionary { get; set; }
        internal abstract uint MaxNumItems { get; set; }
        public BasicTriListWithSmartObject Panel { get; protected set; }

        /// <summary>
        /// Default constructor for TouchPanelBase
        /// </summary>
        /// <param name="keyId"></param>
        /// <param name="friendlyId"></param>
        /// <param name="panel"></param>
        internal TouchPanelBase(string keyId, string friendlyId, BasicTriListWithSmartObject panel)
        {
            if(panel == null)
            {
                Debug.Console(0, this, "ERROR: Panel is null.  TouchPanel class will NOT work correctly.");
                return;
            }

            Panel = panel;
            Panel.SigChange += Panel_SigChange;

            this.Key = keyId;
            this.Name = friendlyId;

            SubPage = 0; // Set value to 0 during construction
        }

        /// <summary>
        /// Trigger page flip progmatically.  This will also clear active subpage.
        /// </summary>
        /// <param name="value"></param>
        internal void SetPage(uint value)
        {
            SubPage = 0;
            CrestronInvoke.BeginInvoke((o) => {
                Panel.SetBool(value, true);
                System.Threading.System.Threading.Thread.Sleep(100);
                Panel.SetBool(value, false);
            });                
            Debug.Console(2, this, "Page {0} > Set True.", value); 
            if(PageDictionary.ContainsKey(value))
                PageDictionary[value] = true;           
        }

        /// <summary>
        /// Sets the exclusive Subpage uint value.  This will clear any other subpage.
        /// </summary>
        /// <param name="value"></param>
        internal void SetSubPage(uint value) 
        {
            if(SubPage != value)
            {
                SubPage = value;
                Panel.SetBool(value, true);
                Debug.Console(2, this, "SubPage {0} > Set True.", value);
            }
        }

        /// <summary>
        /// Set a single PopupPage. Popups are not exclusive of each other.
        /// </summary>
        /// <param name="value"></param>
        internal void SetPopupPage(uint value)
        {
            Panel.SetBool(value, true);
            PopupPageDictionary[value] = true;
            Debug.Console(2, this, "PopupPage {0} > Set True.", value);
        }

        /// <summary>
        /// Set a single PanelWidePopupPage. Popups are not exclusive of each other.
        /// </summary>
        internal void SetPanelWidePopupPage(uint value)
        {
            Panel.SetBool(value, true);
            PopupPageDictionary[value] = true;
            Debug.Console(2, this, "PanelWidePopupPage {0} > Set True.", value);
        }

        /// <summary>
        /// Clears the exclusive Subpage uint value.
        /// </summary>
        /// <param name="value"></param>
        internal void ClearPage(uint value)
        {     
            SubPage = 0;
            Panel.SetBool(value, false);   
            Debug.Console(2, this, "Page {0} Cleared.", value); 
            if(PageDictionary.ContainsKey(value))
                PageDictionary[value] = false; 
  
        }

        /// <summary>
        /// Clears the exclusive Subpage uint value.
        /// </summary>
        /// <param name="value"></param>
        internal void ClearSubPage()
        {     
            Panel.SetBool(SubPage, false);
            Debug.Console(2, this, "SubPage {0} Cleared.", SubPage);
  
        }

        /// <summary>
        /// Clear a single PopupPage. Popups are not exclusive of each other.
        /// </summary>
        /// <param name="value"></param>
        internal void ClearPopupPage(uint value)
        {
            Panel.SetBool(value, false);
            PopupPageDictionary[value] = false;
            Debug.Console(2, this, "PopupPage {0} Cleared.", value);
        }

        /// <summary>
        /// Clear a single PanelWidePopupPage. Popups are not exclusive of each other.
        /// </summary>
        /// <param name="value"></param>
        internal void ClearPanelWidePopupPage(uint value)
        {
            Panel.SetBool(value, false);
            PopupPageDictionary[value] = false;
            Debug.Console(2, this, "PanelWidePopupPage {0} Cleared.", value);
        }

        /// <summary>
        /// Set all items in dictionary to false
        /// </summary>
        internal void ResetAllPopups()
        {
            try
            {
                // Create a list of keys to process
                var keys = new List<uint>(PopupPageDictionary.Keys);

                if (keys.Count == 0)
                {
                    Debug.Console(0, this, "ERROR: ResetAllPopups: No keys found.");
                    return;
                }
                // Loop through the list of keys and set the value to false
                foreach (var key in keys)
                {
                    Panel.SetBool(key, false);
                    PopupPageDictionary[key] = false;
                    Debug.Console(2, this, "ResetAllPopups {0} > Set False.", key);
                }
            }
            catch (Exception e)
            {
                Debug.Console(0, this, "ERROR: ResetAllPopups: {0}", e);
            }
        }

        /// <summary>
        /// Set all items in dictionary to false
        /// </summary>
        internal void ResetAllPanelWidePopups()
        {
            try
            {
                // Create a list of keys to process
                var keys = new List<uint>(PanelWidePopupPageDictionary.Keys);

                if(keys.Count == 0)
                {
                    Debug.Console(0, this, "ERROR: ResetAllPanelWidePopups: No keys found.");
                    return;
                }
                // Loop through the list of keys and set the value to false
                foreach (var key in keys)
                {
                    Panel.SetBool(key, false);
                    PanelWidePopupPageDictionary[key] = false;
                    Debug.Console(2, this, "PanelWidePopupPage {0} > Set False.", key);
                }
            }
            catch (Exception e)
            {
                Debug.Console(0, this, "ERROR: ResetAllPanelWidePopups: {0}", e);
            }
        }

        /// <summary>
        /// Reset all Subpages, Popups, and PanelWidePopups
        /// </summary>
        internal void ResetEverything()
        {
            SubPage = 0;
            ResetAllPopups();
            ResetAllPanelWidePopups();
        }

        private void Panel_SigChange(object currentDevice, Crestron.SimplSharpPro.SigEventArgs args)
        {       
            Debug.Console(2, this, "Sig change: {0} {1}={2}", args.Sig.Type, args.Sig.Number, args.Sig.StringValue);
            var uo = args.Sig.UserObject;
            if (uo is Action<bool>)
                (uo as Action<bool>)(args.Sig.BoolValue);
            else if (uo is Action<ushort>)
                (uo as Action<ushort>)(args.Sig.UShortValue);
            else if (uo is Action<string>)
                (uo as Action<string>)(args.Sig.StringValue);
        }

        private void Tsw_ButtonStateChange(GenericBase device, ButtonEventArgs args)
        {
            var uo = args.Button.UserObject;
            if (uo is Action<bool>)
                (uo as Action<bool>)(args.Button.State == eButtonState.Pressed);
        }
    }
}
using core_tools;
using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Services;
using musicStudioUnit.UserInterface;

namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// Combine Screen UI for MSU TouchPanel
    /// Implements Studio Combination controls per Client-Scope.md requirements:
    /// - Single Studio (default, uncombined)
    /// - Mega Studio (two adjoining units)
    /// - Monster Studio (three adjoining units)
    /// - Adjacent MSU detection (north, south, east, west - not diagonal)
    /// - Master unit controls all combination functions
    /// - Temperature synchronization across combined units
    /// - Music playback coordination (master unit only sends commands)
    /// </summary>
    public class CombineScreenUI : IDisposable
    {
        #region Private Fields

        private readonly BasicTriList _panel;
        private readonly StudioCombinationManager _combinationManager;
        private readonly object _lockObject = new object();

        // Combination state
        private StudioCombinationType _currentCombination = StudioCombinationType.Single;
        private List<MusicStudioUnit> _combinedUnits = new List<MusicStudioUnit>();
        private List<MusicStudioUnit> _availableAdjacentUnits = new List<MusicStudioUnit>();

        // Connection state
        private bool _isInitialized = false;
        private bool _disposed = false;

        #endregion

        #region Events

        /// <summary>
        /// Fired when combination configuration changes
        /// </summary>
        public event EventHandler<StudioCombinationChangedEventArgs>? CombinationChanged;

        /// <summary>
        /// Fired when UI requires navigation back to menu
        /// </summary>
        public event EventHandler? NavigateBackRequested;

        #endregion

        #region Public Properties

        /// <summary>
        /// Current combination type
        /// </summary>
        public StudioCombinationType CurrentCombination => _currentCombination;

        /// <summary>
        /// Whether this unit is currently combined with others
        /// </summary>
        public bool IsCombined => _currentCombination != StudioCombinationType.Single;

        /// <summary>
        /// Whether this unit is the master of a combination
        /// </summary>
        public bool IsMaster => _combinationManager?.IsMaster == true;

        /// <summary>
        /// List of currently combined units
        /// </summary>
        public IReadOnlyList<MusicStudioUnit> CombinedUnits => _combinedUnits.AsReadOnly();

        #endregion

        #region Constructor

        public CombineScreenUI(BasicTriList panel, StudioCombinationManager combinationManager)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _combinationManager = combinationManager ?? throw new ArgumentNullException(nameof(combinationManager));

            Debug.Console(1, "CombineScreenUI", "Initializing combine screen UI");

            InitializeUI();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize and show the combine screen
        /// </summary>
        public void Initialize()
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                try
                {
                    SetupEventHandlers();
                    RefreshAdjacentUnits();
                    UpdateCombinationStatus();
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "CombineScreenUI", "Error initializing combine screen: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Show the combine screen (entry point from MSU TouchPanel)
        /// </summary>
        public void Show()   
        {
            if (!_isInitialized)
            {
                Initialize();
            }
            // Show Combine PAGE, hide others
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.Pages.Settings].BoolValue = false;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.Pages.User].BoolValue = false;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.Pages.Music].BoolValue = false;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.Pages.Temperature].BoolValue = false;
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.Pages.Combine].BoolValue = true;
            RefreshAdjacentUnits();
            UpdateCombinationDisplay();
        }

        /// <summary>
        /// Hide the combine screen
        /// </summary>
        public void Hide()
        {
            // Hide Combine PAGE
            _panel.BooleanInput[(uint)MSUTouchPanelJoins.Pages.Combine].BoolValue = false;
        }

        /// <summary>
        /// Refresh available adjacent units for combination
        /// </summary>
        public void RefreshAdjacentUnits()
        {
            if (_disposed) return;

            try
            {
                // Get current combination state from manager
                _currentCombination = _combinationManager.CombinationType;
                _combinedUnits = _combinationManager.CombinedMSUs.ToList();

                // Check what combinations are available
                CheckCombinationAvailability();

                Debug.Console(1, "CombineScreenUI", "Adjacent units refreshed - Available combinations updated");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "CombineScreenUI", "Error refreshing adjacent units: {0}", ex.Message);
                ShowError("Failed to refresh adjacent units");
            }
        }

        #endregion

        #region Private Methods - Initialization

        private void InitializeUI()
        {
            try
            {
                // Setup touch panel button events
                SetupTouchPanelEvents();

                // Initialize display
                ClearCombinationDisplay();
                UpdateCombinationStatus();

                core_tools.Debug.Console(2, "CombineScreenUI", "UI initialized");
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "CombineScreenUI", "Error initializing UI: {0}", ex.Message);
            }
        }

        private void SetupEventHandlers()
        {
            if (_combinationManager != null)
            {
                _combinationManager.CombinationChanged += OnCombinationChanged;
                core_tools.Debug.Console(2, "CombineScreenUI", "Event handlers configured");
            }
        }

        private void SetupTouchPanelEvents()
        {
            _panel.SigChange += (device, args) =>
            {
                if (args.Sig.Type != eSigType.Bool || !args.Sig.BoolValue) return;

                uint join = args.Sig.Number;
                HandleTouchPanelButtonPress(join);
            };
        }

        #endregion

        #region Private Methods - Touch Panel Event Handling

        private void HandleTouchPanelButtonPress(uint join)
        {
            try
            {
                switch (join)
                {
                    case MSUTouchPanelJoins.Combine.SingleStudioButton:
                        OnSingleStudioSelected();
                        break;
                    case MSUTouchPanelJoins.Combine.MegaStudioButton:
                        OnMegaStudioSelected();
                        break;
                    case MSUTouchPanelJoins.Combine.MonsterStudioButton:
                        OnMonsterStudioSelected();
                        break;
                    case MSUTouchPanelJoins.Combine.RefreshButton:
                        OnRefreshPressed();
                        break;
                    case MSUTouchPanelJoins.MenuBar.BackButton:
                        OnBackButtonPressed();
                        break;
                }
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "CombineScreenUI", "Error handling button press for join {0}: {1}", join, ex.Message);
            }
        }

        #endregion

        #region Private Methods - User Actions

        private void OnSingleStudioSelected()
        {
            lock (_lockObject)
            {
                try
                {
                    Debug.Console(1, "CombineScreenUI", "Single studio selected");

                    if (_currentCombination != StudioCombinationType.Single)
                    {
                        // Uncombine current combination
                        bool success = _combinationManager.UncombineStudios();

                        if (success)
                        {
                            Debug.Console(1, "CombineScreenUI", "Successfully uncombined studios");
                            ShowStatus("Studios uncombined successfully");
                        }
                        else
                        {
                            ShowError("Failed to uncombine studios");
                        }
                    }
                    else
                    {
                        ShowStatus("Already in Single Studio mode");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "CombineScreenUI", "Error selecting single studio: {0}", ex.Message);
                    ShowError("Error changing to Single Studio");
                }
            }
        }

        private void OnMegaStudioSelected()
        {
            lock (_lockObject)
            {
                try
                {
                    Debug.Console(1, "CombineScreenUI", "Mega studio selected");

                    // Check if Mega combination is available
                    bool canCombine = _combinationManager.CanCombineWithAdjacentMSUs(StudioCombinationType.Mega);

                    if (!canCombine)
                    {
                        ShowError("Mega Studio not available - check adjacent units");
                        return;
                    }

                    // Attempt to combine
                    bool success = _combinationManager.CombineStudios(StudioCombinationType.Mega);

                    if (success)
                    {
                        Debug.Console(1, "CombineScreenUI", "Successfully combined into Mega Studio");
                        ShowStatus("Mega Studio created successfully");
                    }
                    else
                    {
                        ShowError("Failed to create Mega Studio");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "CombineScreenUI", "Error selecting mega studio: {0}", ex.Message);
                    ShowError("Error creating Mega Studio");
                }
            }
        }

        private void OnMonsterStudioSelected()
        {
            lock (_lockObject)
            {
                try
                {
                    Debug.Console(1, "CombineScreenUI", "Monster studio selected");

                    // Check if Monster combination is available
                    bool canCombine = _combinationManager.CanCombineWithAdjacentMSUs(StudioCombinationType.Monster);

                    if (!canCombine)
                    {
                        ShowError("Monster Studio not available - need 3 adjacent units");
                        return;
                    }

                    // Attempt to combine
                    bool success = _combinationManager.CombineStudios(StudioCombinationType.Monster);

                    if (success)
                    {
                        Debug.Console(1, "CombineScreenUI", "Successfully combined into Monster Studio");
                        ShowStatus("Monster Studio created successfully");
                    }
                    else
                    {
                        ShowError("Failed to create Monster Studio");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "CombineScreenUI", "Error selecting monster studio: {0}", ex.Message);
                    ShowError("Error creating Monster Studio");
                }
            }
        }

        private void OnRefreshPressed()
        {
            RefreshAdjacentUnits();
            ShowStatus("Adjacent units refreshed");
        }

        private void OnBackButtonPressed()
        {
            NavigateBackRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Private Methods - Display Updates

        private void UpdateCombinationDisplay()
        {
            try
            {
                // Update current configuration display
                UpdateCurrentConfiguration();

                // Update combination options availability
                UpdateCombinationOptions();

                // Update combined units list
                UpdateCombinedUnitsList();

                // Update status information
                UpdateCombinationStatus();
            }
            catch (Exception ex)
            {
                Debug.Console(0, "CombineScreenUI", "Error updating combination display: {0}", ex.Message);
            }
        }

        private void UpdateCurrentConfiguration()
        {
            // Update screen title
            _panel.StringInput[MSUTouchPanelJoins.Combine.ScreenTitle].StringValue = "Studio Combination";

            // Update current configuration text
            string configText;
            switch (_currentCombination)
            {
                case StudioCombinationType.Single:
                    configText = "Single Studio";
                    break;
                case StudioCombinationType.Mega:
                    configText = "Mega Studio";
                    break;
                case StudioCombinationType.Monster:
                    configText = "Monster Studio";
                    break;
                default:
                    configText = "Unknown";
                    break;
            }

            _panel.StringInput[MSUTouchPanelJoins.Combine.CurrentConfiguration].StringValue = configText;

            // Update configuration description
            string description = GetConfigurationDescription(_currentCombination);
            _panel.StringInput[MSUTouchPanelJoins.Combine.ConfigurationDescription].StringValue = description;

            // Highlight current selection
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.SingleStudioSelected].BoolValue = 
                _currentCombination == StudioCombinationType.Single;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.MegaStudioSelected].BoolValue = 
                _currentCombination == StudioCombinationType.Mega;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.MonsterStudioSelected].BoolValue = 
                _currentCombination == StudioCombinationType.Monster;
        }

        private void UpdateCombinationOptions()
        {
            // Check availability for each combination type
            bool canSingle = _currentCombination != StudioCombinationType.Single || IsMaster;
            bool canMega = _combinationManager.CanCombineWithAdjacentMSUs(StudioCombinationType.Mega);
            bool canMonster = _combinationManager.CanCombineWithAdjacentMSUs(StudioCombinationType.Monster);

            // Update button availability
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.SingleStudioAvailable].BoolValue = canSingle;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.MegaStudioAvailable].BoolValue = canMega;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.MonsterStudioAvailable].BoolValue = canMonster;

            // Update option text with availability indicators
            _panel.StringInput[MSUTouchPanelJoins.Combine.SingleStudioText].StringValue = 
                canSingle ? "Single Studio" : "Single Studio (N/A)";
            _panel.StringInput[MSUTouchPanelJoins.Combine.MegaStudioText].StringValue = 
                canMega ? "Mega Studio" : "Mega Studio (N/A)";
            _panel.StringInput[MSUTouchPanelJoins.Combine.MonsterStudioText].StringValue = 
                canMonster ? "Monster Studio" : "Monster Studio (N/A)";
        }

        private void UpdateCombinedUnitsList()
        {
            try
            {
                if (IsCombined && _combinedUnits.Any())
                {
                    // Build combined units display text
                    var unitNames = _combinedUnits.Select(u => u.Name ?? u.UID).ToList();
                    string combinedText = string.Join(", ", unitNames);

                    _panel.StringInput[MSUTouchPanelJoins.Combine.CombinedUnits].StringValue = 
                        string.Format("Combined with: {0}", combinedText);

                    // Show combined units count
                    _panel.StringInput[MSUTouchPanelJoins.Combine.UnitsCount].StringValue = 
                        string.Format("{0} units combined", _combinedUnits.Count);
                }
                else
                {
                    _panel.StringInput[MSUTouchPanelJoins.Combine.CombinedUnits].StringValue = "Combined with: None";
                    _panel.StringInput[MSUTouchPanelJoins.Combine.UnitsCount].StringValue = "1 unit (standalone)";
                }

                // Update master status
                _panel.BooleanInput[MSUTouchPanelJoins.Combine.IsMasterUnit].BoolValue = IsMaster;
                _panel.StringInput[MSUTouchPanelJoins.Combine.MasterStatus].StringValue = 
                    IsMaster ? "Master Unit" : IsCombined ? "Combined Unit" : "Standalone Unit";
            }
            catch (Exception ex)
            {
                Debug.Console(0, "CombineScreenUI", "Error updating combined units list: {0}", ex.Message);
            }
        }

        private void UpdateCombinationStatus()
        {
            // Update combination control status
            bool canControl = !IsCombined || IsMaster;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.CanControlCombination].BoolValue = canControl;

            string statusText;
            if (!IsCombined)
            {
                statusText = "Not Combined - All functions available";
            }
            else if (IsMaster)
            {
                statusText = "Master Unit - Controls all combined functions";
            }
            else
            {
                statusText = "Combined Unit - Controlled by master";
            }

            _panel.StringInput[MSUTouchPanelJoins.Combine.CombinationStatus].StringValue = statusText;

            // Update control restrictions display
            if (IsCombined && !IsMaster)
            {
                _panel.StringInput[MSUTouchPanelJoins.Combine.ControlRestrictions].StringValue = 
                    "Combination controls disabled - This unit is controlled by the master unit";
                _panel.BooleanInput[MSUTouchPanelJoins.Combine.ShowRestrictions].BoolValue = true;
            }
            else
            {
                _panel.BooleanInput[MSUTouchPanelJoins.Combine.ShowRestrictions].BoolValue = false;
            }
        }

        private void CheckCombinationAvailability()
        {
            try
            {
                // This method analyzes adjacent units and determines what combinations are possible
                // Results are used by UpdateCombinationOptions()

                bool megaAvailable = _combinationManager.CanCombineWithAdjacentMSUs(StudioCombinationType.Mega);
                bool monsterAvailable = _combinationManager.CanCombineWithAdjacentMSUs(StudioCombinationType.Monster);

                core_tools.Debug.Console(2, "CombineScreenUI", "Combination availability checked - Mega: {0}, Monster: {1}", 
                    megaAvailable, monsterAvailable);
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "CombineScreenUI", "Error checking combination availability: {0}", ex.Message);
            }
        }

        private void ClearCombinationDisplay()
        {
            // Clear all text displays
            _panel.StringInput[MSUTouchPanelJoins.Combine.CurrentConfiguration].StringValue = "";
            _panel.StringInput[MSUTouchPanelJoins.Combine.ConfigurationDescription].StringValue = "";
            _panel.StringInput[MSUTouchPanelJoins.Combine.CombinedUnits].StringValue = "";
            _panel.StringInput[MSUTouchPanelJoins.Combine.UnitsCount].StringValue = "";
            _panel.StringInput[MSUTouchPanelJoins.Combine.CombinationStatus].StringValue = "";

            // Clear all button states
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.SingleStudioSelected].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.MegaStudioSelected].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.MonsterStudioSelected].BoolValue = false;
        }

        private void ShowError(string message)
        {
            _panel.StringInput[MSUTouchPanelJoins.Combine.ErrorMessage].StringValue = message;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.ErrorVisible].BoolValue = true;

            // Auto-hide error after 3 seconds
            CTimer errorTimer = new CTimer(_ =>
            {
                _panel.BooleanInput[MSUTouchPanelJoins.Combine.ErrorVisible].BoolValue = false;
            }, 3000);

            core_tools.Debug.Console(1, "CombineScreenUI", "Error displayed: {0}", message);
        }

        private void ShowStatus(string message)
        {
            _panel.StringInput[MSUTouchPanelJoins.Combine.StatusMessage].StringValue = message;
            _panel.BooleanInput[MSUTouchPanelJoins.Combine.StatusVisible].BoolValue = true;

            // Auto-hide status after 2 seconds
            CTimer statusTimer = new CTimer(_ =>
            {
                _panel.BooleanInput[MSUTouchPanelJoins.Combine.StatusVisible].BoolValue = false;
            }, 2000);

            core_tools.Debug.Console(1, "CombineScreenUI", "Status displayed: {0}", message);
        }

        private string GetConfigurationDescription(StudioCombinationType type)
        {
            switch (type)
            {
                case StudioCombinationType.Single:
                    return "Only one unit, uncombined. All functions independent.";
                case StudioCombinationType.Mega:
                    return "Two adjoining units combined. Shared temperature and music controls.";
                case StudioCombinationType.Monster:
                    return "Three adjoining units combined. Fully synchronized operations.";
                default:
                    return "Unknown configuration";
            }
        }

        #endregion

        #region Event Handlers

        private void OnCombinationChanged(object? sender, StudioCombinationChangedEventArgs e)
        {
            core_tools.Debug.Console(1, "CombineScreenUI", "Combination changed - Type: {0}, Units: {1}", 
                e.CombinationType, e.CombinedMSUs?.Count ?? 0);

            lock (_lockObject)
            {
                _currentCombination = e.CombinationType;
                _combinedUnits = e.CombinedMSUs?.ToList() ?? new List<MusicStudioUnit>();

                // Update display
                UpdateCombinationDisplay();

                // Fire external event
                CombinationChanged?.Invoke(this, e);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from events
                if (_combinationManager != null)
                {
                    _combinationManager.CombinationChanged -= OnCombinationChanged;
                }

                _disposed = true;
            }
            catch (Exception ex)
            {
                Debug.Console(0, "CombineScreenUI", "Error during disposal: {0}", ex.Message);
            }
        }

        #endregion
    }
}


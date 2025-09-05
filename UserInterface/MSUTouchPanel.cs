using System;
using System.Collections.Concurrent;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using core_tools;
using flexpod.Services;
using flexpod.Controllers;
using flexpod.Passengers;
using flexpod.Configuration;
using flexpod.MusicSystemController;

namespace flexpod.UserInterface
{
    /// <summary>
    /// MSU TouchPanel - Extends TouchPanelBase for Music Studio Unit specific screens
    /// Implements the five main functions per Client-Scope.md:
    /// - Settings, User, Music, Temperature, Combine
    /// </summary>
    public class MSUTouchPanel : TouchPanelBase, IDisposable
    {
        #region Private Fields
        private bool _disposed = false;
        private readonly MSUController _msuController;
        private readonly SystemInitializationService _initService;
        private readonly EnhancedHVACController _hvacController;
        private readonly EnhancedMusicSystemController _musicController;
        private readonly LoyaltyID _userDatabase;

        // UI Screen Handlers
        private SettingsScreenUI _settingsScreen;
        private UserLoginScreenUI _userLoginScreen;
        private TemperatureScreenUI _temperatureScreen;
        private MusicBrowseUI _musicBrowseScreen;

        // State tracking
        private MSUTouchPanelJoins.Pages _currentPage = MSUTouchPanelJoins.Pages.Settings;
        private bool _isUserLoggedIn = false;
        private string _currentUserName = string.Empty;
        private bool _isMusicPlaying = false;
        private string _currentTrackInfo = string.Empty;

        #endregion

        #region TouchPanelBase Implementation
        internal override uint SubPage { get; set; }
        internal override uint MaxNumItems { get; set; } = 50;
        internal override ConcurrentDictionary<uint, bool> PageDictionary { get; set; }
        internal override ConcurrentDictionary<uint, bool> PopupPageDictionary { get; set; }
        internal override ConcurrentDictionary<uint, bool> PanelWidePopupPageDictionary { get; set; }
        #endregion

        #region Properties
        public MSUTouchPanelJoins.Pages CurrentPage => _currentPage;
        public bool IsUserLoggedIn => _isUserLoggedIn;
        public string CurrentUserName => _currentUserName;
        public bool IsMusicPlaying => _isMusicPlaying;
        #endregion

        #region Constructor
        public MSUTouchPanel(string keyId, string friendlyId, BasicTriListWithSmartObject panel,
                           MSUController msuController, SystemInitializationService initService,
                           EnhancedHVACController hvacController, EnhancedMusicSystemController musicController,
                           LoyaltyID userDatabase) 
            : base(keyId, friendlyId, panel)
        {
            _msuController = msuController ?? throw new ArgumentNullException(nameof(msuController));
            _initService = initService ?? throw new ArgumentNullException(nameof(initService));
            _hvacController = hvacController ?? throw new ArgumentNullException(nameof(hvacController));
            _musicController = musicController ?? throw new ArgumentNullException(nameof(musicController));
            _userDatabase = userDatabase ?? throw new ArgumentNullException(nameof(userDatabase));

            Debug.Console(1, this, "Initializing MSU TouchPanel");

            // Initialize dictionaries
            PageDictionary = new ConcurrentDictionary<uint, bool>();
            PopupPageDictionary = new ConcurrentDictionary<uint, bool>();
            PanelWidePopupPageDictionary = new ConcurrentDictionary<uint, bool>();

            // Setup page tracking
            InitializePageTracking();

            // Setup menu bar events
            SetupMenuBarEvents();

            // Initialize screen handlers
            InitializeScreenHandlers();

            // Set initial page to Settings per Client-Scope.md requirement
            NavigateToPage(MSUTouchPanelJoins.Pages.Settings);

            // Initialize menu bar display
            UpdateMenuBar();

            Debug.Console(1, this, "MSU TouchPanel initialized successfully");
        }
        #endregion

        #region Initialization
        private void InitializePageTracking()
        {
            // Add all pages to tracking dictionary
            foreach (MSUTouchPanelJoins.Pages page in Enum.GetValues(typeof(MSUTouchPanelJoins.Pages)))
            {
                PageDictionary[(uint)page] = false;
            }
        }

        private void SetupMenuBarEvents()
        {
            Panel.SigChange += (device, args) =>
            {
                if (!args.Sig.BoolValue) return; // Only handle button press

                switch (args.Sig.Number)
                {
                    case (uint)MSUTouchPanelJoins.MenuBar.SettingsButton:
                        NavigateToPage(MSUTouchPanelJoins.Pages.Settings);
                        break;
                    case (uint)MSUTouchPanelJoins.MenuBar.UserButton:
                        NavigateToPage(MSUTouchPanelJoins.Pages.User);
                        break;
                    case (uint)MSUTouchPanelJoins.MenuBar.MusicButton:
                        NavigateToPage(MSUTouchPanelJoins.Pages.Music);
                        break;
                    case (uint)MSUTouchPanelJoins.MenuBar.TemperatureButton:
                        NavigateToPage(MSUTouchPanelJoins.Pages.Temperature);
                        break;
                    case (uint)MSUTouchPanelJoins.MenuBar.CombineButton:
                        NavigateToPage(MSUTouchPanelJoins.Pages.Combine);
                        break;
                }
            };

            Debug.Console(2, this, "Menu bar events configured");
        }

        private void InitializeScreenHandlers()
        {
            try
            {
                // Settings Screen
                _settingsScreen = new SettingsScreenUI(Panel, _msuController, _initService);
                _settingsScreen.ConfigurationReloadRequested += OnConfigurationReloadRequested;

                // User Login Screen
                _userLoginScreen = new UserLoginScreenUI(Panel, _userDatabase);
                _userLoginScreen.UserLoggedIn += OnUserLoggedIn;
                _userLoginScreen.UserLoggedOut += OnUserLoggedOut;
                _userLoginScreen.GuestModeActivated += OnGuestModeActivated;

                // Temperature Screen
                _temperatureScreen = new TemperatureScreenUI(Panel, _hvacController, _msuController);
                _temperatureScreen.TemperatureChanged += OnTemperatureChanged;
                _temperatureScreen.TemperatureFault += OnTemperatureFault;

                // Music Browse Screen (using existing implementation)
                _musicBrowseScreen = new MusicBrowseUI(_musicController, Panel);
                _musicController.TrackStarted += OnMusicTrackStarted;
                _musicController.TrackStopped += OnMusicTrackStopped;
                _musicController.TimeUpdate += OnMusicTimeUpdate;

                Debug.Console(1, this, "Screen handlers initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error initializing screen handlers: {0}", ex.Message);
            }
        }
        #endregion

        #region Navigation
        public void NavigateToPage(MSUTouchPanelJoins.Pages page)
        {
            try
            {
                Debug.Console(1, this, "Navigating to page: {0}", page);

                // Clear current page
                if (PageDictionary.ContainsKey((uint)_currentPage))
                {
                    PageDictionary[(uint)_currentPage] = false;
                }

                // Set new page
                _currentPage = page;
                SetPage((uint)page);
                PageDictionary[(uint)page] = true;

                // Handle page-specific logic
                OnPageChanged(page);

                Debug.Console(1, this, "Navigation completed to page: {0}", page);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error navigating to page {0}: {1}", page, ex.Message);
            }
        }

        private void OnPageChanged(MSUTouchPanelJoins.Pages page)
        {
            switch (page)
            {
                case MSUTouchPanelJoins.Pages.Settings:
                    _settingsScreen?.UpdateSettingsDisplay();
                    break;
                case MSUTouchPanelJoins.Pages.User:
                    // User screen is always up to date
                    break;
                case MSUTouchPanelJoins.Pages.Temperature:
                    // Temperature screen updates automatically via HVAC events
                    break;
                case MSUTouchPanelJoins.Pages.Music:
                    // Only allow browsing when playback is stopped per Client-Scope.md
                    if (_isMusicPlaying)
                    {
                        _musicBrowseScreen?.ShowNowPlayingView();
                    }
                    else
                    {
                        _musicBrowseScreen?.ShowArtistListView();
                    }
                    break;
                case MSUTouchPanelJoins.Pages.Combine:
                    // TODO: Implement combination screen
                    break;
            }
        }
        #endregion

        #region Menu Bar Updates
        private void UpdateMenuBar()
        {
            try
            {
                // Update building location from configuration
                var config = _msuController?.GetCurrentConfiguration();
                string cityName = config?.LocalConfig?.Address?.City ?? "Unknown Location";
                Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.BuildingLocationText].StringValue = cityName;

                // Update music info if playing
                UpdateMusicInfo();

                Debug.Console(2, this, "Menu bar updated - City: {0}", cityName);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error updating menu bar: {0}", ex.Message);
            }
        }

        private void UpdateMusicInfo()
        {
            if (_isMusicPlaying && !string.IsNullOrEmpty(_currentTrackInfo))
            {
                // Parse track info and display on menu bar
                var trackParts = _currentTrackInfo.Split('|');
                if (trackParts.Length >= 2)
                {
                    Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.NowPlayingArtistText].StringValue = trackParts[0];
                    Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.NowPlayingTrackText].StringValue = trackParts[1];
                }
            }
            else
            {
                // Clear music info when not playing
                Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.NowPlayingArtistText].StringValue = string.Empty;
                Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.NowPlayingTrackText].StringValue = string.Empty;
                Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.NowPlayingTimeText].StringValue = string.Empty;
            }
        }
        #endregion

        #region Event Handlers

        private void OnConfigurationReloadRequested(object sender, ConfigurationReloadEventArgs args)
        {
            Debug.Console(1, this, "Configuration reload requested: {0}", args.Reason);
            // The initialization service will handle the actual reload
        }

        private void OnUserLoggedIn(object sender, UserLoginEventArgs args)
        {
            _isUserLoggedIn = true;
            _currentUserName = args.UserName;
            Debug.Console(1, this, "User logged in: {0} (ID: {1})", args.UserName, args.UserId);
        }

        private void OnUserLoggedOut(object sender, UserLogoutEventArgs args)
        {
            _isUserLoggedIn = false;
            _currentUserName = string.Empty;
            Debug.Console(1, this, "User logged out at: {0}", args.LogoutTime);
        }

        private void OnGuestModeActivated(object sender, EventArgs args)
        {
            Debug.Console(1, this, "Guest mode activated");
        }

        private void OnTemperatureChanged(object sender, TemperatureChangedEventArgs args)
        {
            Debug.Console(1, this, "Temperature changed to {0:F1}°C for zones: {1}", 
                args.Temperature, string.Join(",", args.ZoneIds));
        }

        private void OnTemperatureFault(object sender, TemperatureFaultEventArgs args)
        {
            Debug.Console(0, this, "Temperature fault: {0}", args.FaultMessage);
        }

        private void OnMusicTrackStarted(object sender, TrackStartedEventArgs args)
        {
            _isMusicPlaying = true;
            _currentTrackInfo = $"{args.ArtistName}|{args.TrackName}";
            UpdateMusicInfo();
            Debug.Console(1, this, "Music track started: {0} - {1}", args.ArtistName, args.TrackName);
        }

        private void OnMusicTrackStopped(object sender, TrackStoppedEventArgs args)
        {
            _isMusicPlaying = false;
            _currentTrackInfo = string.Empty;
            UpdateMusicInfo();
            Debug.Console(1, this, "Music track stopped");
        }

        private void OnMusicTimeUpdate(object sender, TimeUpdateEventArgs args)
        {
            if (_isMusicPlaying)
            {
                // Display remaining time as static value per Client-Scope.md
                string timeText = $"{args.RemainingMinutes}:{args.RemainingSeconds:D2}";
                Panel.StringInput[(uint)MSUTouchPanelJoins.MenuBar.NowPlayingTimeText].StringValue = timeText;
            }
        }

        #endregion

        #region Connection Status
        public void ShowConnectionMessage(bool isConnected)
        {
            if (isConnected)
            {
                Panel.BooleanInput[(uint)MSUTouchPanelJoins.ConnectionStatus.ProcessorOnline].BoolValue = true;
                Panel.StringInput[(uint)MSUTouchPanelJoins.ConnectionStatus.ConnectionMessage].StringValue = string.Empty;
            }
            else
            {
                Panel.BooleanInput[(uint)MSUTouchPanelJoins.ConnectionStatus.ProcessorOnline].BoolValue = false;
                Panel.StringInput[(uint)MSUTouchPanelJoins.ConnectionStatus.ConnectionMessage].StringValue = 
                    "Connecting to system - please wait";
            }
        }
        #endregion

        #region Console Commands
        public void SetMSUPage(string command)
        {
            try
            {
                var parts = command.Split(' ');
                if (parts.Length > 0 && Enum.TryParse(parts[0], true, out MSUTouchPanelJoins.Pages page))
                {
                    NavigateToPage(page);
                }
                else
                {
                    Debug.Console(0, this, "Invalid page name: {0}", command);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting MSU page: {0}", ex.Message);
            }
        }

        public void ShowMSUStatus()
        {
            Debug.Console(0, this, "=== MSU TouchPanel Status ===");
            Debug.Console(0, this, "Current Page: {0}", _currentPage);
            Debug.Console(0, this, "User Logged In: {0}", _isUserLoggedIn);
            Debug.Console(0, this, "Current User: {0}", _currentUserName);
            Debug.Console(0, this, "Music Playing: {0}", _isMusicPlaying);
            Debug.Console(0, this, "Track Info: {0}", _currentTrackInfo);
            Debug.Console(0, this, "HVAC Connected: {0}", _hvacController?.IsConnected);
            Debug.Console(0, this, "Music Connected: {0}", _musicController?.IsConnected);
        }
        #endregion

        #region Dispose
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                Debug.Console(1, this, "Disposing MSU TouchPanel");

                // Dispose screen handlers
                _settingsScreen?.Dispose();
                _userLoginScreen?.Dispose();
                _temperatureScreen?.Dispose();
                _musicBrowseScreen?.Dispose();

                // Unsubscribe from events
                if (_musicController != null)
                {
                    _musicController.TrackStarted -= OnMusicTrackStarted;
                    _musicController.TrackStopped -= OnMusicTrackStopped;
                    _musicController.TimeUpdate -= OnMusicTimeUpdate;
                }

                Debug.Console(1, this, "MSU TouchPanel disposed successfully");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error disposing MSU TouchPanel: {0}", ex.Message);
            }
            finally
            {
                _disposed = true;
            }
        }
        #endregion
    }
}

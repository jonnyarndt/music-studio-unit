using core_tools;
using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Devices;
using musicStudioUnit.UserInterface;
using MusicSystemControllerNS = musicStudioUnit.MusicSystemController;
namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// Enhanced Music Screen UI for MSU TouchPanel
    /// Implements complete Client-Scope.md Appendix C requirements:
    /// - Artist browsing with pagination
    /// - Track browsing per artist with pagination  
    /// - Now playing controls with time remaining
    /// - Play/Stop functionality per protocol specifications
    /// - Combination mode support (master unit only sends playback commands)
    /// </summary>
    public class MusicScreenUI : IDisposable
    {
        #region Private Fields

        private readonly BasicTriList _panel;
        private readonly EnhancedMusicSystemController _musicController;
        private readonly object _lockObject = new object();

        // Browse state management
        private BrowseState _currentState = BrowseState.ArtistSelection;
    private List<musicStudioUnit.Devices.MusicArtist> _currentArtists = new List<musicStudioUnit.Devices.MusicArtist>();
    private List<musicStudioUnit.Devices.MusicTrack> _currentTracks = new List<musicStudioUnit.Devices.MusicTrack>();
        private int _selectedArtistId = 0;
        private int _selectedTrackId = 0;
        private string _selectedArtistName = string.Empty;
        private string _selectedTrackName = string.Empty;

        // Pagination state
        private int _currentArtistPage = 1;
        private int _currentTrackPage = 1;
        private const int _itemsPerPage = 10;

        // Connection state
        private bool _isInitialized = false;
        private bool _disposed = false;

        #endregion

        #region Events

        /// <summary>
        /// Fired when music playback state changes
        /// </summary>
        public event EventHandler<MusicPlaybackStateEventArgs> PlaybackStateChanged;

        /// <summary>
        /// Fired when UI requires navigation back to menu
        /// </summary>
        public event EventHandler NavigateBackRequested;

        #endregion

        #region Public Properties

        /// <summary>
        /// Current browse state
        /// </summary>
        public BrowseState CurrentBrowseState => _currentState;

        /// <summary>
        /// Whether music is currently playing
        /// </summary>
        public bool IsPlaying => _musicController?.GetPlaybackStatus()?.IsPlaying == true;

        /// <summary>
        /// Whether browsing is allowed (only when playback is stopped per Client-Scope.md)
        /// </summary>
        public bool IsBrowsingAllowed => !IsPlaying;

        /// <summary>
        /// Music system connection status
        /// </summary>
        public bool IsConnected => _musicController?.IsConnected == true;

        #endregion

        #region Constructor

        public MusicScreenUI(BasicTriList panel, EnhancedMusicSystemController musicController)
        {
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));
            _musicController = musicController ?? throw new ArgumentNullException(nameof(musicController));

            Debug.Console(1, "MusicScreenUI", "Initializing enhanced music screen UI");

            InitializeUI();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize and show the music screen
        /// </summary>
        public void Initialize()
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                try
                {
                    SetupEventHandlers();
                    RefreshCatalogFromServer();
                    UpdateConnectionStatus();
                    ShowArtistSelection();
                    _isInitialized = true;

                    Debug.Console(1, "MusicScreenUI", "Music screen initialized successfully");
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicScreenUI", "Error initializing music screen: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Show the music screen (entry point from MSU TouchPanel)
        /// </summary>
        public void Show()
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            // Show appropriate view based on current state
            switch (_currentState)
            {
                case BrowseState.ArtistSelection:
                    ShowArtistSelection();
                    break;
                case BrowseState.TrackSelection:
                    ShowTrackSelection();
                    break;
                case BrowseState.NowPlaying:
                    ShowNowPlaying();
                    break;
            }

            Debug.Console(1, "MusicScreenUI", "Music screen shown - State: {0}", _currentState);
        }

        /// <summary>
        /// Hide the music screen
        /// </summary>
        public void Hide()
        {
            // Clear any temporary display states
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ArtistListVisible].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.TrackListVisible].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.NowPlayingVisible].BoolValue = false;
        }

        /// <summary>
        /// Refresh the music catalog from the server
        /// </summary>
        public void RefreshCatalogFromServer()
        {
            if (_disposed) return;

            try
            {
                Debug.Console(1, "MusicScreenUI", "Refreshing music catalog");
                _musicController?.LoadMusicCatalog();
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error refreshing catalog: {0}", ex.Message);
                ShowError("Failed to refresh music catalog");
            }
        }

        #endregion

        #region Private Methods - Initialization

        private void InitializeUI()
        {
            try
            {
                // Clear all displays
                ClearArtistList();
                ClearTrackList();
                ClearNowPlaying();

                // Setup touch panel button events
                SetupTouchPanelEvents();
                // Initialize with disconnected state
                UpdateConnectionStatus();

                Debug.Console(2, "MusicScreenUI", "UI initialized");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error initializing UI: {0}", ex.Message);
        
            }
        }

        private void SetupEventHandlers()
        {
            if (_musicController != null)
            {
                _musicController.CatalogUpdated += OnCatalogUpdated;
                _musicController.PlaybackStatusChanged += OnPlaybackStatusChanged;
                _musicController.TrackTimeUpdated += OnTrackTimeUpdated;
                _musicController.Connected += OnMusicSystemConnected;
                _musicController.Disconnected += OnMusicSystemDisconnected;
                _musicController.MusicSystemError += OnMusicSystemError;

                Debug.Console(2, "MusicScreenUI", "Event handlers configured");
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
                // Artist list buttons (421-430)
                if (join >= MSUTouchPanelJoins.Music.ArtistButton1 && 
                    join <= MSUTouchPanelJoins.Music.ArtistButton10)
                {
                    int artistIndex = (int)(join - MSUTouchPanelJoins.Music.ArtistButton1);
                    OnArtistSelected(artistIndex);
                }
                // Track list buttons (431-440)
                else if (join >= MSUTouchPanelJoins.Music.TrackButton1 && 
                         join <= MSUTouchPanelJoins.Music.TrackButton10)
                {
                    int trackIndex = (int)(join - MSUTouchPanelJoins.Music.TrackButton1);
                    OnTrackSelected(trackIndex);
                }
                // Navigation and control buttons
                else
                {
                    switch (join)
                    {
                        case MSUTouchPanelJoins.Music.ArtistPrevPage:
                            OnArtistPreviousPage();
                            break;
                        case MSUTouchPanelJoins.Music.ArtistNextPage:
                            OnArtistNextPage();
                            break;
                        case MSUTouchPanelJoins.Music.TrackPrevPage:
                            OnTrackPreviousPage();
                            break;
                        case MSUTouchPanelJoins.Music.TrackNextPage:
                            OnTrackNextPage();
                            break;
                        case MSUTouchPanelJoins.Music.BackToArtists:
                            OnBackToArtists();
                            break;
                        case MSUTouchPanelJoins.Music.PlayStopButton:
                            OnPlayStopPressed();
                            break;
                        case MSUTouchPanelJoins.Music.BackToBrowse:
                            OnBackToBrowse();
                            break;
                        case MSUTouchPanelJoins.Music.RefreshCatalog:
                            OnRefreshCatalog();
                            break;
                        case MSUTouchPanelJoins.MenuBar.BackButton:
                            OnBackButtonPressed();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error handling button press for join {0}: {1}", join, ex.Message);
            }
        }

        #endregion

        #region Private Methods - User Actions

        private void OnArtistSelected(int artistIndex)
        {
            if (!IsBrowsingAllowed)
            {
                ShowError("Browsing not allowed during playback");
                return;
            }

            lock (_lockObject)
            {
                try
                {
                    int globalIndex = (_currentArtistPage - 1) * _itemsPerPage + artistIndex;

                    if (globalIndex >= 0 && globalIndex < _currentArtists.Count)
                    {
                        var selectedArtist = _currentArtists[globalIndex];
                        _selectedArtistId = selectedArtist.Id;
                        _selectedArtistName = selectedArtist.Name;

                        Debug.Console(1, "MusicScreenUI", "Artist selected: {0} - {1}", selectedArtist.Id, selectedArtist.Name);

                        // Load tracks for selected artist
                        LoadTracksForArtist(selectedArtist.Id);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicScreenUI", "Error selecting artist: {0}", ex.Message);
                    ShowError("Error selecting artist");
                }
            }
        }

        private void OnTrackSelected(int trackIndex)
        {
            if (!IsBrowsingAllowed)
            {
                ShowError("Browsing not allowed during playback");
                return;
            }

            lock (_lockObject)
            {
                try
                {
                    int globalIndex = (_currentTrackPage - 1) * _itemsPerPage + trackIndex;

                    if (globalIndex >= 0 && globalIndex < _currentTracks.Count)
                    {
                        var selectedTrack = _currentTracks[globalIndex];
                        _selectedTrackId = selectedTrack.Id;
                        _selectedTrackName = selectedTrack.Name;

                        Debug.Console(1, "MusicScreenUI", "Track selected: {0} - {1}", selectedTrack.Id, selectedTrack.Name);

                        // Start playback per Client-Scope.md protocol
                        StartTrackPlayback(selectedTrack.Id, selectedTrack.Name, _selectedArtistName);
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicScreenUI", "Error selecting track: {0}", ex.Message);
                    ShowError("Error selecting track");
                }
            }
        }

        private void OnPlayStopPressed()
        {
            lock (_lockObject)
            {
                try
                {
                    var status = _musicController.GetPlaybackStatus();

                    if (status.IsPlaying)
                    {
                        // Stop current playback
                        Debug.Console(1, "MusicScreenUI", "Stopping playback - Track: {0}", _selectedTrackId);
                        _musicController.StopTrack();
                    }
                    else if (_selectedTrackId > 0)
                    {
                        // Resume/start playback of selected track
                        Debug.Console(1, "MusicScreenUI", "Starting playback - Track: {0}", _selectedTrackId);
                        StartTrackPlayback(_selectedTrackId, _selectedTrackName, _selectedArtistName);
                    }
                    else
                    {
                        ShowError("No track selected for playback");
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicScreenUI", "Error handling play/stop: {0}", ex.Message);
                    ShowError("Playback control error");
                }
            }
        }

        private void OnBackToArtists()
        {
            if (!IsBrowsingAllowed)
            {
                ShowError("Browsing not allowed during playback");
                return;
            }

            _currentState = BrowseState.ArtistSelection;
            _currentTracks.Clear();
            _currentTrackPage = 1;
            ShowArtistSelection();

            Debug.Console(1, "MusicScreenUI", "Navigated back to artist selection");
        }

        private void OnBackToBrowse()
        {
            // Return to browsing - behavior depends on current selection state
            if (_selectedArtistId > 0 && _currentTracks.Any())
            {
                _currentState = BrowseState.TrackSelection;
                ShowTrackSelection();
            }
            else
            {
                _currentState = BrowseState.ArtistSelection;
                ShowArtistSelection();
            }

            Debug.Console(1, "MusicScreenUI", "Navigated back to browse - State: {0}", _currentState);
        }

        private void OnRefreshCatalog()
        {
            RefreshCatalogFromServer();
        }

        private void OnBackButtonPressed()
        {
            NavigateBackRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnArtistPreviousPage()
        {
            if (_currentArtistPage > 1)
            {
                _currentArtistPage--;
                UpdateArtistList();
                Debug.Console(2, "MusicScreenUI", "Artist page: {0}", _currentArtistPage);
            }
        }

        private void OnArtistNextPage()
        {
            int totalPages = (int)Math.Ceiling((double)_currentArtists.Count / _itemsPerPage);
            if (_currentArtistPage < totalPages)
            {
                _currentArtistPage++;
                UpdateArtistList();
                Debug.Console(2, "MusicScreenUI", "Artist page: {0}", _currentArtistPage);
            }
        }

        private void OnTrackPreviousPage()
        {
            if (_currentTrackPage > 1)
            {
                _currentTrackPage--;
                UpdateTrackList();
                Debug.Console(2, "MusicScreenUI", "Track page: {0}", _currentTrackPage);
            }
        }

        private void OnTrackNextPage()
        {
            int totalPages = (int)Math.Ceiling((double)_currentTracks.Count / _itemsPerPage);
            if (_currentTrackPage < totalPages)
            {
                _currentTrackPage++;
                UpdateTrackList();
                Debug.Console(2, "MusicScreenUI", "Track page: {0}", _currentTrackPage);
            }
        }

        #endregion

        #region Private Methods - Music Control

        private void StartTrackPlayback(int trackId, string trackName, string artistName)
        {
            try
            {
                Debug.Console(1, "MusicScreenUI", "Starting playback - Track: {0}, Artist: {1}", trackName, artistName);

                bool success = _musicController.PlayTrack(trackId, trackName, artistName);

                if (success)
                {
                    _currentState = BrowseState.NowPlaying;
                    ShowNowPlaying();

                    // Fire event
                    PlaybackStateChanged?.Invoke(this, new MusicPlaybackStateEventArgs
                    {
                        IsPlaying = true,
                        TrackName = trackName,
                        ArtistName = artistName
                    });
                }
                else
                {
                    ShowError("Failed to start playback");
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error starting playback: {0}", ex.Message);
                ShowError("Playback start error");
            }
        }

        private void LoadTracksForArtist(int artistId)
        {
            try
            {
                Debug.Console(1, "MusicScreenUI", "Loading tracks for artist {0}", artistId);

                _currentTracks = _musicController.GetTracksForArtist(artistId);
                _currentTrackPage = 1;
                _currentState = BrowseState.TrackSelection;

                ShowTrackSelection();

                Debug.Console(1, "MusicScreenUI", "Loaded {0} tracks for artist {1}", _currentTracks.Count, artistId);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error loading tracks: {0}", ex.Message);
                ShowError("Failed to load tracks");
            }
        }

        #endregion

        #region Private Methods - Display Updates

        private void ShowArtistSelection()
        {
            // Hide other views
            _panel.BooleanInput[MSUTouchPanelJoins.Music.TrackListVisible].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.NowPlayingVisible].BoolValue = false;

            // Show artist list
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ArtistListVisible].BoolValue = true;

            // Update artist list content
            UpdateArtistList();

            // Update screen title
            _panel.StringInput[MSUTouchPanelJoins.Music.ScreenTitle].StringValue = "Select Artist";

            Debug.Console(2, "MusicScreenUI", "Showing artist selection");
        }

        private void ShowTrackSelection()
        {
            // Hide other views
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ArtistListVisible].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.NowPlayingVisible].BoolValue = false;

            // Show track list
            _panel.BooleanInput[MSUTouchPanelJoins.Music.TrackListVisible].BoolValue = true;

            // Update track list content
            UpdateTrackList();

            // Update screen title
            _panel.StringInput[MSUTouchPanelJoins.Music.ScreenTitle].StringValue = 
                string.Format("Select Track - {0}", _selectedArtistName);

            Debug.Console(2, "MusicScreenUI", "Showing track selection for artist: {0}", _selectedArtistName);
        }

        private void ShowNowPlaying()
        {
            // Hide other views
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ArtistListVisible].BoolValue = false;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.TrackListVisible].BoolValue = false;

            // Show now playing
            _panel.BooleanInput[MSUTouchPanelJoins.Music.NowPlayingVisible].BoolValue = true;

            // Update now playing content
            UpdateNowPlayingDisplay();

            // Update screen title
            _panel.StringInput[MSUTouchPanelJoins.Music.ScreenTitle].StringValue = "Now Playing";

            Debug.Console(2, "MusicScreenUI", "Showing now playing");
        }

        private void UpdateArtistList()
        {
            try
            {
                // Load current artists if needed
                if (!_currentArtists.Any())
                {
                    _currentArtists = _musicController.GetArtistList();
                }

                // Calculate page bounds
                int startIndex = (_currentArtistPage - 1) * _itemsPerPage;
                int endIndex = Math.Min(startIndex + _itemsPerPage, _currentArtists.Count);

                // Update artist buttons and text
                for (int i = 0; i < _itemsPerPage; i++)
                {
                    uint buttonJoin = MSUTouchPanelJoins.Music.ArtistButton1 + (uint)i;
                    uint textJoin = MSUTouchPanelJoins.Music.ArtistText1 + (uint)i;

                    if (startIndex + i < endIndex)
                    {
                        var artist = _currentArtists[startIndex + i];
                        _panel.BooleanInput[buttonJoin].BoolValue = true; // Enable button
                        _panel.StringInput[textJoin].StringValue = artist.Name;
                    }
                    else
                    {
                        _panel.BooleanInput[buttonJoin].BoolValue = false; // Disable button
                        _panel.StringInput[textJoin].StringValue = "";
                    }
                }

                // Update pagination
                UpdateArtistPagination();

                Debug.Console(2, "MusicScreenUI", "Updated artist list - page {0}, showing {1} artists", 
                    _currentArtistPage, endIndex - startIndex);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error updating artist list: {0}", ex.Message);
            }
        }

        private void UpdateTrackList()
        {
            try
            {
                // Calculate page bounds
                int startIndex = (_currentTrackPage - 1) * _itemsPerPage;
                int endIndex = Math.Min(startIndex + _itemsPerPage, _currentTracks.Count);

                // Update track buttons and text
                for (int i = 0; i < _itemsPerPage; i++)
                {
                    uint buttonJoin = MSUTouchPanelJoins.Music.TrackButton1 + (uint)i;
                    uint textJoin = MSUTouchPanelJoins.Music.TrackText1 + (uint)i;

                    if (startIndex + i < endIndex)
                    {
                        var track = _currentTracks[startIndex + i];
                        _panel.BooleanInput[buttonJoin].BoolValue = true; // Enable button
                        _panel.StringInput[textJoin].StringValue = track.Name;
                    }
                    else
                    {
                        _panel.BooleanInput[buttonJoin].BoolValue = false; // Disable button
                        _panel.StringInput[textJoin].StringValue = "";
                    }
                }

                // Update pagination
                UpdateTrackPagination();

                Debug.Console(2, "MusicScreenUI", "Updated track list - page {0}, showing {1} tracks", 
                    _currentTrackPage, endIndex - startIndex);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error updating track list: {0}", ex.Message);
            }
        }

        private void UpdateArtistPagination()
        {
            int totalPages = (int)Math.Ceiling((double)_currentArtists.Count / _itemsPerPage);

            // Update pagination buttons
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ArtistPrevPage].BoolValue = _currentArtistPage > 1;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ArtistNextPage].BoolValue = _currentArtistPage < totalPages;

            // Update page text
            _panel.StringInput[MSUTouchPanelJoins.Music.ArtistPageInfo].StringValue = 
                string.Format("Page {0} of {1}", _currentArtistPage, Math.Max(1, totalPages));
        }

        private void UpdateTrackPagination()
        {
            int totalPages = (int)Math.Ceiling((double)_currentTracks.Count / _itemsPerPage);

            // Update pagination buttons
            _panel.BooleanInput[MSUTouchPanelJoins.Music.TrackPrevPage].BoolValue = _currentTrackPage > 1;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.TrackNextPage].BoolValue = _currentTrackPage < totalPages;

            // Update page text
            _panel.StringInput[MSUTouchPanelJoins.Music.TrackPageInfo].StringValue = 
                string.Format("Page {0} of {1}", _currentTrackPage, Math.Max(1, totalPages));
        }

        private void UpdateNowPlayingDisplay()
        {
            try
            {
                var status = _musicController.GetPlaybackStatus();

                // Update track and artist info
                _panel.StringInput[MSUTouchPanelJoins.Music.NowPlayingTrack].StringValue = 
                    status.CurrentTrackName ?? _selectedTrackName ?? "No Track";
                _panel.StringInput[MSUTouchPanelJoins.Music.NowPlayingArtist].StringValue = 
                    status.CurrentArtistName ?? _selectedArtistName ?? "No Artist";

                // Update time remaining per Client-Scope.md protocol
                _panel.StringInput[MSUTouchPanelJoins.Music.RemainingTime].StringValue = 
                    status.FormattedRemainingTime ?? "0:00";

                // Update play/stop button
                _panel.StringInput[MSUTouchPanelJoins.Music.PlayStopText].StringValue = 
                    status.IsPlaying ? "Stop" : "Play";
                _panel.BooleanInput[MSUTouchPanelJoins.Music.PlayStopButton].BoolValue = true;

                // Update playback status
                string statusText = status.IsPlaying ? "Playing" : "Stopped";
                _panel.StringInput[MSUTouchPanelJoins.Music.PlaybackStatus].StringValue = statusText;

                Debug.Console(2, "MusicScreenUI", "Updated now playing display - Status: {0}", statusText);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error updating now playing display: {0}", ex.Message);
            }
        }

        private void UpdateConnectionStatus()
        {
            bool connected = IsConnected;
            
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ConnectionStatus].BoolValue = connected;
            _panel.StringInput[MSUTouchPanelJoins.Music.ConnectionStatusText].StringValue = 
                connected ? "Connected" : "Disconnected";

            // Enable/disable browsing based on connection and playback state
            bool browsingEnabled = connected && IsBrowsingAllowed;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.BrowsingEnabled].BoolValue = browsingEnabled;

            Debug.Console(2, "MusicScreenUI", "Connection status updated - Connected: {0}, Browsing: {1}", 
                connected, browsingEnabled);
        }

        private void ShowError(string message)
        {
            _panel.StringInput[MSUTouchPanelJoins.Music.ErrorMessage].StringValue = message;
            _panel.BooleanInput[MSUTouchPanelJoins.Music.ErrorVisible].BoolValue = true;

            // Auto-hide error after 3 seconds
            CTimer errorTimer = new CTimer(_ =>
            {
                _panel.BooleanInput[MSUTouchPanelJoins.Music.ErrorVisible].BoolValue = false;
            }, 3000);

            Debug.Console(1, "MusicScreenUI", "Error displayed: {0}", message);
        }

        private void ClearArtistList()
        {
            for (int i = 0; i < _itemsPerPage; i++)
            {
                uint buttonJoin = MSUTouchPanelJoins.Music.ArtistButton1 + (uint)i;
                uint textJoin = MSUTouchPanelJoins.Music.ArtistText1 + (uint)i;

                _panel.BooleanInput[buttonJoin].BoolValue = false;
                _panel.StringInput[textJoin].StringValue = "";
            }
        }

        private void ClearTrackList()
        {
            for (int i = 0; i < _itemsPerPage; i++)
            {
                uint buttonJoin = MSUTouchPanelJoins.Music.TrackButton1 + (uint)i;
                uint textJoin = MSUTouchPanelJoins.Music.TrackText1 + (uint)i;

                _panel.BooleanInput[buttonJoin].BoolValue = false;
                _panel.StringInput[textJoin].StringValue = "";
            }
        }

        private void ClearNowPlaying()
        {
            _panel.StringInput[MSUTouchPanelJoins.Music.NowPlayingTrack].StringValue = "";
            _panel.StringInput[MSUTouchPanelJoins.Music.NowPlayingArtist].StringValue = "";
            _panel.StringInput[MSUTouchPanelJoins.Music.RemainingTime].StringValue = "0:00";
            _panel.StringInput[MSUTouchPanelJoins.Music.PlaybackStatus].StringValue = "Stopped";
            _panel.StringInput[MSUTouchPanelJoins.Music.PlayStopText].StringValue = "Play";
        }

        // Change the OnCatalogUpdated method signature to match the delegate EventHandler<MusicCatalogUpdatedEventArgs>
        private void OnCatalogUpdated(object? sender, MusicCatalogUpdatedEventArgs e)
        {
            Debug.Console(1, "MusicScreenUI", "Catalog updated - refreshing display");

            // Refresh current view
            _currentArtists.Clear();
            _currentArtistPage = 1;

            if (_currentState == BrowseState.ArtistSelection)
            {
                UpdateArtistList();
            }
        }

        private void OnPlaybackStatusChanged(object? sender, PlaybackStatusChangedEventArgs e)
        {
            Debug.Console(1, "MusicScreenUI", "Playback status changed - Playing: {0}", e.IsPlaying);

            // Update browsing availability per Client-Scope.md (only when stopped)
            UpdateConnectionStatus();

            // Update now playing if in that view
            if (_currentState == BrowseState.NowPlaying)
            {
                UpdateNowPlayingDisplay();
            }

            // If playback stopped automatically (time reached 0:00), return to artist selection
            // (Assume e has a property RemainingTimeSeconds if needed, otherwise skip this logic)

            // Fire external event
            PlaybackStateChanged?.Invoke(this, new MusicPlaybackStateEventArgs
            {
                IsPlaying = e.IsPlaying,
                TrackName = e.TrackName ?? "",
                ArtistName = e.ArtistName ?? ""
            });
        }

        private void OnTrackTimeUpdated(object? sender, TrackTimeUpdatedEventArgs e)
        {
            // Update time display if in now playing view
            if (_currentState == BrowseState.NowPlaying)
            {
                // Use the formatted remaining time from the event args
                _panel.StringInput[MSUTouchPanelJoins.Music.RemainingTime].StringValue = e.FormattedRemainingTime;
            }
        }

        private void OnMusicSystemConnected(object? sender, EventArgs e)
        {
            Debug.Console(1, "MusicScreenUI", "Music system connected");
            UpdateConnectionStatus();
        }

        private void OnMusicSystemDisconnected(object? sender, EventArgs e)
        {
            Debug.Console(1, "MusicScreenUI", "Music system disconnected");
            UpdateConnectionStatus();
            ShowError("Music system disconnected");
        }

        private void OnMusicSystemError(object? sender, MusicSystemErrorEventArgs e)
        {
            Debug.Console(0, "MusicScreenUI", "Music system error: {0}", e.ErrorMessage);
            ShowError(e.ErrorMessage);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from events
                if (_musicController != null)
                {
                    _musicController.CatalogUpdated -= OnCatalogUpdated;
                    _musicController.PlaybackStatusChanged -= OnPlaybackStatusChanged;
                    _musicController.TrackTimeUpdated -= OnTrackTimeUpdated;
                    _musicController.Connected -= OnMusicSystemConnected;
                    _musicController.Disconnected -= OnMusicSystemDisconnected;
                    _musicController.MusicSystemError -= OnMusicSystemError;
                }

                _disposed = true;
                Debug.Console(1, "MusicScreenUI", "Disposed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicScreenUI", "Error during disposal: {0}", ex.Message);
            }
        }

        #endregion
    }

    #region Supporting Enums and EventArgs

    /// <summary>
    /// Music browsing state per Client-Scope.md requirements
    /// </summary>
    public enum BrowseState
    {
        ArtistSelection,  // Selecting from artist list
        TrackSelection,   // Selecting tracks for chosen artist  
        NowPlaying        // Track is playing with controls
    }

    /// <summary>
    /// Music playback state change event arguments
    /// </summary>
    public class MusicPlaybackStateEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
    }

    #endregion
}




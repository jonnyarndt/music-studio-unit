using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using musicStudioUnit.Devices;

namespace musicStudioUnit.UserInterface
{
    /// <summary>
    /// Music Browse UI Controller for touch panel integration
    /// Handles artist/track browsing and playback controls per Client-Scope.md
    /// </summary>
    public class MusicBrowseUI : IDisposable
    {
        private readonly EnhancedMusicSystemController _musicController;
        private readonly BasicTriList _panel;
        private readonly object _lockObject = new object();
        
        // Current browse state
        private List<MusicArtist> _currentArtists = new List<MusicArtist>();
        private List<MusicTrack> _currentTracks = new List<MusicTrack>();
        private int _selectedArtistId;
        private int _selectedTrackId;
        private int _currentArtistPage = 1;
        private int _currentTrackPage = 1;
        private const int _pageSize = 10;

        // Touch panel join definitions for music browse screen
        private const uint ArtistListSubpageJoin = 201;        // Artist list subpage
        private const uint TrackListSubpageJoin = 202;         // Track list subpage
        private const uint NowPlayingSubpageJoin = 203;        // Now playing subpage
        
        // Artist list joins (201-210 for 10 artists per page)
        private const uint ArtistButtonBaseJoin = 211;         // Artist selection buttons
        private const uint ArtistTextBaseJoin = 221;           // Artist name text
        private const uint ArtistPrevPageJoin = 231;           // Previous artist page
        private const uint ArtistNextPageJoin = 232;           // Next artist page
        private const uint ArtistPageTextJoin = 233;           // Current artist page
        
        // Track list joins (241-250 for 10 tracks per page)
        private const uint TrackButtonBaseJoin = 241;          // Track selection buttons
        private const uint TrackTextBaseJoin = 251;            // Track name text
        private const uint TrackPrevPageJoin = 261;            // Previous track page
        private const uint TrackNextPageJoin = 262;            // Next track page
        private const uint TrackPageTextJoin = 263;            // Current track page
        private const uint BackToArtistsJoin = 264;            // Back to artists button
        
        // Now playing joins
        private const uint PlayStopButtonJoin = 301;           // Play/Stop toggle button
        private const uint PlayStopTextJoin = 302;             // Play/Stop button text
        private const uint NowPlayingTrackJoin = 303;          // Current track name
        private const uint NowPlayingArtistJoin = 304;         // Current artist name
        private const uint RemainingTimeJoin = 305;            // Remaining time display
        private const uint PlaybackStatusJoin = 306;           // Playback status text
        private const uint BackToBrowseJoin = 307;             // Back to browse button
        
        // Connection status
        private const uint ConnectionStatusJoin = 310;         // Connection status indicator
        private const uint ErrorMessageJoin = 311;            // Error message display

        public EnhancedMusicSystemController MusicController => _musicController;
        public bool IsConnected => _musicController?.IsConnected == true;

        public MusicBrowseUI(EnhancedMusicSystemController musicController, BasicTriList panel)
        {
            _musicController = musicController ?? throw new ArgumentNullException(nameof(musicController));
            _panel = panel ?? throw new ArgumentNullException(nameof(panel));

            Debug.Console(1, "MusicBrowseUI", "Initializing music browse UI");

            // Subscribe to music controller events
            _musicController.CatalogUpdated += OnCatalogUpdated;
            _musicController.PlaybackStatusChanged += OnPlaybackStatusChanged;
            _musicController.TrackTimeUpdated += OnTrackTimeUpdated;
            _musicController.Connected += OnMusicSystemConnected;
            _musicController.Disconnected += OnMusicSystemDisconnected;
            _musicController.MusicSystemError += OnMusicSystemError;

            // Setup touch panel event handlers
            SetupTouchPanelEvents();

            // Initialize UI
            UpdateUI();

            Debug.Console(1, "MusicBrowseUI", "Music browse UI initialized successfully");
        }

        /// <summary>
        /// Setup touch panel button events
        /// </summary>
        private void SetupTouchPanelEvents()
        {
            _panel.SigChange += (device, args) =>
            {
                if (!args.Sig.BoolValue) return; // Only respond to button presses

                uint join = args.Sig.Number;

                // Artist selection buttons
                if (join >= ArtistButtonBaseJoin && join < ArtistButtonBaseJoin + _pageSize)
                {
                    int artistIndex = (int)(join - ArtistButtonBaseJoin);
                    OnArtistSelected(artistIndex);
                }
                // Track selection buttons
                else if (join >= TrackButtonBaseJoin && join < TrackButtonBaseJoin + _pageSize)
                {
                    int trackIndex = (int)(join - TrackButtonBaseJoin);
                    OnTrackSelected(trackIndex);
                }
                // Navigation buttons
                else
                {
                    switch (join)
                    {
                        case ArtistPrevPageJoin:
                            OnArtistPreviousPage();
                            break;
                        case ArtistNextPageJoin:
                            OnArtistNextPage();
                            break;
                        case TrackPrevPageJoin:
                            OnTrackPreviousPage();
                            break;
                        case TrackNextPageJoin:
                            OnTrackNextPage();
                            break;
                        case BackToArtistsJoin:
                            OnBackToArtists();
                            break;
                        case PlayStopButtonJoin:
                            OnPlayStopPressed();
                            break;
                        case BackToBrowseJoin:
                            OnBackToBrowse();
                            break;
                    }
                }
            };

            Debug.Console(2, "MusicBrowseUI", "Touch panel events configured");
        }

        /// <summary>
        /// Handle artist selection
        /// </summary>
        private void OnArtistSelected(int artistIndex)
        {
            lock (_lockObject)
            {
                try
                {
                    if (artistIndex >= 0 && artistIndex < _currentArtists.Count)
                    {
                        var selectedArtist = _currentArtists[artistIndex];
                        _selectedArtistId = selectedArtist.Id;
                        
                        Debug.Console(1, "MusicBrowseUI", "Artist selected: {0} - {1}", 
                            selectedArtist.Id, selectedArtist.Name);

                        // Load tracks for selected artist
                        LoadTracksForArtist(selectedArtist.Id);
                        
                        // Switch to track list view
                        ShowTrackList();
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicBrowseUI", "Error selecting artist: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Handle track selection and start playback
        /// </summary>
        private void OnTrackSelected(int trackIndex)
        {
            lock (_lockObject)
            {
                try
                {
                    if (trackIndex >= 0 && trackIndex < _currentTracks.Count)
                    {
                        var selectedTrack = _currentTracks[trackIndex];
                        _selectedTrackId = selectedTrack.Id;
                        
                        Debug.Console(1, "MusicBrowseUI", "Track selected: {0} - {1}", 
                            selectedTrack.Id, selectedTrack.Name);

                        // Start playback
                        var artist = _musicController.GetArtistById(_selectedArtistId);
                        if (artist != null)
                        {
                            if (_musicController.PlayTrack(selectedTrack.Id, selectedTrack.Name, artist.Name))
                            {
                                // Switch to now playing view
                                ShowNowPlaying();
                            }
                            else
                            {
                                ShowError("Failed to start track playback");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicBrowseUI", "Error selecting track: {0}", ex.Message);
                    ShowError("Error starting playback");
                }
            }
        }

        /// <summary>
        /// Handle play/stop button press
        /// </summary>
        private void OnPlayStopPressed()
        {
            lock (_lockObject)
            {
                try
                {
                    var status = _musicController.GetPlaybackStatus();
                    
                    if (status.IsPlaying)
                    {
                        Debug.Console(1, "MusicBrowseUI", "Stopping playback");
                        _musicController.StopTrack();
                    }
                    else
                    {
                        // If we have a selected track, try to play it
                        if (_selectedTrackId > 0 && _selectedArtistId > 0)
                        {
                            var selectedTrack = _currentTracks.FirstOrDefault(t => t.Id == _selectedTrackId);
                            var artist = _musicController.GetArtistById(_selectedArtistId);
                            
                            if (selectedTrack != null && artist != null)
                            {
                                Debug.Console(1, "MusicBrowseUI", "Starting playback");
                                _musicController.PlayTrack(selectedTrack.Id, selectedTrack.Name, artist.Name);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "MusicBrowseUI", "Error handling play/stop: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Load tracks for specific artist
        /// </summary>
        private void LoadTracksForArtist(int artistId)
        {
            try
            {
                Debug.Console(1, "MusicBrowseUI", "Loading tracks for artist {0}", artistId);
                
                _currentTracks = _musicController.GetTracksForArtist(artistId);
                _currentTrackPage = 1;
                
                Debug.Console(1, "MusicBrowseUI", "Loaded {0} tracks", _currentTracks.Count);
                
                UpdateTrackList();
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicBrowseUI", "Error loading tracks: {0}", ex.Message);
                ShowError("Failed to load tracks");
            }
        }

        /// <summary>
        /// Update artist list display
        /// </summary>
        private void UpdateArtistList()
        {
            try
            {
                _currentArtists = _musicController.GetArtistList();
                
                // Calculate page bounds
                int startIndex = (_currentArtistPage - 1) * _pageSize;
                int endIndex = Math.Min(startIndex + _pageSize, _currentArtists.Count);
                
                // Update artist buttons and text
                for (int i = 0; i < _pageSize; i++)
                {
                    uint buttonJoin = ArtistButtonBaseJoin + (uint)i;
                    uint textJoin = ArtistTextBaseJoin + (uint)i;
                    
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
                
                // Update pagination buttons
                _panel.BooleanInput[ArtistPrevPageJoin].BoolValue = _currentArtistPage > 1;
                _panel.BooleanInput[ArtistNextPageJoin].BoolValue = endIndex < _currentArtists.Count;
                
                // Update page text
                int totalPages = (int)Math.Ceiling((double)_currentArtists.Count / _pageSize);
                _panel.StringInput[ArtistPageTextJoin].StringValue = 
                    string.Format("Page {0} of {1}", _currentArtistPage, totalPages);

                Debug.Console(2, "MusicBrowseUI", "Updated artist list - page {0}, showing {1} artists", 
                    _currentArtistPage, endIndex - startIndex);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicBrowseUI", "Error updating artist list: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update track list display
        /// </summary>
        private void UpdateTrackList()
        {
            try
            {
                // Calculate page bounds
                int startIndex = (_currentTrackPage - 1) * _pageSize;
                int endIndex = Math.Min(startIndex + _pageSize, _currentTracks.Count);
                
                // Update track buttons and text
                for (int i = 0; i < _pageSize; i++)
                {
                    uint buttonJoin = TrackButtonBaseJoin + (uint)i;
                    uint textJoin = TrackTextBaseJoin + (uint)i;
                    
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
                
                // Update pagination buttons
                _panel.BooleanInput[TrackPrevPageJoin].BoolValue = _currentTrackPage > 1;
                _panel.BooleanInput[TrackNextPageJoin].BoolValue = endIndex < _currentTracks.Count;
                
                // Update page text
                int totalPages = (int)Math.Ceiling((double)_currentTracks.Count / _pageSize);
                _panel.StringInput[TrackPageTextJoin].StringValue = 
                    string.Format("Page {0} of {1}", _currentTrackPage, totalPages);

                Debug.Console(2, "MusicBrowseUI", "Updated track list - page {0}, showing {1} tracks", 
                    _currentTrackPage, endIndex - startIndex);
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicBrowseUI", "Error updating track list: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update now playing display
        /// </summary>
        private void UpdateNowPlaying()
        {
            try
            {
                var status = _musicController.GetPlaybackStatus();
                
                _panel.StringInput[NowPlayingTrackJoin].StringValue = status.CurrentTrackName ?? "No Track";
                _panel.StringInput[NowPlayingArtistJoin].StringValue = status.CurrentArtistName ?? "No Artist";
                _panel.StringInput[RemainingTimeJoin].StringValue = status.FormattedRemainingTime;
                
                // Update play/stop button
                _panel.StringInput[PlayStopTextJoin].StringValue = status.IsPlaying ? "Stop" : "Play";
                _panel.BooleanInput[PlayStopButtonJoin].BoolValue = true; // Always enable
                
                // Update status text
                string statusText = status.IsPlaying ? "Playing" : "Stopped";
                _panel.StringInput[PlaybackStatusJoin].StringValue = statusText;

                Debug.Console(2, "MusicBrowseUI", "Updated now playing display");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "MusicBrowseUI", "Error updating now playing: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Show artist list view
        /// </summary>
        private void ShowArtistList()
        {
            _panel.BooleanInput[ArtistListSubpageJoin].BoolValue = true;
            _panel.BooleanInput[TrackListSubpageJoin].BoolValue = false;
            _panel.BooleanInput[NowPlayingSubpageJoin].BoolValue = false;
            UpdateArtistList();
        }

        /// <summary>
        /// Show track list view
        /// </summary>
        private void ShowTrackList()
        {
            _panel.BooleanInput[ArtistListSubpageJoin].BoolValue = false;
            _panel.BooleanInput[TrackListSubpageJoin].BoolValue = true;
            _panel.BooleanInput[NowPlayingSubpageJoin].BoolValue = false;
            UpdateTrackList();
        }

        /// <summary>
        /// Show now playing view
        /// </summary>
        private void ShowNowPlaying()
        {
            _panel.BooleanInput[ArtistListSubpageJoin].BoolValue = false;
            _panel.BooleanInput[TrackListSubpageJoin].BoolValue = false;
            _panel.BooleanInput[NowPlayingSubpageJoin].BoolValue = true;
            UpdateNowPlaying();
        }

        /// <summary>
        /// Show error message
        /// </summary>
        private void ShowError(string message)
        {
            _panel.StringInput[ErrorMessageJoin].StringValue = message;
            Debug.Console(0, "MusicBrowseUI", "Error displayed: {0}", message);
            
            // Clear error after 5 seconds
            CTimer.Wait(5000, () => _panel.StringInput[ErrorMessageJoin].StringValue = "");
        }

        /// <summary>
        /// Navigation methods
        /// </summary>
        private void OnArtistPreviousPage()
        {
            if (_currentArtistPage > 1)
            {
                _currentArtistPage--;
                UpdateArtistList();
            }
        }

        private void OnArtistNextPage()
        {
            int totalPages = (int)Math.Ceiling((double)_currentArtists.Count / _pageSize);
            if (_currentArtistPage < totalPages)
            {
                _currentArtistPage++;
                UpdateArtistList();
            }
        }

        private void OnTrackPreviousPage()
        {
            if (_currentTrackPage > 1)
            {
                _currentTrackPage--;
                UpdateTrackList();
            }
        }

        private void OnTrackNextPage()
        {
            int totalPages = (int)Math.Ceiling((double)_currentTracks.Count / _pageSize);
            if (_currentTrackPage < totalPages)
            {
                _currentTrackPage++;
                UpdateTrackList();
            }
        }

        private void OnBackToArtists()
        {
            ShowArtistList();
        }

        private void OnBackToBrowse()
        {
            if (_selectedArtistId > 0)
            {
                ShowTrackList();
            }
            else
            {
                ShowArtistList();
            }
        }

        /// <summary>
        /// Update overall UI state
        /// </summary>
        private void UpdateUI()
        {
            UpdateConnectionStatus();
            UpdateArtistList();
            UpdateNowPlaying();
        }

        /// <summary>
        /// Update connection status indicator
        /// </summary>
        private void UpdateConnectionStatus()
        {
            _panel.BooleanInput[ConnectionStatusJoin].BoolValue = IsConnected;
        }

        /// <summary>
        /// Event handlers
        /// </summary>
        private void OnCatalogUpdated(object sender, MusicCatalogUpdatedEventArgs args)
        {
            Debug.Console(1, "MusicBrowseUI", "Catalog updated - refreshing display");
            UpdateArtistList();
        }

        private void OnPlaybackStatusChanged(object sender, PlaybackStatusChangedEventArgs args)
        {
            Debug.Console(1, "MusicBrowseUI", "Playback status changed - updating display");
            UpdateNowPlaying();
        }

        private void OnTrackTimeUpdated(object sender, TrackTimeUpdatedEventArgs args)
        {
            // Update remaining time display
            _panel.StringInput[RemainingTimeJoin].StringValue = args.FormattedRemainingTime;
        }

        private void OnMusicSystemConnected(object sender, MusicSystemConnectedEventArgs args)
        {
            Debug.Console(1, "MusicBrowseUI", "Music system connected");
            UpdateConnectionStatus();
        }

        private void OnMusicSystemDisconnected(object sender, MusicSystemDisconnectedEventArgs args)
        {
            Debug.Console(1, "MusicBrowseUI", "Music system disconnected");
            UpdateConnectionStatus();
        }

        private void OnMusicSystemError(object sender, MusicSystemErrorEventArgs args)
        {
            Debug.Console(0, "MusicBrowseUI", "Music system error: {0}", args.ErrorMessage);
            ShowError(args.ErrorMessage);
        }

        public void Dispose()
        {
            if (_musicController != null)
            {
                _musicController.CatalogUpdated -= OnCatalogUpdated;
                _musicController.PlaybackStatusChanged -= OnPlaybackStatusChanged;
                _musicController.TrackTimeUpdated -= OnTrackTimeUpdated;
                _musicController.Connected -= OnMusicSystemConnected;
                _musicController.Disconnected -= OnMusicSystemDisconnected;
                _musicController.MusicSystemError -= OnMusicSystemError;
            }
        }
    }
}

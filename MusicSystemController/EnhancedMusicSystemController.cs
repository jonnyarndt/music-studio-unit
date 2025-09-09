using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;
using core_tools;
using core_tools;
using musicStudioUnit.Configuration;
using System.Text.RegularExpressions;

namespace musicStudioUnit.Devices
{
    /// <summary>
    /// Enhanced Music System Controller for QuirkyTech Digital Music System (DMS)
    /// Implements complete protocol per Client-Scope.md Appendix C
    /// </summary>
    public class EnhancedMusicSystemController : core_tools.IKeyName, IDisposable
    {
        private readonly string _key;
        private readonly DMSInfo _config;
        private readonly string _msuUID;
        private readonly object _lockObject = new object();

        // TCP connections
        private TcpCoreClient _commandClient;
        private Core_tools_tcpServer _feedbackServer;
        private readonly CTimer _responseTimeoutTimer;

        // Music catalog data
        private int _totalArtistCount;
        private readonly Dictionary<int, MusicArtist> _artists = new Dictionary<int, MusicArtist>();
        private readonly Dictionary<int, List<MusicTrack>> _artistTracks = new Dictionary<int, List<MusicTrack>>();
        private readonly Dictionary<int, int> _artistTrackCounts = new Dictionary<int, int>();

        // Current playback state
        private int _currentTrackId;
        private string _currentTrackName;
        private string _currentArtistName;
        private int _remainingTimeSeconds;
        private bool _isPlaying;
        private bool _waitingForResponse;

        // Response handling
        private readonly CrestronQueue<string> _responseQueue;
        private string _lastResponse;

        public string Key => _key;
        public string Name => "Enhanced Music System Controller";

        // Properties for status
        public int TotalArtistCount => _totalArtistCount;
        public bool IsPlaying => _isPlaying;
        public string CurrentTrackName => _currentTrackName;
        public string CurrentArtistName => _currentArtistName;
        public int RemainingTimeSeconds => _remainingTimeSeconds;
        public bool IsConnected => _commandClient?.IsConnected == true;
        public int ArtistCount => _artists.Count;

        // Events
        public event EventHandler<MusicCatalogUpdatedEventArgs> CatalogUpdated;
        public event EventHandler<PlaybackStatusChangedEventArgs> PlaybackStatusChanged;
        public event EventHandler<TrackTimeUpdatedEventArgs> TrackTimeUpdated;
        public event EventHandler<MusicSystemErrorEventArgs> MusicSystemError;
        public event EventHandler<MusicSystemConnectedEventArgs> Connected;
        public event EventHandler<MusicSystemDisconnectedEventArgs> Disconnected;

        public EnhancedMusicSystemController(string key, DMSInfo config, string msuUID)
        {
            _key = key;
            _config = config;
            _msuUID = msuUID;
            
            core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Initializing Enhanced Music System Controller for MSU: {0}", msuUID);

            // Initialize response queue and timeout timer
            _responseQueue = new CrestronQueue<string>(50);
            _responseTimeoutTimer = new CTimer(OnResponseTimeout, 10000);

            DeviceManager.AddDevice(key, this);
            core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Enhanced Music System Controller created successfully");
        }

        /// <summary>
        /// Initialize the music system controller
        /// </summary>
        public bool Initialize()
        {
            try
            {
                core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Initializing Enhanced Music System Controller");

                // Create command client for DMS commands
                _commandClient = new TcpCoreClient(
                    _key + "_CommandClient",
                    "DMS Command Client",
                    _config.IP,
                    _config.Port);

                _commandClient.DataReceived += OnCommandDataReceived;
                _commandClient.Connected += OnCommandClientConnected;
                _commandClient.Disconnected += OnCommandClientDisconnected;

                // Create feedback server for time counter
                _feedbackServer = new Core_tools_tcpServer(
                    _key + "_FeedbackServer",
                    "DMS Feedback Server",
                    _config.ListenPort);

                _feedbackServer.DataReceivedObservable.Subscribe(data => OnFeedbackDataReceived(data));

                // Connect command client
                if (_commandClient.Connect())
                {
                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Music System Controller initialized successfully");
                    
                    // Load initial catalog
                    new CTimer((obj) => LoadMusicCatalog(), 1000);
                    return true;
                }
                else
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to connect to DMS");
                    return false;
                }
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error initializing Music System Controller: {0}", ex.Message);
                MusicSystemError?.Invoke(this, new MusicSystemErrorEventArgs { ErrorMessage = ex.Message });
                return false;
            }
        }

        /// <summary>
        /// Load complete music catalog from DMS
        /// </summary>
        public void LoadMusicCatalog()
        {
            lock (_lockObject)
            {
                try
                {
                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Loading music catalog from DMS...");

                    // First, get the total artist count
                    if (GetArtistCount())
                    {
                        core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Found {0} artists in catalog", _totalArtistCount);
                        
                        // Load all artists in chunks of 10 (DMS limitation)
                        _artists.Clear();
                        
                        for (int startIndex = 1; startIndex <= _totalArtistCount; startIndex += 10)
                        {
                            var artistsChunk = GetArtistRange(startIndex, Math.Min(startIndex + 9, _totalArtistCount));
                            foreach (var artist in artistsChunk)
                            {
                                _artists[artist.Id] = artist;
                            }
                        }

                        core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Loaded {0} artists from catalog", _artists.Count);
                        
                        // Fire catalog updated event
                        CatalogUpdated?.Invoke(this, new MusicCatalogUpdatedEventArgs
                        {
                            TotalArtists = _totalArtistCount,
                            LoadedArtists = _artists.Count
                        });
                    }
                    else
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get artist count from DMS");
                    }
                }
                catch (Exception ex)
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error loading music catalog: {0}", ex.Message);
                    MusicSystemError?.Invoke(this, new MusicSystemErrorEventArgs { ErrorMessage = ex.Message });
                }
            }
        }

        /// <summary>
        /// Get artist count from DMS
        /// </summary>
        private bool GetArtistCount()
        {
            try
            {
                if (!SendCommandAndWaitForResponse("QDMS ARTIST COUNT?", out string response))
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get response for artist count");
                    return false;
                }

                // Parse response: "QDMS ARTIST COUNT 15"
                var match = Regex.Match(response, @"QDMS ARTIST COUNT (\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out _totalArtistCount))
                {
                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Artist count: {0}", _totalArtistCount);
                    return true;
                }

                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Invalid artist count response: {0}", response);
                return false;
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error getting artist count: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Get range of artists from DMS
        /// </summary>
        private List<MusicArtist> GetArtistRange(int startIndex, int endIndex)
        {
            var artists = new List<MusicArtist>();

            try
            {
                string command = string.Format("QDMS LIST ARTIST START {0} END {1}", startIndex, endIndex);
                
                if (!SendCommandAndWaitForResponse(command, out string initialResponse))
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get response for artist range");
                    return artists;
                }

                // Process initial response and subsequent lines
                artists.AddRange(ProcessArtistResponse(initialResponse));

                // Get additional responses for the range (up to 10 artists per request)
                int expectedResponses = endIndex - startIndex;
                for (int i = 0; i < expectedResponses; i++)
                {
                    if (WaitForResponse(out string additionalResponse, 2000))
                    {
                        artists.AddRange(ProcessArtistResponse(additionalResponse));
                    }
                }

                core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Retrieved {0} artists from range {1}-{2}", artists.Count, startIndex, endIndex);
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error getting artist range: {0}", ex.Message);
            }

            return artists;
        }

        /// <summary>
        /// Process artist response line
        /// </summary>
        private List<MusicArtist> ProcessArtistResponse(string response)
        {
            var artists = new List<MusicArtist>();

            try
            {
                // Parse: QDMS ARTIST 1 "The Rolling Scones"
                var match = Regex.Match(response, @"QDMS ARTIST (\d+) ""([^""]+)""");
                if (match.Success)
                {
                    int artistId = int.Parse(match.Groups[1].Value);
                    string artistName = match.Groups[2].Value;

                    artists.Add(new MusicArtist
                    {
                        Id = artistId,
                        Name = artistName
                    });

                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Parsed artist: {0} - {1}", artistId, artistName);
                }
                else
                {
                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Empty or invalid artist response: {0}", response);
                }
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error processing artist response: {0}", ex.Message);
            }

            return artists;
        }

        /// <summary>
        /// Get tracks for a specific artist
        /// </summary>
        public List<MusicTrack> GetTracksForArtist(int artistId)
        {
            lock (_lockObject)
            {
                try
                {
                    // Check if we already have tracks cached
                    if (_artistTracks.ContainsKey(artistId))
                    {
                        return _artistTracks[artistId];
                    }

                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Loading tracks for artist {0}", artistId);

                    // Get track count for artist
                    string countCommand = string.Format("QDMS ARTIST {0} TRACK COUNT?", artistId);
                    if (!SendCommandAndWaitForResponse(countCommand, out string countResponse))
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get track count for artist {0}", artistId);
                        return new List<MusicTrack>();
                    }

                    // Parse track count response
                    var countMatch = Regex.Match(countResponse, string.Format(@"QDMS ARTIST {0} TRACK COUNT (\d+)", artistId));
                    if (!countMatch.Success || !int.TryParse(countMatch.Groups[1].Value, out int trackCount))
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Invalid track count response for artist {0}: {1}", artistId, countResponse);
                        return new List<MusicTrack>();
                    }

                    _artistTrackCounts[artistId] = trackCount;
                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Artist {0} has {1} tracks", artistId, trackCount);

                    // Load all tracks in chunks of 10
                    var allTracks = new List<MusicTrack>();
                    for (int startIndex = 1; startIndex <= trackCount; startIndex += 10)
                    {
                        int endIndex = Math.Min(startIndex + 9, trackCount);
                        var tracksChunk = GetTrackRange(artistId, startIndex, endIndex);
                        allTracks.AddRange(tracksChunk);
                    }

                    // Cache the tracks
                    _artistTracks[artistId] = allTracks;
                    
                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Loaded {0} tracks for artist {1}", allTracks.Count, artistId);
                    return allTracks;
                }
                catch (Exception ex)
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error getting tracks for artist {0}: {1}", artistId, ex.Message);
                    MusicSystemError?.Invoke(this, new MusicSystemErrorEventArgs { ErrorMessage = ex.Message });
                    return new List<MusicTrack>();
                }
            }
        }

        /// <summary>
        /// Get range of tracks for an artist
        /// </summary>
        private List<MusicTrack> GetTrackRange(int artistId, int startIndex, int endIndex)
        {
            var tracks = new List<MusicTrack>();

            try
            {
                string command = string.Format("QDMS LIST ARTIST {0} TRACK START {1} END {2}", 
                    artistId, startIndex, endIndex);
                
                if (!SendCommandAndWaitForResponse(command, out string initialResponse))
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get response for track range");
                    return tracks;
                }

                // Process initial response and subsequent lines
                tracks.AddRange(ProcessTrackResponse(initialResponse));

                // Get additional responses for the range
                int expectedResponses = endIndex - startIndex;
                for (int i = 0; i < expectedResponses; i++)
                {
                    if (WaitForResponse(out string additionalResponse, 2000))
                    {
                        tracks.AddRange(ProcessTrackResponse(additionalResponse));
                    }
                }

                core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Retrieved {0} tracks from range {1}-{2} for artist {3}", 
                    tracks.Count, startIndex, endIndex, artistId);
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error getting track range: {0}", ex.Message);
            }

            return tracks;
        }

        /// <summary>
        /// Process track response line
        /// </summary>
        private List<MusicTrack> ProcessTrackResponse(string response)
        {
            var tracks = new List<MusicTrack>();

            try
            {
                // Parse: QDMS TRACK 1001 "Paint It Bread"
                var match = Regex.Match(response, @"QDMS TRACK (\d+) ""([^""]+)""");
                if (match.Success)
                {
                    int trackId = int.Parse(match.Groups[1].Value);
                    string trackName = match.Groups[2].Value;

                    tracks.Add(new MusicTrack
                    {
                        Id = trackId,
                        Name = trackName
                    });

                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Parsed track: {0} - {1}", trackId, trackName);
                }
                else
                {
                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Empty or invalid track response: {0}", response);
                }
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error processing track response: {0}", ex.Message);
            }

            return tracks;
        }

        /// <summary>
        /// Start playback of a specific track
        /// </summary>
        public bool PlayTrack(int trackId, string trackName, string artistName)
        {
            lock (_lockObject)
            {
                try
                {
                    if (_isPlaying)
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Cannot play track - another track is already playing. Stop current track first.");
                        return false;
                    }

                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Starting playback: Track {0} ({1}) by {2}", trackId, trackName, artistName);

                    // Get current IP address for feedback
                    var sysInfo = new SystemInformationMethods();
                    sysInfo.GetEthernetInfo();
                    string ipAddress = sysInfo.Adapter.IpAddress;

                    // Build PLAY command per Client-Scope.md
                    string command = string.Format("QDMS PLAY {0} FOR {1} SEND {2} START", 
                        trackId, _msuUID, ipAddress);

                    if (!SendCommandAndWaitForResponse(command, out string response))
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get response for play command");
                        return false;
                    }

                    // Check for success response: "QDMS PLAY 1002 OK"
                    string expectedResponse = string.Format("QDMS PLAY {0} OK", trackId);
                    if (response.Trim() == expectedResponse)
                    {
                        _isPlaying = true;
                        _currentTrackId = trackId;
                        _currentTrackName = trackName;
                        _currentArtistName = artistName;
                        _remainingTimeSeconds = 0; // Will be updated by feedback

                        core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Track playback started successfully");

                        // Fire playback status changed event
                        PlaybackStatusChanged?.Invoke(this, new PlaybackStatusChangedEventArgs
                        {
                            IsPlaying = true,
                            TrackId = trackId,
                            TrackName = trackName,
                            ArtistName = artistName
                        });

                        return true;
                    }
                    else
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "DMS returned error for play command: {0}", response);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error playing track: {0}", ex.Message);
                    MusicSystemError?.Invoke(this, new MusicSystemErrorEventArgs { ErrorMessage = ex.Message });
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop playback of current track
        /// </summary>
        public bool StopTrack()
        {
            lock (_lockObject)
            {
                try
                {
                    if (!_isPlaying)
                    {
                        core_tools.Debug.Console(1, "EnhancedMusicSystemController", "No track currently playing");
                        return true;
                    }

                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Stopping playback of track {0}", _currentTrackId);

                    // Build STOP command per Client-Scope.md
                    string command = string.Format("QDMS STOP {0} FOR {1}", _currentTrackId, _msuUID);

                    if (!SendCommandAndWaitForResponse(command, out string response))
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Failed to get response for stop command");
                        return false;
                    }

                    // Check for success response: "QDMS STOP 1002 OK"
                    string expectedResponse = string.Format("QDMS STOP {0} OK", _currentTrackId);
                    if (response.Trim() == expectedResponse)
                    {
                        _isPlaying = false;
                        int stoppedTrackId = _currentTrackId;
                        string stoppedTrackName = _currentTrackName;
                        string stoppedArtistName = _currentArtistName;

                        _currentTrackId = 0;
                        _currentTrackName = "";
                        _currentArtistName = "";
                        _remainingTimeSeconds = 0;

                        core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Track playback stopped successfully");

                        // Fire playback status changed event
                        PlaybackStatusChanged?.Invoke(this, new PlaybackStatusChangedEventArgs
                        {
                            IsPlaying = false,
                            TrackId = stoppedTrackId,
                            TrackName = stoppedTrackName,
                            ArtistName = stoppedArtistName
                        });

                        return true;
                    }
                    else
                    {
                        core_tools.Debug.Console(0, "EnhancedMusicSystemController", "DMS returned error for stop command: {0}", response);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error stopping track: {0}", ex.Message);
                    MusicSystemError?.Invoke(this, new MusicSystemErrorEventArgs { ErrorMessage = ex.Message });
                    return false;
                }
            }
        }

        /// <summary>
        /// Send command and wait for response
        /// </summary>
        private bool SendCommandAndWaitForResponse(string command, out string response)
        {
            response = "";

            try
            {
                if (!_commandClient.IsConnected)
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Cannot send command - not connected to DMS");
                    return false;
                }

                if (_waitingForResponse)
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Cannot send command - already waiting for response");
                    return false;
                }

                core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Sending DMS command: {0}", command);

                // Clear any old responses
                while (_responseQueue.Count > 0)
                {
                    _responseQueue.Dequeue();
                }

                _waitingForResponse = true;
                _lastResponse = "";

                // Send command with CR LF terminator
                string fullCommand = command + "\r\n";
                _commandClient.SendAsciiMessage(fullCommand);

                // Start timeout timer
                _responseTimeoutTimer.Reset(5000);

                // Wait for response
                if (WaitForResponse(out response, 5000))
                {
                    _waitingForResponse = false;
                    _responseTimeoutTimer.Stop();
                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Received DMS response: {0}", response);
                    return true;
                }
                else
                {
                    _waitingForResponse = false;
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Timeout waiting for DMS response to: {0}", command);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _waitingForResponse = false;
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error sending command: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Wait for response from DMS
        /// </summary>
        private bool WaitForResponse(out string response, int timeoutMs = 5000)
        {
            response = "";
            
            try
            {
                var startTime = DateTime.Now;
                while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    if (_responseQueue.Count > 0)
                    {
                        response = _responseQueue.Dequeue();
                        return true;
                    }
                    Thread.Sleep(10);
                }

                return false;
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error waiting for response: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Handle command data received from DMS
        /// </summary>
        private void OnCommandDataReceived(object sender, TcpDataReceivedEventArgs args)
        {
            try
            {
                string receivedData = Encoding.ASCII.GetString(args.Data).Trim();
                core_tools.Debug.Console(2, "EnhancedMusicSystemController", "DMS command response received: {0}", receivedData);

                // Queue the response for processing
                if (!_responseQueue.Enqueue(receivedData))
                {
                    core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Response queue full, dropping response");
                }
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error processing command data: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle feedback data received from DMS (time counter)
        /// </summary>
        private void OnFeedbackDataReceived(byte[] data)
        {
            try
            {
                core_tools.Debug.Console(2, "EnhancedMusicSystemController", "DMS feedback received: {0} bytes", data.Length);

                // Parse time counter per Client-Scope.md: [51][51][51]<lsb><msb>[03]
                if (data.Length == 6 && 
                    data[0] == 0x51 && data[1] == 0x51 && data[2] == 0x51 && data[5] == 0x03)
                {
                    // Combine LSB and MSB to get remaining seconds
                    int remainingSeconds = (data[4] << 8) | data[3];
                    
                    core_tools.Debug.Console(2, "EnhancedMusicSystemController", "Track time remaining: {0} seconds", remainingSeconds);

                    _remainingTimeSeconds = remainingSeconds;

                    // Fire time updated event
                    TrackTimeUpdated?.Invoke(this, new TrackTimeUpdatedEventArgs
                    {
                        RemainingTimeSeconds = remainingSeconds,
                        TrackId = _currentTrackId,
                        TrackName = _currentTrackName
                    });

                    // If time reaches zero, track has finished
                    if (remainingSeconds == 0 && _isPlaying)
                    {
                        core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Track playback completed");
                        
                        _isPlaying = false;
                        int finishedTrackId = _currentTrackId;
                        string finishedTrackName = _currentTrackName;
                        string finishedArtistName = _currentArtistName;

                        _currentTrackId = 0;
                        _currentTrackName = "";
                        _currentArtistName = "";

                        // Fire playback status changed event
                        PlaybackStatusChanged?.Invoke(this, new PlaybackStatusChangedEventArgs
                        {
                            IsPlaying = false,
                            TrackId = finishedTrackId,
                            TrackName = finishedTrackName,
                            ArtistName = finishedArtistName
                        });
                    }
                }
                else
                {
                    core_tools.Debug.Console(1, "EnhancedMusicSystemController", "Received non-time-counter feedback data");
                }
            }
            catch (Exception ex)
            {
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "Error processing feedback data: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle command client connected
        /// </summary>
        private void OnCommandClientConnected(object sender, EventArgs args)
        {
            core_tools.Debug.Console(1, "EnhancedMusicSystemController", "DMS command client connected");
            Connected?.Invoke(this, new MusicSystemConnectedEventArgs());
        }

        /// <summary>
        /// Handle command client disconnected
        /// </summary>
        private void OnCommandClientDisconnected(object sender, EventArgs args)
        {
            core_tools.Debug.Console(1, "EnhancedMusicSystemController", "DMS command client disconnected");
            _waitingForResponse = false;
            Disconnected?.Invoke(this, new MusicSystemDisconnectedEventArgs());
        }

        /// <summary>
        /// Handle response timeout
        /// </summary>
        private void OnResponseTimeout(object obj)
        {
            if (_waitingForResponse)
            {
                _waitingForResponse = false;
                core_tools.Debug.Console(0, "EnhancedMusicSystemController", "DMS response timeout");
                MusicSystemError?.Invoke(this, new MusicSystemErrorEventArgs 
                { 
                    ErrorMessage = "DMS response timeout" 
                });
            }
        }

        /// <summary>
        /// Get list of artists for UI display
        /// </summary>
        public List<MusicArtist> GetArtistList()
        {
            return _artists.Values.ToList();
        }

        /// <summary>
        /// Get artist by ID
        /// </summary>
        public MusicArtist GetArtistById(int artistId)
        {
            return _artists.ContainsKey(artistId) ? _artists[artistId] : null;
        }

        /// <summary>
        /// Get current playback status
        /// </summary>
        public MusicPlaybackStatus GetPlaybackStatus()
        {
            return new MusicPlaybackStatus
            {
                IsPlaying = _isPlaying,
                CurrentTrackId = _currentTrackId,
                CurrentTrackName = _currentTrackName,
                CurrentArtistName = _currentArtistName,
                RemainingTimeSeconds = _remainingTimeSeconds
            };
        }

        public void Dispose()
        {
            _responseTimeoutTimer?.Dispose();
            _commandClient?.Dispose();
            _feedbackServer?.Dispose();
            
            if (DeviceManager.ContainsKey(Key))
                DeviceManager.RemoveDevice(Key);
        }
    }
}


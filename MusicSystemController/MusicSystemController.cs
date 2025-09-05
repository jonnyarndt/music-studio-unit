using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Crestron.SimplSharp;
using core_tools;
using flexpod.Configuration;

namespace flexpod.Devices
{
    /// <summary>
    /// Music System Controller for QuirkyTech Digital Music System (DMS)
    /// </summary>
    public class MusicSystemController : IKeyName, IDisposable
    {
        private readonly Core_tools_tcpClient _commandClient;
        private readonly Core_tools_tcpServer _feedbackServer;
        private readonly DMSInfo _config;
        private readonly string _key;
        private readonly string _msuUID;
        private readonly CrestronQueue<string> _responseQueue;

        // Music catalog state
        private int _artistCount;
        private readonly Dictionary<int, string> _artists = new Dictionary<int, string>();
        private readonly Dictionary<int, int> _trackCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, string> _tracks = new Dictionary<int, string>();

        // Playback state
        private int _currentTrackId;
        private string _currentTrackName;
        private string _currentArtistName;
        private int _remainingTime;
        private bool _isPlaying;

        public string Key => _key;
        public string Name => "Music System Controller";

        // Properties
        public int ArtistCount => _artistCount;
        public bool IsPlaying => _isPlaying;
        public string CurrentTrackName => _currentTrackName;
        public string CurrentArtistName => _currentArtistName;
        public int RemainingTime => _remainingTime;

        // Events
        public event EventHandler<MusicCatalogUpdatedEventArgs> CatalogUpdated;
        public event EventHandler<PlaybackStatusUpdatedEventArgs> PlaybackUpdated;
        public event EventHandler<TrackTimeUpdatedEventArgs> TimeUpdated;

        public MusicSystemController(string key, DMSInfo config, string msuUID)
        {
            _key = key;
            _config = config;
            _msuUID = msuUID;

            // Create TCP client for commands
            _commandClient = new Core_tools_tcpClient(
                key + "Client",
                "Music System TCP Client",
                config.IP,
                config.Port);

            // Create TCP server for feedback
            _feedbackServer = new Core_tools_tcpServer(
                key + "Server",
                "Music System TCP Server",
                config.ListenPort);

            // Register with device manager
            DeviceManager.AddDevice(key, this);

            // Create response queue
            _responseQueue = new CrestronQueue<string>(100);

            // Register for events
            _commandClient.DataReceived += OnCommandDataReceived;
            _feedbackServer.DataReceivedObservable.Subscribe(OnFeedbackDataReceived);

            Debug.Console(1, this, "Music System Controller initialized for MSU: {0}", msuUID);
        }

        /// <summary>
        /// Initialize the music system controller
        /// </summary>
        public void Initialize()
        {
            Debug.Console(1, this, "Initializing Music System Controller");

            // Start feedback server
            _feedbackServer.Start();

            // Connect command client
            _commandClient.Connect();

            // Initial catalog load
            CTimer.Wait(1000, RefreshArtistCatalog);
        }

        /// <summary>
        /// Refresh the artist catalog from DMS
        /// </summary>
        public void RefreshArtistCatalog()
        {
            Debug.Console(1, this, "Refreshing artist catalog");

            try
            {
                // Get artist count
                SendCommand("QDMS ARTIST COUNT?");
                string response = WaitForResponse();

                if (response.StartsWith("QDMS ARTIST COUNT"))
                {
                    string[] parts = response.Split(' ');
                    if (parts.Length >= 4 && int.TryParse(parts[3], out _artistCount))
                    {
                        Debug.Console(1, this, "Found {0} artists in catalog", _artistCount);
                        LoadAllArtists();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error refreshing artist catalog: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Get list of artists with pagination
        /// </summary>
        public List<Artist> GetArtists(int startIndex = 1, int count = 10)
        {
            var result = new List<Artist>();

            try
            {
                int endIndex = Math.Min(startIndex + count - 1, _artistCount);
                string command = string.Format("QDMS LIST ARTIST START {0} END {1}", startIndex, endIndex);
                SendCommand(command);

                // Process multiple response lines
                for (int i = 0; i < (endIndex - startIndex + 1); i++)
                {
                    string response = WaitForResponse();
                    if (response.StartsWith("QDMS ARTIST"))
                    {
                        var artist = ParseArtistResponse(response);
                        if (artist != null)
                        {
                            result.Add(artist);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error getting artists: {0}", ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Get tracks for a specific artist
        /// </summary>
        public List<Track> GetTracksForArtist(int artistId, int startIndex = 1, int count = 10)
        {
            var result = new List<Track>();

            try
            {
                // First get track count for artist
                string countCommand = string.Format("QDMS ARTIST {0} TRACK COUNT?", artistId);
                SendCommand(countCommand);
                string countResponse = WaitForResponse();

                int trackCount = 0;
                if (countResponse.StartsWith(string.Format("QDMS ARTIST {0} TRACK COUNT", artistId)))
                {
                    string[] parts = countResponse.Split(' ');
                    if (parts.Length >= 5)
                    {
                        int.TryParse(parts[4], out trackCount);
                    }
                }

                if (trackCount > 0)
                {
                    int endIndex = Math.Min(startIndex + count - 1, trackCount);
                    string command = string.Format("QDMS LIST ARTIST {0} TRACK START {1} END {2}",
                        artistId, startIndex, endIndex);
                    SendCommand(command);

                    // Process multiple response lines
                    for (int i = 0; i < (endIndex - startIndex + 1); i++)
                    {
                        string response = WaitForResponse();
                        if (response.StartsWith("QDMS TRACK"))
                        {
                            var track = ParseTrackResponse(response);
                            if (track != null)
                            {
                                result.Add(track);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error getting tracks for artist {0}: {1}", artistId, ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Play a specific track
        /// </summary>
        public void PlayTrack(int trackId, string trackName, string artistName)
        {
            if (_isPlaying)
            {
                Debug.Console(0, this, "Cannot play track while another is playing. Stop current track first.");
                return;
            }

            Debug.Console(1, this, "Playing track {0}: {1} by {2}", trackId, trackName, artistName);

            try
            {
                // Get current IP address for feedback
                var sysInfo = new SystemInformationMethods();
                sysInfo.GetEthernetInfo();
                string ipAddress = sysInfo.Adapter.IpAddress;

                string command = string.Format("QDMS PLAY {0} FOR {1} SEND {2} START",
                    trackId, _msuUID, ipAddress);

                SendCommand(command);
                string response = WaitForResponse();

                if (response.StartsWith(string.Format("QDMS PLAY {0} OK", trackId)))
                {
                    _isPlaying = true;
                    _currentTrackId = trackId;
                    _currentTrackName = trackName;
                    _currentArtistName = artistName;

                    Debug.Console(1, this, "Playback started successfully");

                    // Fire playback updated event
                    PlaybackUpdated?.Invoke(this, new PlaybackStatusUpdatedEventArgs
                    {
                        IsPlaying = true,
                        TrackId = trackId,
                        TrackName = trackName,
                        ArtistName = artistName
                    });
                }
                else
                {
                    Debug.Console(0, this, "Failed to start playback: {0}", response);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error playing track {0}: {1}", trackId, ex.Message);
            }
        }

        /// <summary>
        /// Stop current track playback
        /// </summary>
        public void StopTrack()
        {
            if (!_isPlaying)
            {
                Debug.Console(1, this, "No track is currently playing");
                return;
            }

            Debug.Console(1, this, "Stopping track {0}", _currentTrackId);

            try
            {
                string command = string.Format("QDMS STOP {0} FOR {1}", _currentTrackId, _msuUID);
                SendCommand(command);
                string response = WaitForResponse();

                if (response.StartsWith(string.Format("QDMS STOP {0} OK", _currentTrackId)))
                {
                    _isPlaying = false;
                    _remainingTime = 0;

                    Debug.Console(1, this, "Playback stopped");

                    // Fire playback updated event
                    PlaybackUpdated?.Invoke(this, new PlaybackStatusUpdatedEventArgs
                    {
                        IsPlaying = false,
                        TrackId = 0,
                        TrackName = string.Empty,
                        ArtistName = string.Empty
                    });
                }
                else
                {
                    Debug.Console(0, this, "Failed to stop playback: {0}", response);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error stopping track: {0}", ex.Message);
            }
        }

        private void LoadAllArtists()
        {
            _artists.Clear();

            int batchSize = 10;
            for (int start = 1; start <= _artistCount; start += batchSize)
            {
                var artists = GetArtists(start, batchSize);
                foreach (var artist in artists)
                {
                    _artists[artist.Id] = artist.Name;
                }
            }

            // Fire catalog updated event
            CatalogUpdated?.Invoke(this, new MusicCatalogUpdatedEventArgs
            {
                ArtistCount = _artistCount
            });
        }

        private Artist ParseArtistResponse(string response)
        {
            try
            {
                // Format: QDMS ARTIST 1 "The Rolling Scones"
                var parts = response.Split(' ');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int id))
                {
                    string name = string.Empty;
                    if (parts.Length >= 4)
                    {
                        name = string.Join(" ", parts.Skip(3)).Trim('"');
                    }

                    return new Artist { Id = id, Name = name };
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error parsing artist response: {0}", ex.Message);
            }

            return null;
        }

        private Track ParseTrackResponse(string response)
        {
            try
            {
                // Format: QDMS TRACK 1001 "Paint It Bread"
                var parts = response.Split(' ');
                if (parts.Length >= 3 && int.TryParse(parts[2], out int id))
                {
                    string name = string.Empty;
                    if (parts.Length >= 4)
                    {
                        name = string.Join(" ", parts.Skip(3)).Trim('"');
                    }

                    return new Track { Id = id, Name = name };
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error parsing track response: {0}", ex.Message);
            }

            return null;
        }

        private void SendCommand(string command)
        {
            string fullCommand = command + "\r\n";
            byte[] data = Encoding.ASCII.GetBytes(fullCommand);
            
            Debug.Console(2, this, "Sending: {0}", command);
            _commandClient.Send(data);
        }

        private string WaitForResponse(int timeoutMs = 5000)
        {
            string response;
            if (_responseQueue.TryToDequeue(out response, timeoutMs))
            {
                Debug.Console(2, this, "Received: {0}", response);
                return response;
            }

            Debug.Console(0, this, "Timeout waiting for response");
            return string.Empty;
        }

        private void OnCommandDataReceived(byte[] data)
        {
            try
            {
                string response = Encoding.ASCII.GetString(data).Trim();
                _responseQueue.Enqueue(response);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error processing command response: {0}", ex.Message);
            }
        }

        private void OnFeedbackDataReceived(byte[] data)
        {
            try
            {
                // Process time feedback: [51][51][51]<lsb><msb>[03]
                if (data.Length == 6 && data[0] == 0x51 && data[1] == 0x51 && data[2] == 0x51 && data[5] == 0x03)
                {
                    ushort timeValue = (ushort)((data[4] << 8) | data[3]);
                    _remainingTime = timeValue;

                    Debug.Console(2, this, "Track time remaining: {0}:{1:D2}",
                        _remainingTime / 60, _remainingTime % 60);

                    // Fire time updated event
                    TimeUpdated?.Invoke(this, new TrackTimeUpdatedEventArgs
                    {
                        RemainingTimeSeconds = _remainingTime
                    });

                    // Check if track finished
                    if (_remainingTime == 0 && _isPlaying)
                    {
                        _isPlaying = false;
                        Debug.Console(1, this, "Track playback completed");

                        PlaybackUpdated?.Invoke(this, new PlaybackStatusUpdatedEventArgs
                        {
                            IsPlaying = false,
                            TrackId = 0,
                            TrackName = string.Empty,
                            ArtistName = string.Empty
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error processing feedback data: {0}", ex.Message);
            }
        }

        public void Dispose()
        {
            _commandClient?.Dispose();
            _feedbackServer?.Dispose();
        }
    }

    // Data classes
    public class Artist
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class Track
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    // Event argument classes
    public class MusicCatalogUpdatedEventArgs : EventArgs
    {
        public int ArtistCount { get; set; }
    }

    public class PlaybackStatusUpdatedEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public int TrackId { get; set; }
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
    }

    public class TrackTimeUpdatedEventArgs : EventArgs
    {
        public int RemainingTimeSeconds { get; set; }
    }
}

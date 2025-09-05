using System;
using System.Collections.Generic;
using System.Text;
using Crestron.SimplSharp;
using core_tools;

namespace Masters.Karaoke.Devices
{
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
        private Dictionary<int, string> _artists = new Dictionary<int, string>();
        private Dictionary<int, int> _trackCounts = new Dictionary<int, int>();
        private Dictionary<int, string> _tracks = new Dictionary<int, string>();
        
        // Playback state
        private int _currentTrackId;
        private string _currentTrackName;
        private string _currentArtistName;
        private int _remainingTime;
        private bool _isPlaying;
        
        // Events
        public event EventHandler<MusicCatalogUpdatedEventArgs> CatalogUpdated;
        public event EventHandler<PlaybackStatusUpdatedEventArgs> PlaybackUpdated;
        
        public string Key => _key;
        public string Name => "Music System Controller";
        
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
            
            // Start feedback server
            _feedbackServer.Start();
        }
        
        public void Initialize()
        {
            Debug.Console(1, this, "Initializing Music System Controller");
            
            // Initial catalog load
            RefreshArtistCatalog();
        }
        
        public void RefreshArtistCatalog()
        {
            Debug.Console(1, this, "Refreshing artist catalog");
            
            try
            {
                // Get artist count
                SendCommand("QDMS ARTIST COUNT?");
                string response = WaitForResponse();
                
                if (response.StartsWith("QDMS ARTIST COUNT "))
                {
                    _artistCount = int.Parse(response.Substring("QDMS ARTIST COUNT ".Length));
                    Debug.Console(1, this, "Artist count: {0}", _artistCount);
                    
                    // Fetch artists in batches of 10
                    _artists.Clear();
                    for (int i = 1; i <= _artistCount; i += 10)
                    {
                        FetchArtistBatch(i, Math.Min(i + 9, _artistCount));
                    }
                    
                    CatalogUpdated?.Invoke(this, new MusicCatalogUpdatedEventArgs
                    {
                        ArtistCount = _artistCount,
                        Artists = new Dictionary<int, string>(_artists)
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error refreshing artist catalog: {0}", ex.Message);
            }
        }
        
        private void FetchArtistBatch(int start, int end)
        {
            Debug.Console(2, this, "Fetching artists {0} to {1}", start, end);
            
            SendCommand(string.Format("QDMS LIST ARTIST START {0} END {1}", start, end));
            
            // Process each artist in the response
            for (int i = start; i <= end; i++)
            {
                string response = WaitForResponse();
                
                if (response.StartsWith("QDMS ARTIST "))
                {
                    int idEnd = response.IndexOf(' ', "QDMS ARTIST ".Length);
                    if (idEnd > 0)
                    {
                        string idStr = response.Substring("QDMS ARTIST ".Length, idEnd - "QDMS ARTIST ".Length);
                        string name = response.Substring(idEnd + 2).Trim('"');
                        
                        if (!string.IsNullOrEmpty(idStr))
                        {
                            int artistId = int.Parse(idStr);
                            _artists[artistId] = name;
                            Debug.Console(2, this, "Artist {0}: {1}", artistId, name);
                        }
                    }
                }
            }
        }
        
        public void GetTracksByArtist(int artistId)
        {
            Debug.Console(1, this, "Getting tracks for artist {0}", artistId);
            
            try
            {
                // Get track count for this artist
                SendCommand(string.Format("QDMS ARTIST {0} TRACK COUNT?", artistId));
                string response = WaitForResponse();
                
                if (response.StartsWith(string.Format("QDMS ARTIST {0} TRACK COUNT ", artistId)))
                {
                    int trackCount = int.Parse(response.Substring(string.Format("QDMS ARTIST {0} TRACK COUNT ", artistId).Length));
                    _trackCounts[artistId] = trackCount;
                    Debug.Console(1, this, "Track count for artist {0}: {1}", artistId, trackCount);
                    
                    // Fetch tracks in batches of 10
                    Dictionary<int, string> artistTracks = new Dictionary<int, string>();
                    for (int i = 1; i <= trackCount; i += 10)
                    {
                        FetchTrackBatch(artistId, i, Math.Min(i + 9, trackCount), artistTracks);
                    }
                    
                    // Update combined tracks dictionary
                    foreach (var track in artistTracks)
                    {
                        _tracks[track.Key] = track.Value;
                    }
                    
                    CatalogUpdated?.Invoke(this, new MusicCatalogUpdatedEventArgs
                    {
                        ArtistId = artistId,
                        TrackCount = trackCount,
                        Tracks = artistTracks
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error getting tracks for artist {0}: {1}", artistId, ex.Message);
            }
        }
        
        private void FetchTrackBatch(int artistId, int start, int end, Dictionary<int, string> tracks)
        {
            Debug.Console(2, this, "Fetching tracks {0} to {1} for artist {2}", start, end, artistId);
            
            SendCommand(string.Format("QDMS LIST ARTIST {0} TRACK START {1} END {2}", artistId, start, end));
            
            // Process each track in the response
            for (int i = start; i <= end; i++)
            {
                string response = WaitForResponse();
                
                if (response.StartsWith("QDMS TRACK "))
                {
                    int idEnd = response.IndexOf(' ', "QDMS TRACK ".Length);
                    if (idEnd > 0)
                    {
                        string idStr = response.Substring("QDMS TRACK ".Length, idEnd - "QDMS TRACK ".Length);
                        string name = response.Substring(idEnd + 2).Trim('"');
                        
                        if (!string.IsNullOrEmpty(idStr))
                        {
                            int trackId = int.Parse(idStr);
                            tracks[trackId] = name;
                            Debug.Console(2, this, "Track {0}: {1}", trackId, name);
                        }
                    }
                }
            }
        }
        
        public void PlayTrack(int trackId)
        {
            if (_isPlaying)
            {
                Debug.Console(0, this, "Cannot play track while another is playing. Stop current track first.");
                return;
            }
            
            Debug.Console(1, this, "Playing track {0}", trackId);
            
            try
            {
                string ipAddress = CrestronEthernetHelper.GetEthernetParameter(
                    CrestronEthernetHelper.ETHERNET_PARAMETER_TO_GET.GET_CURRENT_IP_ADDRESS, 0);
                
                string command = string.Format("QDMS PLAY {0} FOR {1} SEND {2} START", 
                    trackId, _msuUID, ipAddress);
                
                SendCommand(command);
                string response = WaitForResponse();
                
                if (response.StartsWith(string.Format("QDMS PLAY {0} OK", trackId)))
                {
                    _currentTrackId = trackId;
                    _currentTrackName = _tracks.ContainsKey(trackId) ? _tracks[trackId] : "Unknown Track";
                    _isPlaying = true;
                    
                    Debug.Console(1, this, "Now playing: {0}", _currentTrackName);
                    
                    PlaybackUpdated?.Invoke(this, new PlaybackStatusUpdatedEventArgs
                    {
                        IsPlaying = true,
                        TrackId = trackId,
                        TrackName = _currentTrackName,
                        ArtistName = _currentArtistName,
                        RemainingTime = 0 // Will be updated by feedback
                    });
                }
                else
                {
                    Debug.Console(0, this, "Failed to play track: {0}", response);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error playing track {0}: {1}", trackId, ex.Message);
            }
        }
        
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
                    
                    Debug.Console(1, this, "Playback stopped");
                    
                    PlaybackUpdated?.Invoke(this, new PlaybackStatusUpdatedEventArgs
                    {
                        IsPlaying = false,
                        TrackId = _currentTrackId,
                        TrackName = _currentTrackName,
                        ArtistName = _currentArtistName,
                        RemainingTime = 0
                    });
                }
                else
                {
                    Debug.Console(0, this, "Failed to stop track: {0}", response);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error stopping track {0}: {1}", _currentTrackId, ex.Message);
            }
        }
        
        private void SendCommand(string command)
        {
            Debug.Console(2, this, "Sending command: {0}", command);
            
            if (_commandClient.IsConnected)
            {
                _commandClient.Send(Encoding.ASCII.GetBytes(command + "\r\n"));
            }
            else
            {
                Debug.Console(1, this, "TCP client not connected. Connecting...");
                _commandClient.Connect();
                _commandClient.Send(Encoding.ASCII.GetBytes(command + "\r\n"));
            }
        }
        
        private string WaitForResponse(int timeoutMs = 5000)
        {
            string response = null;
            
            if (_responseQueue.Wait(timeoutMs))
            {
                _responseQueue.Dequeue(out response);
            }
            else
            {
                throw new TimeoutException("Timeout waiting for music system response");
            }
            
            return response;
        }
        
        private void OnCommandDataReceived(byte[] data)
        {
            string response = Encoding.ASCII.GetString(data).Trim();
            Debug.Console(2, this, "Received response: {0}", response);
            
            _responseQueue.Enqueue(response);
        }
        
        private void OnFeedbackDataReceived(byte[] data)
        {
            if (data.Length >= 6 && data[0] == 0x51 && data[1] == 0x51 && data[2] == 0x51 && data[5] == 0x03)
            {
                // Process time remaining feedback
                ushort seconds = (ushort)((data[4] << 8) | data[3]);
                _remainingTime = seconds;
                
                TimeSpan time = TimeSpan.FromSeconds(seconds);
                string timeStr = string.Format("{0}:{1:00}", (int)time.TotalMinutes, time.Seconds);
                
                Debug.Console(2, this, "Track remaining time: {0}", timeStr);
                
                if (seconds == 0 && _isPlaying)
                {
                    // Track has finished
                    _isPlaying = false;
                    
                    Debug.Console(1, this, "Track playback completed");
                }
                
                PlaybackUpdated?.Invoke(this, new PlaybackStatusUpdatedEventArgs
                {
                    IsPlaying = _isPlaying,
                    TrackId = _currentTrackId,
                    TrackName = _currentTrackName,
                    ArtistName = _currentArtistName,
                    RemainingTime = seconds,
                    RemainingTimeFormatted = timeStr
                });
            }
        }
        
        public void Dispose()
        {
            _commandClient.Dispose();
            _feedbackServer.Dispose();
        }
    }
    
    public class MusicCatalogUpdatedEventArgs : EventArgs
    {
        public int ArtistCount { get; set; }
        public Dictionary<int, string> Artists { get; set; }
        public int ArtistId { get; set; }
        public int TrackCount { get; set; }
        public Dictionary<int, string> Tracks { get; set; }
    }
    
    public class PlaybackStatusUpdatedEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public int TrackId { get; set; }
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
        public int RemainingTime { get; set; }
        public string RemainingTimeFormatted { get; set; }
    }
}
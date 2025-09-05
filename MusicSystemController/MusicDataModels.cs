using System;
using System.Collections.Generic;

namespace flexpod.Devices
{
    /// <summary>
    /// Represents a music artist in the catalog
    /// </summary>
    public class MusicArtist
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int TrackCount { get; set; }

        public override string ToString()
        {
            return string.Format("{0} - {1} ({2} tracks)", Id, Name, TrackCount);
        }
    }

    /// <summary>
    /// Represents a music track in the catalog
    /// </summary>
    public class MusicTrack
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int ArtistId { get; set; }
        public string ArtistName { get; set; }
        public int DurationSeconds { get; set; }

        public override string ToString()
        {
            return string.Format("{0} - {1} by {2}", Id, Name, ArtistName);
        }
    }

    /// <summary>
    /// Current playback status
    /// </summary>
    public class MusicPlaybackStatus
    {
        public bool IsPlaying { get; set; }
        public int CurrentTrackId { get; set; }
        public string CurrentTrackName { get; set; }
        public string CurrentArtistName { get; set; }
        public int RemainingTimeSeconds { get; set; }
        public bool IsConnected { get; set; }

        public string FormattedRemainingTime
        {
            get
            {
                var time = TimeSpan.FromSeconds(RemainingTimeSeconds);
                return string.Format("{0:mm\\:ss}", time);
            }
        }
    }

    /// <summary>
    /// Music catalog browsing result
    /// </summary>
    public class MusicCatalogPage
    {
        public List<MusicArtist> Artists { get; set; } = new List<MusicArtist>();
        public List<MusicTrack> Tracks { get; set; } = new List<MusicTrack>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public bool HasMorePages => (PageNumber * PageSize) < TotalCount;
    }

    /// <summary>
    /// Event arguments for catalog updates
    /// </summary>
    public class MusicCatalogUpdatedEventArgs : EventArgs
    {
        public int TotalArtists { get; set; }
        public int LoadedArtists { get; set; }
        public bool IsComplete => LoadedArtists >= TotalArtists;
    }

    /// <summary>
    /// Event arguments for playback status changes
    /// </summary>
    public class PlaybackStatusChangedEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public int TrackId { get; set; }
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
        public PlaybackChangeReason Reason { get; set; }
    }

    /// <summary>
    /// Reason for playback status change
    /// </summary>
    public enum PlaybackChangeReason
    {
        UserStarted,
        UserStopped,
        TrackFinished,
        Error,
        SystemStop
    }

    /// <summary>
    /// Event arguments for track time updates
    /// </summary>
    public class TrackTimeUpdatedEventArgs : EventArgs
    {
        public int TrackId { get; set; }
        public string TrackName { get; set; }
        public int RemainingTimeSeconds { get; set; }
        public int ElapsedTimeSeconds { get; set; }
        public int TotalTimeSeconds { get; set; }

        public string FormattedRemainingTime
        {
            get
            {
                var time = TimeSpan.FromSeconds(RemainingTimeSeconds);
                return string.Format("{0:mm\\:ss}", time);
            }
        }

        public string FormattedElapsedTime
        {
            get
            {
                var time = TimeSpan.FromSeconds(ElapsedTimeSeconds);
                return string.Format("{0:mm\\:ss}", time);
            }
        }
    }

    /// <summary>
    /// Event arguments for music system errors
    /// </summary>
    public class MusicSystemErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;
    }

    /// <summary>
    /// Error severity levels
    /// </summary>
    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Event arguments for connection events
    /// </summary>
    public class MusicSystemConnectedEventArgs : EventArgs
    {
        public DateTime ConnectedTime { get; set; } = DateTime.Now;
    }

    public class MusicSystemDisconnectedEventArgs : EventArgs
    {
        public DateTime DisconnectedTime { get; set; } = DateTime.Now;
        public string Reason { get; set; }
    }

    /// <summary>
    /// Music system configuration
    /// </summary>
    public class DMSInfo
    {
        /// <summary>
        /// IP address of QuirkyTech DMS server
        /// </summary>
        public string IP { get; set; }

        /// <summary>
        /// Command port for DMS communication
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Local listen port for time feedback
        /// </summary>
        public int ListenPort { get; set; }

        /// <summary>
        /// Connection timeout in milliseconds
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Response timeout in milliseconds
        /// </summary>
        public int ResponseTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Enable detailed debug logging
        /// </summary>
        public bool DebugMode { get; set; } = false;

        /// <summary>
        /// Auto-reconnect on connection loss
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Reconnect delay in milliseconds
        /// </summary>
        public int ReconnectDelayMs { get; set; } = 10000;

        /// <summary>
        /// Maximum reconnection attempts (0 = infinite)
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 0;
    }

    /// <summary>
    /// Search criteria for music browsing
    /// </summary>
    public class MusicSearchCriteria
    {
        public string ArtistNameFilter { get; set; }
        public string TrackNameFilter { get; set; }
        public int? ArtistId { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public bool CaseSensitive { get; set; } = false;
    }
}

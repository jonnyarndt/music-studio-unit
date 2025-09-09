using System;

namespace musicStudioUnit.MusicSystemController
{
    // Music Event Arguments
    public class PlaybackStatusEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
        public PlaybackStatusEventArgs(bool isPlaying, string trackName = "", string artistName = "")
        {
            IsPlaying = isPlaying;
            TrackName = trackName;
            ArtistName = artistName;
        }
    }

    public class PlaybackStatusUpdatedEventArgs : EventArgs
    {
        public PlaybackStatus Status { get; set; }
        public PlaybackStatusUpdatedEventArgs(PlaybackStatus status)
        {
            Status = status;
        }
    }

    public class PlaybackStatusChangedEventArgs : EventArgs
    {
        public PlaybackStatus Status { get; set; }
        public PlaybackStatusChangedEventArgs(PlaybackStatus status)
        {
            Status = status;
        }
    }

    public class TrackTimeEventArgs : EventArgs
    {
        public int CurrentTime { get; set; }
        public int TotalTime { get; set; }
        public TrackTimeEventArgs(int currentTime, int totalTime)
        {
            CurrentTime = currentTime;
            TotalTime = totalTime;
        }
    }

    public class TrackTimeChangedEventArgs : EventArgs
    {
        public int CurrentTime { get; set; }
        public int TotalTime { get; set; }
        public TrackTimeChangedEventArgs(int currentTime, int totalTime)
        {
            CurrentTime = currentTime;
            TotalTime = totalTime;
        }
    }

    public class MusicSystemErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public MusicSystemErrorEventArgs(string errorMessage, Exception ex = null)
        {
            ErrorMessage = errorMessage;
            Exception = ex;
        }
    }

    // Music Models
    public class PlaybackStatus
    {
        public bool IsConnected { get; set; }
        public bool IsPlaying { get; set; }
        public string CurrentTrackName { get; set; } = "";
        public string CurrentArtistName { get; set; } = "";
        public int CurrentTime { get; set; }
        public int TotalTime { get; set; }
        public string FormattedRemainingTime { get; set; } = "0:00";
    }

    public class MusicArtist
    {
        public int ArtistId { get; set; }
        public string Name { get; set; } = "";
        public System.Collections.Generic.List<MusicTrack> Tracks { get; set; } = new System.Collections.Generic.List<MusicTrack>();
    }

    public class MusicTrack
    {
        public int TrackId { get; set; }
        public string Name { get; set; } = "";
        public string ArtistName { get; set; } = "";
        public int Duration { get; set; }
    }

    public class Artist
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public class Track
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
    }
}

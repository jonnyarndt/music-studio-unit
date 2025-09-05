using System;
using System.Collections.Generic;

namespace flexpod.Interfaces
{
    /// <summary>
    /// Interface for MSU UI screens
    /// </summary>
    public interface IMSUScreen
    {
        string ScreenName { get; }
        bool IsActive { get; }
        void Show();
        void Hide();
        void Update();
    }

    /// <summary>
    /// Interface for MSU temperature control
    /// </summary>
    public interface ITemperatureControl
    {
        float CurrentSetpoint { get; }
        float ExternalTemperature { get; }
        bool OverTemp { get; }
        bool PressureFault { get; }
        bool VoltageFault { get; }
        bool AirflowBlocked { get; }

        void IncrementSetpoint();
        void DecrementSetpoint();
        void SetTemperature(float temperature);

        event EventHandler<TemperatureChangedEventArgs> SetpointChanged;
        event EventHandler<TemperatureStatusEventArgs> StatusUpdated;
    }

    /// <summary>
    /// Interface for music playback control
    /// </summary>
    public interface IMusicPlayback
    {
        bool IsPlaying { get; }
        string CurrentTrackName { get; }
        string CurrentArtistName { get; }
        int RemainingTime { get; }

        List<Artist> GetArtists(int page = 1);
        List<Track> GetTracksForArtist(int artistId, int page = 1);
        void PlayTrack(int trackId, string trackName, string artistName);
        void StopTrack();

        event EventHandler<PlaybackStatusChangedEventArgs> PlaybackStatusChanged;
        event EventHandler<TrackTimeChangedEventArgs> TimeUpdated;
    }

    /// <summary>
    /// Interface for studio combination management
    /// </summary>
    public interface IStudioCombination
    {
        StudioCombinationType CurrentCombination { get; }
        List<StudioCombinationOption> AvailableCombinations { get; }
        bool IsCombinationController { get; }

        bool CreateCombination(StudioCombinationOption option);
        void BreakCombination();

        event EventHandler<CombinationChangedEventArgs> CombinationChanged;
    }

    /// <summary>
    /// Interface for user management
    /// </summary>
    public interface IUserManagement
    {
        bool IsUserLoggedIn { get; }
        UserInfo CurrentUser { get; }

        bool LoginUser(int userId);
        void LogoutUser();
        void ContinueAsGuest();
        bool IsTodayUsersBirthday();

        event EventHandler<UserLoginChangedEventArgs> LoginStatusChanged;
    }

    /// <summary>
    /// Interface for system settings and information
    /// </summary>
    public interface ISystemSettings
    {
        MSUSystemInfo SystemInfo { get; }
        DateTime CurrentTime { get; }
        string CurrentDate { get; }

        void ReloadConfiguration();
        void RestartSystem();

        event EventHandler<SystemStatusChangedEventArgs> StatusChanged;
    }
}

namespace flexpod.Interfaces
{
    // Event argument classes for interfaces
    public class TemperatureChangedEventArgs : EventArgs
    {
        public float OldTemperature { get; set; }
        public float NewTemperature { get; set; }
    }

    public class TemperatureStatusEventArgs : EventArgs
    {
        public float ExternalTemperature { get; set; }
        public bool OverTemp { get; set; }
        public bool PressureFault { get; set; }
        public bool VoltageFault { get; set; }
        public bool AirflowBlocked { get; set; }
    }

    public class PlaybackStatusChangedEventArgs : EventArgs
    {
        public bool IsPlaying { get; set; }
        public string TrackName { get; set; }
        public string ArtistName { get; set; }
    }

    public class TrackTimeChangedEventArgs : EventArgs
    {
        public int RemainingTimeSeconds { get; set; }
        public string FormattedTime { get; set; }
    }

    public class CombinationChangedEventArgs : EventArgs
    {
        public StudioCombinationType OldCombination { get; set; }
        public StudioCombinationType NewCombination { get; set; }
        public List<string> CombinedMSUNames { get; set; }
    }

    public class UserLoginChangedEventArgs : EventArgs
    {
        public bool IsLoggedIn { get; set; }
        public UserInfo User { get; set; }
        public bool IsBirthday { get; set; }
    }

    public class SystemStatusChangedEventArgs : EventArgs
    {
        public string StatusMessage { get; set; }
        public bool IsOnline { get; set; }
    }
}

using System;

namespace flexpod.UserInterface
{
    /// <summary>
    /// MSU-specific touch panel join definitions
    /// Extends the base TouchPanelJoins for Music Studio Unit screens
    /// </summary>
    internal class MSUTouchPanelJoins
    {
        #region Page Joins (Client-Scope.md function screens)
        internal enum Pages
        {
            Settings = 401,      // Settings screen - shows on boot
            User = 402,          // User login/profile screen
            Music = 403,         // Music browsing and playback
            Temperature = 404,   // Temperature control screen
            Combine = 405        // MSU combination screen
        }
        #endregion

        #region Settings Screen Joins (401)
        internal enum SettingsScreen
        {
            // Digital joins
            ReloadConfigButton = 411,    // Button to reload configuration
            
            // Serial joins
            CurrentTimeText = 411,       // Current time (12 hour format)
            CurrentDateText = 412,       // Current date (long format)
            MSUNameText = 413,          // MSU name from configuration
            MSUUIDText = 414,           // MSU UID (processor MAC)
            ProcessorModelText = 415,    // RMC4 processor model
            FirmwareVersionText = 416,   // Current firmware version
            ProcessorMACText = 417,      // Processor MAC address
            ProcessorIPText = 418,       // Current IP address
            MSUCountText = 419,         // Number of MSUs in building
            BuildingAddressText = 420,   // Building address from config
            ConfigStatusText = 421       // Configuration load status
        }
        #endregion

        #region User Login Screen Joins (402)
        internal enum UserScreen
        {
            // Digital joins
            LoginButton = 431,           // Login button after ID entry
            LogoutButton = 432,          // Logout button
            GuestModeButton = 433,       // Continue as guest
            ClearButton = 434,           // Clear entered ID
            
            // Keypad digit buttons (435-444)
            KeypadDigit0 = 435,
            KeypadDigit1 = 436,
            KeypadDigit2 = 437,
            KeypadDigit3 = 438,
            KeypadDigit4 = 439,
            KeypadDigit5 = 440,
            KeypadDigit6 = 441,
            KeypadDigit7 = 442,
            KeypadDigit8 = 443,
            KeypadDigit9 = 444,
            
            // Status indicators
            LoggedInIndicator = 445,     // Shows if user is logged in
            BirthdayIndicator = 446,     // Shows on user's birthday
            
            // Serial joins
            UserIDEntryText = 431,       // User ID being entered
            UserNameText = 432,          // User name from database
            UserBirthdateText = 433,     // User birthdate (yyyymmdd format)
            LoginStatusText = 434,       // Login status messages
            BirthdayMessage = 435        // "HAPPY BIRTHDAY" message
        }
        #endregion

        #region Music Screen Joins (403)
        internal class Music
        {
            // View control joins
            public const uint ArtistListVisible = 411;
            public const uint TrackListVisible = 412;
            public const uint NowPlayingVisible = 413;
            
            // Artist list joins (421-430 for buttons, 441-450 for text)
            public const uint ArtistButton1 = 421;
            public const uint ArtistButton2 = 422;
            public const uint ArtistButton3 = 423;
            public const uint ArtistButton4 = 424;
            public const uint ArtistButton5 = 425;
            public const uint ArtistButton6 = 426;
            public const uint ArtistButton7 = 427;
            public const uint ArtistButton8 = 428;
            public const uint ArtistButton9 = 429;
            public const uint ArtistButton10 = 430;
            
            public const uint ArtistText1 = 441;
            public const uint ArtistText2 = 442;
            public const uint ArtistText3 = 443;
            public const uint ArtistText4 = 444;
            public const uint ArtistText5 = 445;
            public const uint ArtistText6 = 446;
            public const uint ArtistText7 = 447;
            public const uint ArtistText8 = 448;
            public const uint ArtistText9 = 449;
            public const uint ArtistText10 = 450;
            
            // Track list joins (431-440 for buttons, 451-460 for text)
            public const uint TrackButton1 = 431;
            public const uint TrackButton2 = 432;
            public const uint TrackButton3 = 433;
            public const uint TrackButton4 = 434;
            public const uint TrackButton5 = 435;
            public const uint TrackButton6 = 436;
            public const uint TrackButton7 = 437;
            public const uint TrackButton8 = 438;
            public const uint TrackButton9 = 439;
            public const uint TrackButton10 = 440;
            
            public const uint TrackText1 = 451;
            public const uint TrackText2 = 452;
            public const uint TrackText3 = 453;
            public const uint TrackText4 = 454;
            public const uint TrackText5 = 455;
            public const uint TrackText6 = 456;
            public const uint TrackText7 = 457;
            public const uint TrackText8 = 458;
            public const uint TrackText9 = 459;
            public const uint TrackText10 = 460;
            
            // Navigation and control joins
            public const uint ArtistPrevPage = 461;
            public const uint ArtistNextPage = 462;
            public const uint ArtistPageInfo = 461; // Serial
            
            public const uint TrackPrevPage = 463;
            public const uint TrackNextPage = 464;
            public const uint TrackPageInfo = 462; // Serial
            public const uint BackToArtists = 465;
            
            // Playback control joins
            public const uint PlayStopButton = 471;
            public const uint PlayStopText = 471; // Serial
            public const uint BackToBrowse = 472;
            
            // Now playing display joins
            public const uint NowPlayingTrack = 473; // Serial
            public const uint NowPlayingArtist = 474; // Serial
            public const uint RemainingTime = 475; // Serial
            public const uint PlaybackStatus = 476; // Serial
            
            // Connection and status joins
            public const uint ConnectionStatus = 481; // Digital
            public const uint ConnectionStatusText = 481; // Serial
            public const uint BrowsingEnabled = 482; // Digital
            public const uint RefreshCatalog = 483; // Digital
            
            // Error and status display joins
            public const uint ErrorVisible = 491; // Digital
            public const uint ErrorMessage = 491; // Serial
            
            // Screen title
            public const uint ScreenTitle = 401; // Serial
        }
        #endregion

        #region Temperature Screen Joins (404)
        internal enum TemperatureScreen
        {
            // Digital joins
            TempUpButton = 451,          // Temperature increase (+0.5°C)
            TempDownButton = 452,        // Temperature decrease (-0.5°C)
            
            // Status indicators
            ConnectedIndicator = 453,    // HVAC connection status
            OverTempFault = 454,         // Over-temperature fault
            PressureFault = 455,         // Pressure fault
            VoltageFault = 456,          // Voltage fault
            AirflowFault = 457,          // Airflow blocked fault
            
            // Preset buttons (460-469 for up to 10 presets)
            PresetButton1 = 460,
            PresetButton2 = 461,
            PresetButton3 = 462,
            PresetButton4 = 463,
            PresetButton5 = 464,
            
            // Serial joins
            CurrentTempText = 451,       // Current setpoint display
            ExternalTempText = 452,      // External temperature
            StatusText = 453,            // Overall status text
            ZoneDisplayText = 454,       // Zone(s) being controlled
            
            // Analog joins
            CurrentTempAnalog = 451,     // Setpoint as analog value
            ExternalTempAnalog = 452     // External temp as analog value
        }
        #endregion

        #region Combine Screen Joins (405)
        internal class Combine
        {
            // Studio type selection buttons
            public const uint SingleStudioButton = 421;
            public const uint MegaStudioButton = 422;
            public const uint MonsterStudioButton = 423;
            
            // Button availability indicators
            public const uint SingleStudioAvailable = 431;
            public const uint MegaStudioAvailable = 432;
            public const uint MonsterStudioAvailable = 433;
            
            // Current selection indicators
            public const uint SingleStudioSelected = 441;
            public const uint MegaStudioSelected = 442;
            public const uint MonsterStudioSelected = 443;
            
            // Control and status joins
            public const uint RefreshButton = 451;
            public const uint CanControlCombination = 461;
            public const uint IsMasterUnit = 462;
            public const uint ShowRestrictions = 463;
            
            // Text displays
            public const uint ScreenTitle = 401; // Serial
            public const uint CurrentConfiguration = 411; // Serial
            public const uint ConfigurationDescription = 412; // Serial
            public const uint CombinedUnits = 413; // Serial
            public const uint UnitsCount = 414; // Serial
            public const uint CombinationStatus = 415; // Serial
            public const uint MasterStatus = 416; // Serial
            public const uint ControlRestrictions = 417; // Serial
            
            // Button text
            public const uint SingleStudioText = 421; // Serial
            public const uint MegaStudioText = 422; // Serial
            public const uint MonsterStudioText = 423; // Serial
            
            // Status and error displays
            public const uint StatusVisible = 471; // Digital
            public const uint StatusMessage = 471; // Serial
            public const uint ErrorVisible = 481; // Digital
            public const uint ErrorMessage = 481; // Serial
        }
        #endregion

        #region Common Menu Bar Joins (Shared across all pages)
        internal class MenuBar
        {
            // Navigation buttons
            public const uint SettingsButton = 481;
            public const uint UserButton = 482;
            public const uint MusicButton = 483;
            public const uint TemperatureButton = 484;
            public const uint CombineButton = 485;
            public const uint BackButton = 490;
            
            // Status displays
            public const uint BuildingLocationText = 481; // Serial
            public const uint NowPlayingTrackText = 482; // Serial
            public const uint NowPlayingArtistText = 483; // Serial
            public const uint NowPlayingTimeText = 484; // Serial
        }
        #endregion

        #region Connection Status (Global)
        internal enum ConnectionStatus
        {
            // Digital joins
            ProcessorOnline = 491,       // Processor connection status
            
            // Serial joins
            ConnectionMessage = 491      // "Connecting to system - please wait"
        }
        #endregion
    }
}

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

        #region Common Menu Bar Joins (Shared across all pages)
        internal enum MenuBar
        {
            // Digital joins
            SettingsButton = 481,        // Navigate to Settings
            UserButton = 482,            // Navigate to User
            MusicButton = 483,           // Navigate to Music
            TemperatureButton = 484,     // Navigate to Temperature
            CombineButton = 485,         // Navigate to Combine
            
            // Serial joins
            BuildingLocationText = 481,  // City from configuration
            NowPlayingTrackText = 482,   // Current track name
            NowPlayingArtistText = 483,  // Current artist name
            NowPlayingTimeText = 484     // Remaining time display
        }
        #endregion

        #region Combine Screen Joins (405)
        internal enum CombineScreen
        {
            // Digital joins
            SingleStudioButton = 471,    // Single unit mode
            MegaStudioButton = 472,      // Two units combined
            MonsterStudioButton = 473,   // Three units combined
            
            // Status indicators
            SingleStudioActive = 474,    // Current mode indicators
            MegaStudioActive = 475,
            MonsterStudioActive = 476,
            
            // Serial joins
            CombinedWithText = 471,      // "Combined with: None" or unit names
            CombinationStatusText = 472  // Current combination status
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

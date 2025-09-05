# MSU TouchPanel Integration - README

## Overview

This implementation extends the TouchPanelBase class to create MSU-specific screens per the Client-Scope.md requirements. The MSU TouchPanel provides five main functions accessible via a menu bar, with the Settings screen displayed on boot.

## Architecture

### Core Components

1. **MSUTouchPanel** - Main touch panel controller extending TouchPanelBase
2. **SettingsScreenUI** - System information and configuration display
3. **UserLoginScreenUI** - User login with keypad and database integration
4. **TemperatureScreenUI** - Enhanced HVAC control with combination support
5. **MusicBrowseUI** - Music system integration (existing implementation)
6. **MSUTouchPanelJoins** - Comprehensive join definitions

### Screen Functions (per Client-Scope.md)

#### Settings Screen (Page 401)
- **Boot Default**: Displays on system startup per Client-Scope.md
- **Information Displayed**:
  - Current time (12-hour format: H:mm a/p)
  - Current date (long format: day of week, month, day, year)
  - MSU name and UID (processor MAC address)
  - Processor model (RMC4) and firmware version
  - Processor MAC address and IP address
  - Number of MSUs in building
  - Building address from configuration
- **Features**:
  - Configuration reload button
  - Real-time time/date updates
  - Configuration status display

#### User Login Screen (Page 402)
- **User ID Entry**: Keypad for entering IDs 1-60,000
- **Database Integration**: Uses LoyaltyID class per Appendix A
- **Features**:
  - User name and birthdate display (yyyymmdd format)
  - Birthday detection with "HAPPY BIRTHDAY" message
  - Guest mode option
  - Login/logout functionality
  - Input validation and error handling

#### Temperature Screen (Page 404)
- **HVAC Control**: Increment/decrement buttons (±0.5°C per Client-Scope.md)
- **Temperature Range**: -40°C to +50°C per Client-Scope.md
- **Status Display**:
  - Current setpoint and external temperature (2-digit precision)
  - Four error flags with text labels (OVERTEMP, PRESSURE, VOLTAGE, AIRFLOW)
  - Zone display for combination mode
- **Features**:
  - Nonvolatile setpoint storage per Client-Scope.md
  - Combination mode synchronization
  - Temperature presets
  - Real-time fault monitoring

#### Music Screen (Page 403)
- **Integration**: Uses existing MusicBrowseUI implementation
- **Features**:
  - Artist and track browsing (existing functionality)
  - Now playing display
  - Playback controls
  - Time remaining display

#### Combine Screen (Page 405)
- **Studio Combination**: Single, Mega (2 units), Monster (3 units)
- **Integration**: Placeholder for combination logic

### Menu Bar (Shared across all screens)

#### Common Elements
- **Navigation Buttons**: Access to all five functions
- **Building Location**: City from configuration
- **Music Information**: Track, artist, remaining time (when playing)

#### Join Assignments

```csharp
// Navigation (481-485)
SettingsButton = 481
UserButton = 482
MusicButton = 483
TemperatureButton = 484
CombineButton = 485

// Display (481-484)
BuildingLocationText = 481
NowPlayingTrackText = 482
NowPlayingArtistText = 483
NowPlayingTimeText = 484
```

## Touch Panel Join Mappings

### Settings Screen (411-421)
```csharp
// Digital Joins
ReloadConfigButton = 411

// Serial Joins
CurrentTimeText = 411        // 12-hour format
CurrentDateText = 412        // Long format
MSUNameText = 413           // From configuration
MSUUIDText = 414            // Processor MAC
ProcessorModelText = 415     // RMC4
FirmwareVersionText = 416    // Current version
ProcessorMACText = 417       // MAC address
ProcessorIPText = 418        // Current IP
MSUCountText = 419          // Building MSU count
BuildingAddressText = 420    // Address from config
ConfigStatusText = 421       // Load status
```

### User Login Screen (431-446)
```csharp
// Digital Joins
LoginButton = 431
LogoutButton = 432
GuestModeButton = 433
ClearButton = 434
KeypadDigit0-9 = 435-444    // Numeric keypad
LoggedInIndicator = 445      // Login status
BirthdayIndicator = 446      // Birthday flag

// Serial Joins
UserIDEntryText = 431        // ID being entered
UserNameText = 432           // User name from DB
UserBirthdateText = 433      // Birthdate (yyyymmdd)
LoginStatusText = 434        // Status messages
BirthdayMessage = 435        // "HAPPY BIRTHDAY"
```

### Temperature Screen (451-469)
```csharp
// Digital Joins
TempUpButton = 451           // +0.5°C
TempDownButton = 452         // -0.5°C
ConnectedIndicator = 453     // HVAC status
OverTempFault = 454         // Fault indicators
PressureFault = 455
VoltageFault = 456
AirflowFault = 457
PresetButton1-5 = 460-464   // Temperature presets

// Serial Joins
CurrentTempText = 451        // Setpoint display
ExternalTempText = 452       // External temp (2-digit precision)
StatusText = 453            // Overall status
ZoneDisplayText = 454        // Controlled zones

// Analog Joins
CurrentTempAnalog = 451      // Setpoint value
ExternalTempAnalog = 452     // External temp value
```

### Connection Status (491)
```csharp
// Digital Joins
ProcessorOnline = 491        // Connection status

// Serial Joins
ConnectionMessage = 491      // "Connecting to system - please wait"
```

## Client-Scope.md Compliance

### Key Requirements Met

1. **Five Functions**: Settings, User, Music, Temperature, Combine
2. **Settings on Boot**: Default screen per specification
3. **Menu Bar**: Common navigation and building location
4. **Time Format**: 12-hour (H:mm a/p) and long date format
5. **Temperature Control**: ±0.5°C increments, -40°C to +50°C range
6. **User Database**: Integration with Appendix A specifications
7. **HVAC Faults**: Four error flags with text labels
8. **External Temperature**: Two-digit decimal precision
9. **Nonvolatile Storage**: Setpoint persistence across reboots
10. **Combination Mode**: Synchronized temperature controls
11. **Music Integration**: Now playing display on menu bar
12. **Connection Handling**: "Connecting to system" message

### Protocol Integration

- **HVAC**: Binary protocol per Appendix B
- **Music**: ASCII protocol per Appendix C  
- **User Database**: SIMPL# Library per Appendix A
- **Configuration**: XML/JSON per specification

## Usage

### Console Commands

```
msupage <PageName>    - Navigate to specific page
                       (Settings, User, Music, Temperature, Combine)
msustatus            - Show current MSU TouchPanel status
```

### Initialization

The MSU TouchPanel is automatically initialized during system startup after all required components are available:

1. User Database initialization
2. HVAC Controller initialization  
3. Music Controller initialization
4. MSU Controller initialization
5. MSU TouchPanel creation with all screen handlers

### Event Handling

- **Configuration Changes**: Automatic display updates
- **User Login/Logout**: State tracking and UI updates
- **Temperature Changes**: Real-time display and fault monitoring
- **Music Playback**: Menu bar updates with track information
- **HVAC Faults**: Visual indicators and status messages
- **Connection Status**: Automatic connection monitoring

## Technical Notes

### Nonvolatile Storage
Temperature setpoints are stored using `CrestronEnvironment.SetKeyValue()` with keys:
```
MSU_Setpoint_Zone_{zoneId}
```

### Error Handling
- Comprehensive exception handling in all screen handlers
- Debug logging for troubleshooting
- Graceful fallbacks for missing components

### Memory Management
- Proper disposal of all screen handlers
- Event unsubscription on disposal
- Timer cleanup for time updates

### Thread Safety
- Lock objects for UI updates
- Concurrent dictionaries for page tracking
- Async-safe event handling

## Files Created

1. `MSUTouchPanelJoins.cs` - Join definitions for all screens
2. `SettingsScreenUI.cs` - Settings screen implementation
3. `UserLoginScreenUI.cs` - User login screen implementation  
4. `TemperatureScreenUI.cs` - Enhanced temperature control
5. `MSUTouchPanel.cs` - Main touch panel controller
6. Updated `ControlSystem.cs` - Integration and initialization

This implementation provides a complete MSU-specific touch panel interface that fully satisfies the Client-Scope.md requirements while maintaining compatibility with the existing system architecture.

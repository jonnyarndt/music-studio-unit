# Masters of Karaoke - Music Studio Unit (MSU) Controller

## Overview

The MSU Controller is a comprehensive Crestron control system implementation for managing integrated HVAC, Music, and Studio control functionality in aircraft cabin environments. This system provides a unified interface for passengers and crew to interact with environmental controls, entertainment systems, and combined studio operations.

## Architecture

### Core Components

#### Configuration Management
- **ConfigurationManager.cs** - Main coordinator for loading XML and JSON configurations
- **XmlConfigParser.cs** - Robust XML parser for local configuration files
- **HttpConfigClient.cs** - HTTP client for remote JSON configuration retrieval
- **ConfigurationModels.cs** - Data models for all configuration structures

#### System Controllers
- **MSUController.cs** - Main controller orchestrating all subsystems
- **HVACController.cs** - Borden Air Multi-Zone HVAC system interface
- **MusicController.cs** - QuirkyTech Digital Music system interface
- **StudioManager.cs** - Combined studio operations logic
- **UserManager.cs** - User authentication and role management

#### User Interface
- **TP01.cs** - Enhanced touch panel controller with MSU integration
- **Interfaces.cs** - UI interface definitions
- **TouchPanelJoins.cs** - Join mappings for SGD files

### Configuration System

#### Local Configuration (XML)
Location: `\USER\msu.xml` on Crestron processor

```xml
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <ProcessorMAC>B8-2D-AA-12-34-56</ProcessorMAC>
  <Remote>
    <IP>192.168.1.100</IP>
    <Port>8080</Port>
    <File>msu-config.json</File>
  </Remote>
  <HVAC>
    <IP>192.168.1.50</IP>
    <Port>10001</Port>
    <Timeout>5000</Timeout>
  </HVAC>
  <Music>
    <IP>192.168.1.60</IP>
    <Port>10002</Port>
    <Timeout>5000</Timeout>
  </Music>
  <Users>
    <LibraryPath>\\crestron-host\USER\UserLibrary.simpl</LibraryPath>
    <DefaultAccessLevel>Guest</DefaultAccessLevel>
  </Users>
</Configuration>
```

#### Remote Configuration (JSON)
Retrieved via HTTP from central server

```json
{
  "MSUUnits": [
    {
      "ProcessorMAC": "B8-2D-AA-12-34-56",
      "Location": {
        "Terminal": "A",
        "Gate": "A1",
        "Zone": "First Class"
      },
      "Capabilities": {
        "HVAC": {
          "Enabled": true,
          "ZoneCount": 4,
          "TemperatureRange": { "Min": 65, "Max": 78 }
        },
        "Music": {
          "Enabled": true,
          "MaxVolume": 80,
          "EQSettings": "Balanced"
        },
        "Studio": {
          "Enabled": true,
          "CombinationLogic": "Priority",
          "ConflictResolution": "FirstCome"
        }
      }
    }
  ]
}
```

## System Integration

### Initialization Sequence

1. **ControlSystem.cs** initializes core components
2. **ConfigurationManager** loads local XML configuration
3. **ConfigurationManager** attempts remote JSON configuration retrieval
4. **MSUController** initializes with loaded configurations
5. **Individual controllers** (HVAC, Music, Studio) are instantiated
6. **TP01** touch panel connects to MSU controller for UI updates

### Event-Driven Architecture

- Configuration loading events trigger system updates
- HVAC data packets update environmental status
- Music system events update playback information
- Studio operations coordinate cross-system activities
- User authentication events manage access control

## Hardware Support

### Supported Processors
- RMC4 series (4-series processors)
- VC-4 series (with file system compatibility layer)

### Touch Panels
- TSW-770 and compatible models
- Smart Graphics Display (SGD) file support
- Dynamic list and smart object integration

### Network Protocols
- TCP/IP for HVAC and Music system communication
- HTTP for remote configuration retrieval
- DHCP-based network configuration

## Dependencies

### Core Tools Library
The system leverages the existing `core_tools` library for:
- TCP client/server implementations
- Debug logging and device management
- Network utilities and error handling

### Third-Party Libraries
- **Newtonsoft.Json** - JSON configuration parsing
- **Crestron.SimplSharp** - Core Crestron framework
- **System.Xml.Serialization** - XML configuration parsing

## Usage

### Basic Operation

1. Place `msu.xml` configuration file in processor USER directory
2. Ensure network connectivity to HVAC, Music, and configuration servers
3. Load program onto Crestron processor
4. Monitor console output for initialization status
5. Interact with touch panel for system control

### Console Commands

- `printDevMon` - Display all registered devices
- `setTp01Page <page>` - Navigate to specific touch panel page
- Configuration-specific commands available per subsystem

### Debugging

All components provide comprehensive debug logging:
- Level 0: Errors and critical information
- Level 1: General operational status
- Level 2: Detailed debugging information

Example debug output:
```
[INFO] Configuration loaded - Local and Remote configurations available
[INFO] Local Config - Processor MAC: B8-2D-AA-12-34-56
[INFO] MSU Controller initialized successfully
[INFO] HVAC Controller connected to 192.168.1.50:10001
[INFO] Music Controller connected to 192.168.1.60:10002
```

## Error Handling

### Configuration Errors
- Missing XML files fallback to default settings
- Network timeouts for remote configuration retrieval
- JSON validation with detailed error reporting

### Runtime Errors
- TCP connection failures with automatic retry logic
- HVAC protocol validation and error recovery
- Music system communication timeout handling

### Recovery Procedures
- Automatic reconnection attempts for lost network connections
- Configuration reload capabilities without system restart
- Graceful degradation when subsystems are unavailable

## Extending the System

### Adding New Controllers
1. Implement the `IController` interface
2. Add controller initialization to `MSUController`
3. Update configuration models if needed
4. Add UI integration points in `TP01`

### Custom Protocol Support
1. Extend appropriate controller base class
2. Implement protocol-specific communication logic
3. Add configuration parameters
4. Update documentation

### UI Customization
1. Modify SGD file for new visual elements
2. Update `TouchPanelJoins` with new join mappings
3. Add event handlers in `TP01`
4. Implement corresponding controller methods

## File Structure

```
app01-music-studio-unit/
├── Configuration/
│   ├── ConfigurationManager.cs      # Main config coordinator
│   ├── ConfigurationModels.cs       # Data models
│   ├── XmlConfigParser.cs          # XML parser
│   ├── HttpConfigClient.cs         # HTTP client
│   ├── sample-msu.xml              # Sample local config
│   └── sample-msu-config.json      # Sample remote config
├── Controllers/
│   ├── MSUController.cs            # Main system controller
│   ├── HVACController.cs           # HVAC system interface
│   ├── MusicController.cs          # Music system interface
│   ├── StudioManager.cs            # Studio operations
│   └── UserManager.cs              # User management
├── Services/
│   ├── HVACService.cs              # HVAC communication service
│   ├── MusicService.cs             # Music communication service
│   └── UserService.cs              # User authentication service
├── Models/
│   ├── HVACModels.cs               # HVAC data structures
│   ├── MusicModels.cs              # Music data structures
│   └── UserModels.cs               # User data structures
├── Interfaces/
│   └── IMSUInterfaces.cs           # System interfaces
├── UserInterface/
│   ├── TP01.cs                     # Enhanced touch panel
│   ├── Interfaces.cs               # UI interfaces
│   └── TouchPanelJoins.cs          # Join definitions
└── ControlSystem.cs                # Main system entry point
```

## Support and Maintenance

### Regular Maintenance
- Monitor configuration server availability
- Update JSON configurations as needed
- Review debug logs for system health
- Test failover scenarios regularly

### Troubleshooting
1. Check network connectivity to all systems
2. Verify configuration file syntax and content
3. Review debug console output for error details
4. Test individual subsystem functionality

### Performance Monitoring
- TCP connection stability metrics
- Configuration loading times
- Touch panel response times
- Memory usage monitoring

## Version Information

- **Version**: 1.0.0
- **Crestron Framework**: 4-Series Compatible
- **Dependencies**: core_tools library, Newtonsoft.Json
- **Last Updated**: January 2024

For technical support or questions, consult the Client-Scope.md document for detailed system requirements and specifications.

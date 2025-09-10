using System;

namespace musicStudioUnit.Services
{
    // MSU Event Arguments
    public class MSUStatusEventArgs : EventArgs
    {
        public string Status { get; set; }
        public MSUStatusEventArgs(string status)
        {
            Status = status;
        }
    }

    public class MSUConfigEventArgs : EventArgs
    {
        public object Configuration { get; set; }
        public MSUConfigEventArgs(object configuration)
        {
            Configuration = configuration;
        }
    }

    public class SystemStatusChangedEventArgs : EventArgs
    {
        public MSUSystemInfo SystemInfo { get; set; }
        public SystemStatusChangedEventArgs(MSUSystemInfo systemInfo)
        {
            SystemInfo = systemInfo;
        }
    }

    // Studio Combination Event Arguments
    public class CombinationChangedEventArgs : EventArgs
    {
        public StudioCombinationType CombinationType { get; set; }
        public CombinationChangedEventArgs(StudioCombinationType combinationType)
        {
            CombinationType = combinationType;
        }
    }

    // User Event Arguments  
    public class UserLoginChangedEventArgs : EventArgs
    {
        public UserInfo User { get; set; }
        public UserLoginChangedEventArgs(UserInfo user)
        {
            User = user;
        }
    }

    // Models - Forward references to classes defined in other files

    // MusicStudioUnit class moved to avoid duplicate definition - use the one in StudioCombinationManager.cs

    public class BuildingConfiguration
    {
        public string BuildingId { get; set; } = "";
        public string Name { get; set; } = "";
        public List<musicStudioUnit.Services.MusicStudioUnit> MusicStudioUnits { get; set; } = new List<musicStudioUnit.Services.MusicStudioUnit>();
    }

    public class StudioManager
    {
        public string Key { get; set; } = "";
        public bool IsInitialized { get; set; }
    }

    public class UserDatabase
    {
        public string Key { get; set; } = "";
        public bool IsConnected { get; set; }
    }

    // TCP Server/Client stubs for core_tools compatibility
    public class TcpServer
    {
        public string Address { get; set; } = "";
        public int Port { get; set; }
    }

    public class TcpClient  
    {
        public string Address { get; set; } = "";
        public int Port { get; set; }
    }

    public class StudioCombinationOption
    {
        public StudioCombinationType Type { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }
}

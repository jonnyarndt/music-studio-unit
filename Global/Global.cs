using Crestron.SimplSharpPro;
using System;

namespace musicStudioUnit
{
    /// <summary>
    /// Global class of static variables and events
    /// </summary>
    internal static class Global
    {
        internal static CrestronControlSystem ControlSystem { get; set; }  
        internal static DigitalIO DIO { get; set; }
        internal static string FilePathPrefix { get; private set; }
        internal static event EventHandler<BoolChangeEventArgs> OccupiedStateChangeEvent;
        private static bool _Occupied;
        internal static bool Occupied
        {
            get { return _Occupied; }
            set
            {
                if (_Occupied == value) return;
                _Occupied = value;
                OccupiedStateChangeEvent?.Invoke(null, new BoolChangeEventArgs { Item = value });
            }
        }
        internal static string MemberName { get; set; }
        public static char DirectorySeparator { get { return System.IO.Path.DirectorySeparatorChar; } } // Returns the directory separator character based on the running OS
        public static string ApplicationDirectoryPathPrefix { get { return Crestron.SimplSharp.CrestronIO.Directory.GetApplicationDirectory(); } } // The file path prefix to the applciation directory
    }

    /// <summary>
    /// Generic event args for single bool change event.
    /// </summary>
    internal class BoolChangeEventArgs: EventArgs
    {
        internal bool Item { get; set;  }
    }    
}

using core_tools;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronDataStore;
using Crestron.SimplSharpPro;

namespace musicStudioUnit
{
    /// <summary>
    /// Global class of static variables and events
    /// </summary>
    internal static class Global
    {
        internal static CrestronControlSystem? ControlSystem { get; set; }  
        internal static DigitalIO? DIO { get; set; }
        internal static string? FilePathPrefix { get; private set; }
        internal static event EventHandler<BoolChangeEventArgs>? OccupiedStateChangeEvent;
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
        internal static string? MemberName { get; set; }
        /// <summary>
        /// Gets or sets the Platform
        /// </summary>
       public static eDevicePlatform Platform { get { return CrestronEnvironment.DevicePlatform; } }
        /// <summary>
        /// Gets or sets the ProcessorSeries with error handling
        /// </summary>
        public static eCrestronSeries ProcessorSeries
        {
            get
            {
                try
                {
                    return CrestronEnvironment.ProgramCompatibility;
                }
                catch (Exception ex)
                {
                    CrestronConsole.PrintLine("Error determining ProgramCompatibility: {0}", ex.Message);
                    return default(eCrestronSeries);
                }
            }
        }
        public static char DirectorySeparator { get { return Path.DirectorySeparatorChar; } } // Returns the directory separator character based on the running OS

        static string _AssemblyVersion = string.Empty;

        /// <summary>
        /// Gets the Assembly Version of Essentials
        /// </summary>
        /// <returns>The Assembly Version at Runtime</returns>
        public static string AssemblyVersion
        {
            get
            {
                return _AssemblyVersion;
            }
            private set
            {
                _AssemblyVersion = value;
            }
        }

        /// <summary>
        /// Sets the Assembly version to the version of the Essentials Library
        /// </summary>
        /// <param name="assemblyVersion"></param>
        /// <summary>
        /// SetAssemblyVersion method
        /// </summary>
        public static void SetAssemblyVersion(string assemblyVersion)
        {
            AssemblyVersion = assemblyVersion;
        }

        /// <summary>
        /// Sets the file path prefix
        /// </summary>
        /// <param name="prefix"></param>
        public static void SetFilePathPrefix(string prefix)
        {
            FilePathPrefix = prefix;
        }

        static Global()
        {
            try
            {
                CrestronDataStore.CDS_ERROR cDS_ERROR = CrestronDataStoreStatic.InitCrestronDataStore();
                if (cDS_ERROR != 0)
                {
                    CrestronConsole.PrintLine("Error starting CrestronDataStoreStatic: {0}", cDS_ERROR);
                }
            }
            catch (Exception ex)
            {
                CrestronConsole.PrintLine("Exception in InitCrestronDataStore: {0}", ex.Message);
            }
        }
    }

    /// <summary>
    /// Generic event args for single bool change event.
    /// </summary>
    internal class BoolChangeEventArgs: EventArgs
    {
        internal bool Item { get; set;  }
    }   
}

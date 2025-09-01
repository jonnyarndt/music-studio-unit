using core_tools;
using Crestron.SimplSharp;
using System;

namespace flexpod
{
    internal class FlightTelemetry : IKeyName, IDisposable
    {
        #region strings and properties
        private bool alreadyDisposed = false;
        public string Key { get; protected set; }
        public string Name { get; protected set; }
        internal string Altitude { get; private set; }
        internal string VerticalDirection { get; private set; }
        internal string VerticalSpeed { get; private set; }
        internal string CompassDirection { get; private set; }
        internal string Airspeed { get; private set; }
        internal string Temperature { get; private set; }
        internal string ElapsedTime { get; private set; }
        internal string TimeToDestination { get; private set; }
        internal string UtcDate { get; private set; }
        internal string UtcMinute { get; private set; }
        internal string Latitude { get; private set; }
        internal string Longitude { get; private set; }
        internal string CabinPressureOxygenStatus { get; private set; }
        internal string FastenSeatbeltsStatus { get; private set; }
        internal string InFlightStatus { get; private set; }
        internal string CabinSecuredStatus { get; private set; }
        internal string CrewAnnouncementInProgressStatus { get; private set; }
        internal string Bit6FutureUseStatus { get; private set; }
        internal string Bit7FutureUseStatus { get; private set; }
        internal string Bit8FutureUseStatus { get; private set; }
        internal bool CabinPressureOxygenFlag;
        internal bool FastenSeatbeltsFlag;
        internal bool InFlightFlag;
        internal bool CabinSecuredFlag;
        internal bool CrewAnnouncementInProgressFlag;
        private Core_tools_tcpServer TcpServer { get; set; }
        internal event EventHandler DataReceived;
        internal event EventHandler CrewAnnouncementIsInProgress;
        #endregion

        /// <summary>
        /// constructor for FlightTelemetry
        /// </summary>
        internal FlightTelemetry(string keyId, string friendlyId)
        {
            Debug.Console(2, this, "FlightTelemetry constructor called");
            DeviceManager.AddDevice("flightTelemetry", this);
            ErrorLog.Notice("FlightTelemetry added DeviceManager");
            this.Key = keyId;
            ErrorLog.Notice("FlightTelemetry Key = {0}", keyId);
            this.Name = friendlyId;
            ErrorLog.Notice("FlightTelemetry friendlyId = {0}", friendlyId);

            ResetFlightTelemetryInfo();

            TcpServer = new Core_tools_tcpServer("ft-tcpServer", "flightTelemtry-tcpServer", 53001);
            ErrorLog.Notice("FlightTelemetry Core-tools-tcpServer created");

            if (TcpServer.DataReceivedObservable != null)
            {
                // Subscribe to the observable for data received
                IDisposable subscription = TcpServer.DataReceivedObservable.Subscribe(data =>
                {
                    // Handle the received data in the application layer
                    HandleIncomingData(data);
                });
            }
            else { ErrorLog.Notice("FlightTelemetry DataReceivedObservable is null"); }
        }

        /// <summary>
        /// Dispose of Class
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Part of the implementation of the IDisposable pattern
        /// </summary>
        /// <param name="disposing">indicates whether the method is being called from the Dispose method (with disposing set to true) or from the finalizer (with disposing set to false).</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!alreadyDisposed)
            {
                if(disposing)
                {
                    // Unregister TCP Server
                    TcpServer.Dispose();
                    // Unregister event handlers
                    DataReceived = null;
                    CrewAnnouncementIsInProgress = null;

                    // Remove from DeviceManager if necessary
                    if (DeviceManager.ContainsKey(Key))
                    {
                        DeviceManager.RemoveDevice(Key);
                    }

                    // Dispose other managed resources here
                    // Example: if you have other IDisposable fields, dispose them here
                }
            }

            // Dispose unmanaged resources here, meaning Release them explicitly (e.g., close file handles, free memory)
            // Unmanaged Resources would include:
            // - COM objects that are not part of the .NET Framework
            // - Pointers to blocks of memory created using local memory allocation
            // - Handles to OS resources such as files, devices, GDI objects, etc.
            // - Subscriptions to external system events
            // - Anything that implements IDisposable
            // - Network connections, database connections
            alreadyDisposed = true;
        }

        /// <summary>
        /// Destructor for FlightTelemetry. Ensures unmanaged resources are released if the object is finalized.
        /// </summary>
        /// <comment>
        /// Method calls the local protected virtual void `Dispose` method
        /// </comment>
        ~FlightTelemetry() { Dispose(false); }

        /// <summary>
        /// Parse incoming data from FlightTelemetry
        /// </summary>
        /// <param name="data">32 bytes from FlightTelemetry</param>
        internal void HandleIncomingData(byte[] data)
        {
            // Valid PacketSender Hex below:
            // "\02\t\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\02\03\r"

            // Check if incoming data length is exactly 32 bytes
            if (data.Length != 32)
            {
                Debug.Console(1, this, "Key: {0}, HandleIncomingData, Invalid data length. Expected 32 bytes. Received {1} bytes.", Key, data.Length.ToString());
                return;
            }

            // Verify the header (first 2 bytes). Remember that byte[0] is the first byte.
            if (data[0] != 0x02 || data[1] != 0x09)
            {
                Debug.Console(1, this, "Key: {0}, HandleIncomingData, Invalid header. Valid 2 byte header = 0x02 0x09", Key);
                return;
            }

            // Verify the footer (last 2 bytes)
            if (data[30] != 0x03 || data[31] != 0x0D)
            {
                Debug.Console(1, this, "Key: {0}, Invalid footer. Valud footer = 0x03 0x0D", Key);
                return;
            }

            // Extract UINT value from 0-60,000 (next 2 bytes)
            ushort altitudeValue = BitConverter.ToUInt16(data, 2);
            Altitude = altitudeValue.ToString();

            // Check data[4] byte value
            if (data[4] == 0x31)
            { VerticalDirection = "Up"; }
            else if (data[4] == 0x32)
            { VerticalDirection = "Down"; }
            else
            { VerticalDirection = "Unknown"; }

            // Extract UINT value from 0-6,000 (next 2 bytes)
            uint verticalSpeedValue = BitConverter.ToUInt16(data, 5);
            VerticalSpeed = verticalSpeedValue.ToString();

            // Extract compass value (0.00 to 359.99 multiplied by 100, next 2 bytes)
            uint compassValue = BitConverter.ToUInt16(data, 7);
            CompassDirection = (compassValue / 100.0).ToString("F2");

            // Extract UINT value from 0-600 (next 2 bytes)
            uint airspeedValue = BitConverter.ToUInt16(data, 9);
            Airspeed = airspeedValue.ToString();

            // Extract temperature value (-100.0 degrees Celsius to +50.0 degrees Celsius, next 2 bytes)
            ushort temperatureValue = (ushort)(BitConverter.ToUInt16(data, 11) - 1000);
            Temperature = temperatureValue.ToString("F1" + " °C");

            // Extract time in seconds (0-86,400, next 3 bytes)
            int elapsedTimeInSeconds = BitConverter.ToInt32(data, 13);
            ElapsedTime = TimeSpan.FromSeconds(elapsedTimeInSeconds).ToString(@"hh\:mm\:ss");

            // Extract time in seconds (0-86,400, next 3 bytes)
            int timeToDestinationInSeconds = BitConverter.ToInt32(data, 16);
            TimeToDestination = TimeSpan.FromSeconds(timeToDestinationInSeconds).ToString(@"hh\:mm\:ss");

            // Extract UTC Date (number of days past January 1, 2010, next 2 bytes)
            ushort utcDateValue = BitConverter.ToUInt16(data, 19);
            DateTime UtcDateConverted = new DateTime(2010, 1, 1).AddDays(utcDateValue).Date;
            UtcDate = UtcDateConverted.ToString("MM/dd/yyyy");

            // Extract UTC minute (current minute of the day in a UINT value from 0-1,440, next 2 bytes)
            ushort utcMinuteValue = BitConverter.ToUInt16(data, 21);
            DateTime UtcMinuteConverted = new DateTime(2010, 1, 1).AddMinutes(utcMinuteValue);
            UtcMinute = UtcMinuteConverted.ToString("HH:mm:ss");

            // Extract latitude location (decimal notation, next 3 bytes)
            int latitudeValue = BitConverter.ToInt32(data, 23) - 900000;
            Latitude = latitudeValue.ToString("F4");

            // Extract longitude location (decimal notation, next 3 bytes)
            int longitudeValue = BitConverter.ToInt32(data, 26) - 1800000;
            Longitude = longitudeValue.ToString("F4");

            // Extract status flag (next 1 byte)
            byte statusFlag = data[29];

            // Process the extracted data printing data to console
            Debug.Console(2, this, $"\nAltitude: {altitudeValue}");
            Debug.Console(2, this, $"Vertical Direction: {VerticalDirection}");
            Debug.Console(2, this, $"Vertical Speed: {verticalSpeedValue}");
            Debug.Console(2, this, $"Compass: {compassValue / 100.0:F2}");
            Debug.Console(2, this, $"Airspeed: {airspeedValue}");
            Debug.Console(2, this, $"Temperature: {temperatureValue / 10.0:F1}°C");
            Debug.Console(2, this, $"Elapsed time in seconds: {elapsedTimeInSeconds}");
            Debug.Console(2, this, $"Time to destination in seconds: {timeToDestinationInSeconds}");
            Debug.Console(2, this, $"UTC Date: {utcDateValue}");
            Debug.Console(2, this, $"UTC Minute: {utcMinuteValue}");
            Debug.Console(2, this, $"Latitude: {latitudeValue / 10000.0:F4}°");
            Debug.Console(2, this, $"Longitude: {longitudeValue / 10000.0:F4}°\n");

            // Check status flag for individual bits
            for (int i = 0; i < 8; i++)
            {
                byte bitValue = (byte)((statusFlag >> i) & 0x01);
                Debug.Console(2, this, $"Bit {i + 1}: {bitValue}");
            }

            // Interpret the individual bits in the status flag
            bool cabinPressureOxygen = (statusFlag & 0x01) == 0x01;
            bool fastenSeatbelts = (statusFlag & 0x02) == 0x02;
            bool inFlight = (statusFlag & 0x04) == 0x04;
            bool cabinSecured = (statusFlag & 0x08) == 0x08;
            bool crewAnnouncementInProgress = (statusFlag & 0x10) == 0x10;
            bool bit6FutureUse = (statusFlag & 0x20) == 0x20;
            bool bit7FutureUse = (statusFlag & 0x40) == 0x40;
            bool bit8FutureUse = (statusFlag & 0x80) == 0x80;

            // Interpret the status flag booleans
            CabinPressureOxygenStatus = cabinPressureOxygen ? "Fault" : "OK";
            FastenSeatbeltsStatus = fastenSeatbelts ? "Required" : "Not Required";
            InFlightStatus = inFlight ? "In Flight" : "On Ground";
            CabinSecuredStatus = cabinSecured ? "Door Secured" : "Door Open";
            CrewAnnouncementInProgressStatus = crewAnnouncementInProgress ? "Announcement" : "No Announcement";
            Bit6FutureUseStatus = bit6FutureUse ? "Fault" : "OK";
            Bit7FutureUseStatus = bit7FutureUse ? "Fault" : "OK";
            Bit8FutureUseStatus = bit8FutureUse ? "Fault" : "OK";

            // Set the status flag booleans
            CabinPressureOxygenFlag = cabinPressureOxygen;
            FastenSeatbeltsFlag = fastenSeatbelts;
            InFlightFlag = inFlight;
            CabinSecuredFlag = cabinSecured;
            CrewAnnouncementInProgressFlag = crewAnnouncementInProgress;

            Debug.Console(2, this, "\n***Telemtry Status Flag Results***");
            Debug.Console(2, this, $"CabinPressureOxygenStatus is           {(cabinPressureOxygen ? "Fault" : "Ok")}");
            Debug.Console(2, this, $"FastenSeatbeltsStatus is               {(fastenSeatbelts ? "Required" : "Not Required")}");
            Debug.Console(2, this, $"InFlightStatus is                      {(inFlight ? "In Flight" : "On Ground")}");
            Debug.Console(2, this, $"CabinSecuredStatus is                  {(cabinSecured ? "Door Secured" : "Door Open")}");
            Debug.Console(2, this, $"CrewAnnouncementInProgressStatus is    {(crewAnnouncementInProgress ? "Announcement" : "No Announcement")}");
            Debug.Console(2, this, $"Bit6FutureUseStatus is                 {(bit6FutureUse ? "Fault" : "Ok")}");
            Debug.Console(2, this, $"Bit7FutureUseStatus is                 {(bit7FutureUse ? "Fault" : "Ok")}");
            Debug.Console(2, this, $"Bit8FutureUseStatus is                 {(bit8FutureUse ? "Fault" : "Ok")}");
            Debug.Console(2, this, "***Telemtry Status Flag Results Finished***\n");

            // Raise event to notify subscribers that FlightTelemetry data has been received
            DataReceived?.Invoke(this, new EventArgs());
            // Raise event to nofiy subscribers that a crew announcement is in progress
            if (crewAnnouncementInProgress) { CrewAnnouncementIsInProgress?.Invoke(this, new EventArgs()); }
        }

        /// <summary>
        /// Set all FlightTelemetry info to default values
        /// </summary>
        internal void ResetFlightTelemetryInfo()
        {
            Altitude = "TBD";
            VerticalDirection = "TBD";
            VerticalSpeed = "TBD";
            CompassDirection = "TBD";
            Airspeed = "TBD";
            Temperature = "TBD";
            ElapsedTime = "TBD";
            TimeToDestination = "TBD";
            UtcDate = "TBD";
            UtcMinute = "TBD";
            Latitude = "TBD";
            Longitude = "TBD";
            CabinPressureOxygenStatus = "TBD";
            FastenSeatbeltsStatus = "TBD";
            InFlightStatus = "TBD";
            CabinSecuredStatus = "TBD";
            CrewAnnouncementInProgressStatus = "No Announcement";
        }
    }
}

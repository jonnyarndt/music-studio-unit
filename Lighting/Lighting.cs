using System;
using System.Net.Sockets;
using System.Text;
using core_tools;
using Crestron.SimplSharp.CrestronSockets;

namespace musicStudioUnit
{
    // Mathew 5: 14-16 (ESV)
    // 14 You are the light of the world. A city set on a hill cannot be hidden.
    // 15 Nor do people light a lamp and put it under a basket, but on a stand, and it gives light to all in the house.
    // 16 In the same way, let your light shine before others, so that[b] they may see your good works and give glory 
    // to your Father who is in heaven. <summary>
    // Mathew 5: 14-16 (ESV) 

    /// <summary>
    /// Lighting class, connects to TCP/IP client and sends data
    /// </summary>
    internal class Lighting : IKeyName, IDisposable
    {
        private bool alreadyDisposed;
        public string Key { get; protected set; }
        public string Name { get; protected set; }

        Core_tools_tcpClient client;

        /// <summary>
        /// Default constructor for Lighting. Requires string keyId and string friendlyId.
        /// </summary>
        /// <param name="keyId">Client ID or key (example: lighting1)</param>
        /// <param name="friendlyId">Client friendly name (example: intenserfulLighting)</param>
        public Lighting(string keyId, string friendlyId)
        {
            DeviceManager.AddDevice(Key, this);
            this.Key = keyId;
            this.Name = friendlyId;
            using (client = new Core_tools_tcpClient(Key, Name, "192.168.1.255", "5000")) { };
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
                if (disposing)
                {
                    // Unregister TCP Server
                    client.Dispose();

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
        ~Lighting() { Dispose(false); }

        /// <summary>
        /// Recall lighting scenes. Requires a "#" prefix character followed by 4 bytes (2-digit flexpod index, RR, GG, BB). 
        /// </summary>
        /// <param name="message"></param>
        public void SendData(string message)
        {
            // Example message: #A1RRGGBB
            try
            {
                // If the message is empty, return out of the method
                if (message.Length.Equals(0))
                {
                    Debug.Console(2, this, "SendData called. However, no data found within message");
                    return;
                }

                // If the message doesn't contain a '#' character, return out of the method
                if (message.Contains("#") == false)
                {
                    // If the message does not contain a '#' character, return
                    Debug.Console(2, this, "No '#' character found in message");
                    return;
                }

                // If the TcpIp client is null, return out of the method
                if (client == null)
                {
                    Debug.Console(2, this, "TcpIp client is null");
                    return;
                }

                // Instantiate a new StringBuilder object
                StringBuilder dataBuilder = new StringBuilder();
                 
                // Add the fixed 4-byte header to the data
                dataBuilder.Append("414A1002");
                // Add a fixed 1-byte header to the data of the "#" character
                dataBuilder.Append("#");
                // Split the message into individual grouped messages using the '#' separator. Separated message does not contain the '#' character.
                string[] groupedMessages = message.Split('#');

                // Iterate over each grouped message
                foreach (string groupedMessage in groupedMessages)
                {
                    // Trim any leading/trailing whitespaces from the grouped message
                    string trimmedMessage = groupedMessage.Trim();

                    byte[] hexBytes = ConvertAsciiToHexBytes(trimmedMessage);
                    // Add the message to the data
                    dataBuilder.Append(hexBytes);
                    // Add the '#' character back into the data
                    dataBuilder.Append("#");
                }

                // Add a carriage return at the end of the message
                dataBuilder.Append('\r');

                // Convert the dataBuilder to a string and send to console
                string data = dataBuilder.ToString();
                Debug.Console(2, this, "SendData raw: {0}", data);

                // Convert the data string to ASCII bytes
                byte[] dataBytes = Encoding.ASCII.GetBytes(data);

                // Send data to console then send the data to the server
                Debug.Console(2, this, "Sending ASCII data: {0}", data);
                NetworkStream stream = client.GetStream();
                stream.Write(dataBytes, 0, dataBytes.Length);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Device with Key: {0} exception: {1}", Key, ex);
                return;
            }                        
        }

        /// <summary>
        /// Convert ASCII string to hexadecimal bytes
        /// </summary>
        /// <param name="asciiString"></param>
        /// <returns>HexByte array of 4 bytes</returns>
        /// <exception cref="ArgumentException"></exception>
        internal byte[] ConvertAsciiToHexBytes(string asciiString)
        {
            // Check if the input string has exactly 8 characters
            if (asciiString.Length != 8)
            {
                Debug.Console(0, this, "ConvertAsciiToHexBytes input string must have exactly 8 ASCII characters. Total count provided = {0}", asciiString.Length);
                return new byte[0];
            }

            try 
            {
                byte[] asciiBytes = Encoding.ASCII.GetBytes(asciiString);
                byte[] hexBytes = new byte[4];

                // Convert each ASCII byte to its hexadecimal representation
                for (int i = 0; i < 4; i++)
                {
                    hexBytes[i] = (byte)((asciiBytes[i * 2] - 48) * 16 + (asciiBytes[i * 2 + 1] - 48));
                }

                return hexBytes;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "ConvertAsciiToHexBytes exception: {0}", ex);
                return new byte[0];
            }
            
        }
    }
}

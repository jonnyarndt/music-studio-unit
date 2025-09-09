using System;
using System.Net.Sockets;
using Crestron.SimplSharp;

namespace musicStudioUnit.Devices
{
    /// <summary>
    /// Specialized TCP client for HVAC binary protocol communication
    /// Extends basic TCP functionality with binary data handling
    /// </summary>
    public class HVACTcpClient : IDisposable
    {
        private readonly string _key;
        private readonly string _host;
        private readonly int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private byte[] _receiveBuffer = new byte[1024];
        private bool _isConnected;
        private readonly object _lockObject = new object();

        // Events
        public event EventHandler<HVACDataReceivedEventArgs> DataReceived;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

        public bool IsConnected 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _isConnected && _client?.Connected == true;
                }
            } 
        }

        public HVACTcpClient(string key, string host, int port)
        {
            _key = key;
            _host = host;
            _port = port;
            Debug.Console(1, "HVACTcpClient created for {0}:{1}", host, port);
        }

        /// <summary>
        /// Connect to HVAC system
        /// </summary>
        public bool Connect()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_isConnected)
                    {
                        Debug.Console(1, "HVACTcpClient already connected");
                        return true;
                    }

                    Debug.Console(1, "HVACTcpClient connecting to {0}:{1}", _host, _port);

                    _client = new TcpClient();
                    _client.Connect(_host, _port);
                    _stream = _client.GetStream();
                    _isConnected = true;

                    // Start async receive
                    BeginReceive();

                    Debug.Console(1, "HVACTcpClient connected successfully");
                    Connected?.Invoke(this, EventArgs.Empty);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "HVACTcpClient connection error: {0}", ex.Message);
                    Cleanup();
                    return false;
                }
            }
        }

        /// <summary>
        /// Disconnect from HVAC system
        /// </summary>
        public void Disconnect()
        {
            lock (_lockObject)
            {
                Debug.Console(1, "HVACTcpClient disconnecting");
                Cleanup();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Send binary data to HVAC system
        /// </summary>
        public bool SendBinaryData(byte[] data)
        {
            lock (_lockObject)
            {
                try
                {
                    if (!_isConnected || _stream == null)
                    {
                        Debug.Console(0, "HVACTcpClient not connected - cannot send data");
                        return false;
                    }

                    Debug.Console(2, "HVACTcpClient sending {0} bytes: {1}", 
                        data.Length, BitConverter.ToString(data));

                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();

                    Debug.Console(2, "HVACTcpClient data sent successfully");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "HVACTcpClient send error: {0}", ex.Message);
                    Cleanup();
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    return false;
                }
            }
        }

        /// <summary>
        /// Begin async receive operation
        /// </summary>
        private void BeginReceive()
        {
            try
            {
                if (_stream != null && _isConnected)
                {
                    _stream.BeginRead(_receiveBuffer, 0, _receiveBuffer.Length, OnDataReceived, null);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "HVACTcpClient BeginReceive error: {0}", ex.Message);
                Cleanup();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Handle received data
        /// </summary>
        private void OnDataReceived(IAsyncResult result)
        {
            try
            {
                if (_stream == null || !_isConnected)
                    return;

                int bytesRead = _stream.EndRead(result);

                if (bytesRead > 0)
                {
                    // Copy received data
                    byte[] receivedData = new byte[bytesRead];
                    Array.Copy(_receiveBuffer, receivedData, bytesRead);

                    Debug.Console(2, "HVACTcpClient received {0} bytes: {1}", 
                        bytesRead, BitConverter.ToString(receivedData));

                    // Fire data received event
                    DataReceived?.Invoke(this, new HVACDataReceivedEventArgs
                    {
                        Data = receivedData
                    });

                    // Continue receiving
                    BeginReceive();
                }
                else
                {
                    // Connection closed
                    Debug.Console(1, "HVACTcpClient connection closed by remote host");
                    Cleanup();
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "HVACTcpClient receive error: {0}", ex.Message);
                Cleanup();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Cleanup resources
        /// </summary>
        private void Cleanup()
        {
            try
            {
                _isConnected = false;

                if (_stream != null)
                {
                    _stream.Close();
                    _stream.Dispose();
                    _stream = null;
                }

                if (_client != null)
                {
                    _client.Close();
                    _client = null;
                }

                Debug.Console(2, "HVACTcpClient cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "HVACTcpClient cleanup error: {0}", ex.Message);
            }
        }

        public void Dispose()
        {
            Cleanup();
        }
    }

    /// <summary>
    /// Event arguments for HVAC data received
    /// </summary>
    public class HVACDataReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; set; }
    }

    /// <summary>
    /// Event arguments for HVAC status updates
    /// </summary>
    public class HVACStatusUpdatedEventArgs : EventArgs
    {
        public float ExternalTemperature { get; set; }
        public bool OverTemp { get; set; }
        public bool PressureFault { get; set; }
        public bool VoltageFault { get; set; }
        public bool AirflowBlocked { get; set; }
        public byte StatusFlags { get; set; }
    }

    /// <summary>
    /// Event arguments for setpoint changes
    /// </summary>
    public class HVACSetpointChangedEventArgs : EventArgs
    {
        public byte ZoneId { get; set; }
        public float Temperature { get; set; }
    }

    /// <summary>
    /// Event arguments for HVAC errors
    /// </summary>
    public class HVACErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Event arguments for connection events
    /// </summary>
    public class HVACConnectedEventArgs : EventArgs { }
    public class HVACDisconnectedEventArgs : EventArgs { }

    /// <summary>
    /// HVAC status data structure
    /// </summary>
    public class HVACStatus
    {
        public float CurrentSetpoint { get; set; }
        public float ExternalTemperature { get; set; }
        public bool OverTemp { get; set; }
        public bool PressureFault { get; set; }
        public bool VoltageFault { get; set; }
        public bool AirflowBlocked { get; set; }
        public bool IsConnected { get; set; }
        public System.Collections.Generic.Dictionary<byte, float> ZoneSetpoints { get; set; }
    }
}

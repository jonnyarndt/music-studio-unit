using PepperDash.Core;
using System;
using System.Net.Sockets;
using System.Text;
using Crestron.SimplSharp;

namespace musicStudioUnit.Devices
{
    /// <summary>
    /// Enhanced TCP client with proper data receive handling for music system
    /// </summary>
    public class TcpCoreClient : IDisposable
    {
        private readonly string _key;
        private readonly string _name;
        private readonly string _host;
        private readonly int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private byte[] _receiveBuffer = new byte[1024];
        private bool _isConnected;
        private readonly object _lockObject = new object();

        // Events
        public event EventHandler<TcpDataReceivedEventArgs> DataReceived;
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

        public string Key => _key;
        public string Name => _name;

        public TcpCoreClient(string key, string name, string host, int port)
        {
            _key = key;
            _name = name;
            _host = host;
            _port = port;
            Debug.Console(1, "TcpCoreClient created for {0}:{1}", host, port);
        }

        /// <summary>
        /// Connect to remote host
        /// </summary>
        public bool Connect()
        {
            lock (_lockObject)
            {
                try
                {
                    if (_isConnected)
                    {
                        Debug.Console(1, "TcpCoreClient already connected");
                        return true;
                    }

                    Debug.Console(1, "TcpCoreClient connecting to {0}:{1}", _host, _port);

                    _client = new TcpClient();
                    _client.Connect(_host, _port);
                    _stream = _client.GetStream();
                    _isConnected = true;

                    // Start async receive
                    BeginReceive();

                    Debug.Console(1, "TcpCoreClient connected successfully");
                    Connected?.Invoke(this, EventArgs.Empty);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "TcpCoreClient connection error: {0}", ex.Message);
                    Cleanup();
                    return false;
                }
            }
        }

        /// <summary>
        /// Disconnect from remote host
        /// </summary>
        public void Disconnect()
        {
            lock (_lockObject)
            {
                Debug.Console(1, "TcpCoreClient disconnecting");
                Cleanup();
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Send ASCII message
        /// </summary>
        public void SendAsciiMessage(string message)
        {
            lock (_lockObject)
            {
                try
                {
                    if (!_isConnected || _stream == null)
                    {
                        Debug.Console(0, "TcpCoreClient not connected - cannot send message");
                        return;
                    }

                    Debug.Console(2, "TcpCoreClient sending: {0}", message.Replace("\r", "\\r").Replace("\n", "\\n"));

                    byte[] data = Encoding.ASCII.GetBytes(message);
                    _stream.Write(data, 0, data.Length);
                    _stream.Flush();

                    Debug.Console(2, "TcpCoreClient message sent successfully");
                }
                catch (Exception ex)
                {
                    Debug.Console(0, "TcpCoreClient send error: {0}", ex.Message);
                    Cleanup();
                    Disconnected?.Invoke(this, EventArgs.Empty);
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
                Debug.Console(0, "TcpCoreClient BeginReceive error: {0}", ex.Message);
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

                    Debug.Console(2, "TcpCoreClient received {0} bytes", bytesRead);

                    // Fire data received event
                    DataReceived?.Invoke(this, new TcpDataReceivedEventArgs
                    {
                        Data = receivedData
                    });

                    // Continue receiving
                    BeginReceive();
                }
                else
                {
                    // Connection closed
                    Debug.Console(1, "TcpCoreClient connection closed by remote host");
                    Cleanup();
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, "TcpCoreClient receive error: {0}", ex.Message);
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

                Debug.Console(2, "TcpCoreClient cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, "TcpCoreClient cleanup error: {0}", ex.Message);
            }
        }

        public void Dispose()
        {
            Cleanup();
        }
    }

    /// <summary>
    /// Event arguments for TCP data received
    /// </summary>
    public class TcpDataReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; set; }
    }
}

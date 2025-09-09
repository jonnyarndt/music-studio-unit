using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronSockets;
using core_tools;
using musicStudioUnit.Configuration;
using musicStudioUnit.Services;

namespace musicStudioUnit.Services
{
    /// <summary>
    /// Inter-MSU Communication Service
    /// Handles communication between MSUs for combination state sharing
    /// Implements network discovery and state synchronization per Client-Scope.md requirements:
    /// - Adjacent MSU detection (north, south, east, west)
    /// - Combination state sharing and coordination
    /// - Master/slave relationship management
    /// - Network-based MSU discovery and health monitoring
    /// </summary>
    public class InterMSUCommunicationService : IKeyName, IDisposable
    {
        #region Private Fields

        private readonly string _key;
        private readonly string _localMSUID;
        private readonly int _localXCoord;
        private readonly int _localYCoord;
        private readonly ushort _communicationPort;
        private readonly BuildingConfiguration _buildingConfig;

        // Network communication
        private TcpServer _server;
        private readonly Dictionary<string, MslConnection> _connections = new Dictionary<string, MslConnection>();
        private readonly object _lockObject = new object();

        // MSU state tracking
        private readonly Dictionary<string, RemoteMSUState> _remoteMSUs = new Dictionary<string, RemoteMSUState>();
        private StudioCombinationType _localCombinationType = StudioCombinationType.Single;
        private List<string> _localCombinedMSUs = new List<string>();

        // Discovery and heartbeat
        private CTimer _discoveryTimer;
        private CTimer _heartbeatTimer;
        private const int DiscoveryIntervalMs = 30000; // 30 seconds
        private const int HeartbeatIntervalMs = 10000; // 10 seconds
        private const int ConnectionTimeoutMs = 20000; // 20 seconds

        private bool _isInitialized = false;
        private bool _disposed = false;

        #endregion

        #region Events

        /// <summary>
        /// Fired when a remote MSU's combination state changes
        /// </summary>
        public event EventHandler<RemoteMSUStateChangedEventArgs> RemoteMSUStateChanged;

        /// <summary>
        /// Fired when an adjacent MSU becomes available or unavailable
        /// </summary>
        public event EventHandler<AdjacentMSUAvailabilityEventArgs> AdjacentMSUAvailabilityChanged;

        /// <summary>
        /// Fired when combination coordination is requested from another MSU
        /// </summary>
        public event EventHandler<CombinationCoordinationEventArgs> CombinationCoordinationRequested;

        #endregion

        #region Public Properties

        public string Key => _key;
        public string Name => "Inter-MSU Communication Service";

        /// <summary>
        /// List of currently connected remote MSUs
        /// </summary>
        public IReadOnlyDictionary<string, RemoteMSUState> RemoteMSUs
        {
            get
            {
                lock (_lockObject)
                {
                    return new Dictionary<string, RemoteMSUState>(_remoteMSUs);
                }
            }
        }

        /// <summary>
        /// List of adjacent MSUs (north, south, east, west) that are available
        /// </summary>
        public IReadOnlyList<RemoteMSUState> AdjacentMSUs
        {
            get
            {
                lock (_lockObject)
                {
                    return GetAdjacentMSUs().AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Whether the service is running
        /// </summary>
        public bool IsRunning => _server?.State == ServerState.Server_Listening;

        #endregion

        #region Constructor

        public InterMSUCommunicationService(string key, string localMSUID, int xCoord, int yCoord, 
                                          ushort communicationPort, BuildingConfiguration buildingConfig)
        {
            _key = key ?? throw new ArgumentNullException(nameof(key));
            _localMSUID = localMSUID ?? throw new ArgumentNullException(nameof(localMSUID));
            _localXCoord = xCoord;
            _localYCoord = yCoord;
            _communicationPort = communicationPort;
            _buildingConfig = buildingConfig ?? throw new ArgumentNullException(nameof(buildingConfig));

            // Register with device manager
            DeviceManager.AddDevice(key, this);

            Debug.Console(1, this, "Inter-MSU Communication Service created - MSU: {0}, Coordinates: ({1},{2}), Port: {3}",
                _localMSUID, _localXCoord, _localYCoord, _communicationPort);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize and start the communication service
        /// </summary>
        public void Initialize()
        {
            if (_disposed || _isInitialized) return;

            try
            {
                Debug.Console(1, this, "Initializing Inter-MSU Communication Service");

                StartTcpServer();
                StartDiscoveryProcess();
                StartHeartbeatProcess();

                _isInitialized = true;
                Debug.Console(1, this, "Inter-MSU Communication Service initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error initializing Inter-MSU Communication Service: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Update local combination state and broadcast to connected MSUs
        /// </summary>
        public void UpdateLocalCombinationState(StudioCombinationType combinationType, List<string> combinedMSUIDs)
        {
            if (_disposed) return;

            lock (_lockObject)
            {
                try
                {
                    _localCombinationType = combinationType;
                    _localCombinedMSUs = combinedMSUIDs?.ToList() ?? new List<string>();

                    Debug.Console(1, this, "Local combination state updated - Type: {0}, MSUs: {1}",
                        combinationType, string.Join(",", _localCombinedMSUs));

                    // Broadcast state update to all connected MSUs
                    BroadcastStateUpdate();
                }
                catch (Exception ex)
                {
                    Debug.Console(0, this, "Error updating local combination state: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Request combination coordination with specified MSUs
        /// </summary>
        public bool RequestCombinationCoordination(List<string> targetMSUIDs, StudioCombinationType requestedType)
        {
            if (_disposed || targetMSUIDs == null || !targetMSUIDs.Any()) return false;

            try
            {
                Debug.Console(1, this, "Requesting combination coordination - Type: {0}, MSUs: {1}",
                    requestedType, string.Join(",", targetMSUIDs));

                bool allSuccessful = true;

                foreach (string msuUID in targetMSUIDs)
                {
                    if (_connections.ContainsKey(msuUID))
                    {
                        var request = new CombinationCoordinationMessage
                        {
                            RequestType = CombinationRequestType.RequestCombination,
                            RequestedCombination = requestedType,
                            MasterMSUID = _localMSUID,
                            TargetMSUIDs = targetMSUIDs
                        };

                        if (!SendMessageToMSU(msuUID, request))
                        {
                            allSuccessful = false;
                        }
                    }
                    else
                    {
                        Debug.Console(1, this, "No connection to MSU {0} for combination request", msuUID);
                        allSuccessful = false;
                    }
                }

                return allSuccessful;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error requesting combination coordination: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Respond to combination coordination request
        /// </summary>
        public void RespondToCombinationRequest(string requestingMSUID, CombinationRequestType responseType)
        {
            if (_disposed || string.IsNullOrEmpty(requestingMSUID)) return;

            try
            {
                var response = new CombinationCoordinationMessage
                {
                    RequestType = responseType,
                    MasterMSUID = requestingMSUID,
                    TargetMSUIDs = new List<string> { _localMSUID }
                };

                SendMessageToMSU(requestingMSUID, response);

                Debug.Console(1, this, "Sent combination response to {0}: {1}", requestingMSUID, responseType);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error responding to combination request: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Get adjacent MSUs based on coordinates (north, south, east, west only)
        /// </summary>
        public List<RemoteMSUState> GetAdjacentMSUs()
        {
            lock (_lockObject)
            {
                var adjacent = new List<RemoteMSUState>();

                foreach (var msu in _remoteMSUs.Values)
                {
                    // Check for adjacent coordinates (not diagonal)
                    int deltaX = Math.Abs(msu.XCoord - _localXCoord);
                    int deltaY = Math.Abs(msu.YCoord - _localYCoord);

                    // Adjacent means exactly one coordinate differs by 1, other is same
                    if ((deltaX == 1 && deltaY == 0) || (deltaX == 0 && deltaY == 1))
                    {
                        adjacent.Add(msu);
                    }
                }

                return adjacent;
            }
        }

        #endregion

        #region Private Methods - Network Communication

        private void StartTcpServer()
        {
            try
            {
                _server = new TcpServer("0.0.0.0", _communicationPort, EthernetAdapterType.EthernetUnknownAdapter, 10);
                _server.SocketStatusChange += OnServerSocketStatusChange;

                var result = _server.WaitForConnectionAsync(OnClientConnected);

                Debug.Console(1, this, "TCP Server started on port {0}", _communicationPort);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error starting TCP server: {0}", ex.Message);
            }
        }

        private void OnServerSocketStatusChange(TcpServer server, SocketStatus status)
        {
            Debug.Console(2, this, "Server socket status changed: {0}", status);
        }

        private void OnClientConnected(TcpServer server, uint clientIndex)
        {
            try
            {
                Debug.Console(1, this, "Client connected - Index: {0}", clientIndex);

                // Setup receive for this client
                _server.ReceiveDataAsync(clientIndex, OnDataReceived);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error handling client connection: {0}", ex.Message);
            }
        }

        private void OnDataReceived(TcpServer server, uint clientIndex, int byteCount)
        {
            try
            {
                if (byteCount > 0)
                {
                    byte[] receivedData = new byte[byteCount];
                    Array.Copy(server.GetIncomingDataBuffer(clientIndex), receivedData, byteCount);

                    string messageJson = Encoding.UTF8.GetString(receivedData);
                    ProcessReceivedMessage(messageJson, clientIndex);
                }

                // Continue receiving
                _server.ReceiveDataAsync(clientIndex, OnDataReceived);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error receiving data: {0}", ex.Message);
            }
        }

        private void StartDiscoveryProcess()
        {
            _discoveryTimer = new CTimer(_ => PerformMSUDiscovery(), 1000, DiscoveryIntervalMs);
            Debug.Console(1, this, "Discovery process started");
        }

        private void StartHeartbeatProcess()
        {
            _heartbeatTimer = new CTimer(_ => SendHeartbeats(), 5000, HeartbeatIntervalMs);
            Debug.Console(1, this, "Heartbeat process started");
        }

        private void PerformMSUDiscovery()
        {
            try
            {
                // Get list of MSUs from building configuration
                if (_buildingConfig?.MusicStudioUnits != null)
                {
                    foreach (var msuConfig in _buildingConfig.MusicStudioUnits)
                    {
                        if (msuConfig.UID != _localMSUID && !_connections.ContainsKey(msuConfig.UID))
                        {
                            // Attempt to connect to this MSU
                            ConnectToMSU(msuConfig);
                        }
                    }
                }

                // Clean up timed-out connections
                CleanupTimedOutConnections();
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error during MSU discovery: {0}", ex.Message);
            }
        }

        private void ConnectToMSU(MusicStudioUnit msuConfig)
        {
            try
            {
                var client = new TcpClient(msuConfig.IPAddress, _communicationPort, EthernetAdapterType.EthernetUnknownAdapter);
                
                client.SocketStatusChange += (tcpClient, status) =>
                {
                    OnClientSocketStatusChange(msuConfig.UID, tcpClient, status);
                };

                var connectResult = client.ConnectToServerAsync(OnConnectedToMSU);

                Debug.Console(2, this, "Attempting connection to MSU {0} at {1}:{2}", 
                    msuConfig.UID, msuConfig.IPAddress, _communicationPort);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error connecting to MSU {0}: {1}", msuConfig.UID, ex.Message);
            }
        }

        private void OnClientSocketStatusChange(string msuUID, TcpClient client, SocketStatus status)
        {
            Debug.Console(2, this, "Client socket status for {0}: {1}", msuUID, status);

            if (status == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                lock (_lockObject)
                {
                    _connections[msuUID] = new MslConnection
                    {
                        Client = client,
                        LastActivity = DateTime.Now,
                        IsConnected = true
                    };
                }

                // Send initial state exchange
                SendStateUpdate(msuUID);
            }
            else if (status == SocketStatus.SOCKET_STATUS_SOCKET_NOT_EXIST)
            {
                lock (_lockObject)
                {
                    _connections.Remove(msuUID);
                    _remoteMSUs.Remove(msuUID);
                }

                Debug.Console(1, this, "Connection to MSU {0} lost", msuUID);
            }
        }

        private void OnConnectedToMSU(TcpClient client)
        {
            try
            {
                Debug.Console(1, this, "Connected to remote MSU via client");

                // Setup receive for this client connection
                client.ReceiveDataAsync(OnClientDataReceived);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error setting up client connection: {0}", ex.Message);
            }
        }

        private void OnClientDataReceived(TcpClient client, int byteCount)
        {
            try
            {
                if (byteCount > 0)
                {
                    byte[] receivedData = new byte[byteCount];
                    Array.Copy(client.IncomingDataBuffer, receivedData, byteCount);

                    string messageJson = Encoding.UTF8.GetString(receivedData);
                    ProcessReceivedMessage(messageJson, 0); // Use 0 for client connections
                }

                // Continue receiving
                client.ReceiveDataAsync(OnClientDataReceived);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error receiving client data: {0}", ex.Message);
            }
        }

        #endregion

        #region Private Methods - Message Processing

        private void ProcessReceivedMessage(string messageJson, uint clientIndex)
        {
            try
            {
                // Parse the JSON message and handle different message types
                var message = ParseMessage(messageJson);
                if (message == null) return;

                switch (message.MessageType)
                {
                    case MSUMessageType.StateUpdate:
                        HandleStateUpdate(message as StateUpdateMessage);
                        break;
                    case MSUMessageType.CombinationCoordination:
                        HandleCombinationCoordination(message as CombinationCoordinationMessage);
                        break;
                    case MSUMessageType.Heartbeat:
                        HandleHeartbeat(message as HeartbeatMessage);
                        break;
                    case MSUMessageType.Discovery:
                        HandleDiscovery(message as DiscoveryMessage, clientIndex);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error processing received message: {0}", ex.Message);
            }
        }

        private MSUMessage ParseMessage(string messageJson)
        {
            try
            {
                // Simple JSON parsing for MSU messages
                // In a real implementation, you would use a proper JSON library
                // For now, we'll use a simplified approach

                if (messageJson.Contains("\"MessageType\":\"StateUpdate\""))
                {
                    return ParseStateUpdateMessage(messageJson);
                }
                else if (messageJson.Contains("\"MessageType\":\"CombinationCoordination\""))
                {
                    return ParseCombinationCoordinationMessage(messageJson);
                }
                else if (messageJson.Contains("\"MessageType\":\"Heartbeat\""))
                {
                    return ParseHeartbeatMessage(messageJson);
                }
                else if (messageJson.Contains("\"MessageType\":\"Discovery\""))
                {
                    return ParseDiscoveryMessage(messageJson);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error parsing message: {0}", ex.Message);
                return null;
            }
        }

        private StateUpdateMessage ParseStateUpdateMessage(string json)
        {
            // Simplified JSON parsing - in real implementation use proper JSON library
            var message = new StateUpdateMessage
            {
                MessageType = MSUMessageType.StateUpdate,
                SourceMSUID = ExtractJsonValue(json, "SourceMSUID"),
                CombinationType = (StudioCombinationType)Enum.Parse(typeof(StudioCombinationType), 
                    ExtractJsonValue(json, "CombinationType")),
                XCoord = int.Parse(ExtractJsonValue(json, "XCoord")),
                YCoord = int.Parse(ExtractJsonValue(json, "YCoord"))
            };

            return message;
        }

        private CombinationCoordinationMessage ParseCombinationCoordinationMessage(string json)
        {
            var message = new CombinationCoordinationMessage
            {
                MessageType = MSUMessageType.CombinationCoordination,
                RequestType = (CombinationRequestType)Enum.Parse(typeof(CombinationRequestType), 
                    ExtractJsonValue(json, "RequestType")),
                MasterMSUID = ExtractJsonValue(json, "MasterMSUID")
            };

            return message;
        }

        private HeartbeatMessage ParseHeartbeatMessage(string json)
        {
            return new HeartbeatMessage
            {
                MessageType = MSUMessageType.Heartbeat,
                SourceMSUID = ExtractJsonValue(json, "SourceMSUID"),
                Timestamp = DateTime.Parse(ExtractJsonValue(json, "Timestamp"))
            };
        }

        private DiscoveryMessage ParseDiscoveryMessage(string json)
        {
            return new DiscoveryMessage
            {
                MessageType = MSUMessageType.Discovery,
                SourceMSUID = ExtractJsonValue(json, "SourceMSUID"),
                IsResponse = bool.Parse(ExtractJsonValue(json, "IsResponse"))
            };
        }

        private string ExtractJsonValue(string json, string key)
        {
            // Very simple JSON value extraction - replace with proper JSON library in production
            string pattern = $"\"{key}\":\"";
            int startIndex = json.IndexOf(pattern);
            if (startIndex == -1) return string.Empty;

            startIndex += pattern.Length;
            int endIndex = json.IndexOf("\"", startIndex);
            if (endIndex == -1) return string.Empty;

            return json.Substring(startIndex, endIndex - startIndex);
        }

        private void HandleStateUpdate(StateUpdateMessage message)
        {
            if (message?.SourceMSUID == null) return;

            lock (_lockObject)
            {
                var previousState = _remoteMSUs.ContainsKey(message.SourceMSUID) ? 
                    _remoteMSUs[message.SourceMSUID] : null;

                _remoteMSUs[message.SourceMSUID] = new RemoteMSUState
                {
                    MSUID = message.SourceMSUID,
                    CombinationType = message.CombinationType,
                    XCoord = message.XCoord,
                    YCoord = message.YCoord,
                    LastContact = DateTime.Now,
                    IsAvailable = true
                };

                Debug.Console(2, this, "State update from MSU {0} - Type: {1}, Coords: ({2},{3})",
                    message.SourceMSUID, message.CombinationType, message.XCoord, message.YCoord);

                // Fire event if this is a new MSU or state changed
                bool isNewMSU = previousState == null;
                bool stateChanged = previousState?.CombinationType != message.CombinationType;

                if (isNewMSU || stateChanged)
                {
                    RemoteMSUStateChanged?.Invoke(this, new RemoteMSUStateChangedEventArgs
                    {
                        MSUID = message.SourceMSUID,
                        NewState = _remoteMSUs[message.SourceMSUID],
                        PreviousState = previousState
                    });
                }

                // Check if this affects adjacent MSU availability
                CheckAdjacentMSUAvailability(message.SourceMSUID);
            }
        }

        private void HandleCombinationCoordination(CombinationCoordinationMessage message)
        {
            if (message == null) return;

            Debug.Console(1, this, "Combination coordination from {0}: {1}", 
                message.MasterMSUID, message.RequestType);

            // Fire event for upper layers to handle
            CombinationCoordinationRequested?.Invoke(this, new CombinationCoordinationEventArgs
            {
                RequestType = message.RequestType,
                RequestingMSUID = message.MasterMSUID,
                RequestedCombination = message.RequestedCombination,
                TargetMSUIDs = message.TargetMSUIDs
            });
        }

        private void HandleHeartbeat(HeartbeatMessage message)
        {
            if (message?.SourceMSUID == null) return;

            lock (_lockObject)
            {
                if (_connections.ContainsKey(message.SourceMSUID))
                {
                    _connections[message.SourceMSUID].LastActivity = DateTime.Now;
                }

                if (_remoteMSUs.ContainsKey(message.SourceMSUID))
                {
                    _remoteMSUs[message.SourceMSUID].LastContact = DateTime.Now;
                }
            }

            Debug.Console(2, this, "Heartbeat from MSU {0}", message.SourceMSUID);
        }

        private void HandleDiscovery(DiscoveryMessage message, uint clientIndex)
        {
            if (message?.SourceMSUID == null) return;

            Debug.Console(1, this, "Discovery from MSU {0}", message.SourceMSUID);

            if (!message.IsResponse)
            {
                // Send discovery response
                var response = new DiscoveryMessage
                {
                    MessageType = MSUMessageType.Discovery,
                    SourceMSUID = _localMSUID,
                    IsResponse = true
                };

                SendMessageToClient(clientIndex, response);

                // Also send our current state
                SendStateUpdateToClient(clientIndex);
            }
        }

        private void BroadcastStateUpdate()
        {
            var stateMessage = new StateUpdateMessage
            {
                MessageType = MSUMessageType.StateUpdate,
                SourceMSUID = _localMSUID,
                CombinationType = _localCombinationType,
                XCoord = _localXCoord,
                YCoord = _localYCoord,
                CombinedMSUIDs = _localCombinedMSUs
            };

            BroadcastMessage(stateMessage);
        }

        private void SendHeartbeats()
        {
            var heartbeat = new HeartbeatMessage
            {
                MessageType = MSUMessageType.Heartbeat,
                SourceMSUID = _localMSUID,
                Timestamp = DateTime.Now
            };

            BroadcastMessage(heartbeat);
        }

        private void SendStateUpdate(string msuUID)
        {
            var stateMessage = new StateUpdateMessage
            {
                MessageType = MSUMessageType.StateUpdate,
                SourceMSUID = _localMSUID,
                CombinationType = _localCombinationType,
                XCoord = _localXCoord,
                YCoord = _localYCoord,
                CombinedMSUIDs = _localCombinedMSUs
            };

            SendMessageToMSU(msuUID, stateMessage);
        }

        private void SendStateUpdateToClient(uint clientIndex)
        {
            var stateMessage = new StateUpdateMessage
            {
                MessageType = MSUMessageType.StateUpdate,
                SourceMSUID = _localMSUID,
                CombinationType = _localCombinationType,
                XCoord = _localXCoord,
                YCoord = _localYCoord,
                CombinedMSUIDs = _localCombinedMSUs
            };

            SendMessageToClient(clientIndex, stateMessage);
        }

        private void BroadcastMessage(MSUMessage message)
        {
            string messageJson = SerializeMessage(message);

            lock (_lockObject)
            {
                foreach (var connection in _connections.Values)
                {
                    if (connection.IsConnected && connection.Client != null)
                    {
                        try
                        {
                            byte[] data = Encoding.UTF8.GetBytes(messageJson);
                            connection.Client.SendDataAsync(data, data.Length, OnDataSent);
                        }
                        catch (Exception ex)
                        {
                            Debug.Console(0, this, "Error broadcasting message: {0}", ex.Message);
                        }
                    }
                }
            }
        }

        private bool SendMessageToMSU(string msuUID, MSUMessage message)
        {
            lock (_lockObject)
            {
                if (_connections.ContainsKey(msuUID) && _connections[msuUID].IsConnected)
                {
                    try
                    {
                        string messageJson = SerializeMessage(message);
                        byte[] data = Encoding.UTF8.GetBytes(messageJson);
                        
                        _connections[msuUID].Client.SendDataAsync(data, data.Length, OnDataSent);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.Console(0, this, "Error sending message to MSU {0}: {1}", msuUID, ex.Message);
                        return false;
                    }
                }
            }

            return false;
        }

        private void SendMessageToClient(uint clientIndex, MSUMessage message)
        {
            try
            {
                string messageJson = SerializeMessage(message);
                byte[] data = Encoding.UTF8.GetBytes(messageJson);
                
                _server.SendDataAsync(clientIndex, data, data.Length, OnDataSent);
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error sending message to client {0}: {1}", clientIndex, ex.Message);
            }
        }

        private void OnDataSent(TcpClient client, int bytesSent)
        {
            Debug.Console(2, this, "Data sent - Bytes: {0}", bytesSent);
        }

        private string SerializeMessage(MSUMessage message)
        {
            // Simple JSON serialization - replace with proper JSON library in production
            switch (message.MessageType)
            {
                case MSUMessageType.StateUpdate:
                    var stateMsg = message as StateUpdateMessage;
                    return $"{{\"MessageType\":\"StateUpdate\",\"SourceMSUID\":\"{stateMsg.SourceMSUID}\",\"CombinationType\":\"{stateMsg.CombinationType}\",\"XCoord\":{stateMsg.XCoord},\"YCoord\":{stateMsg.YCoord}}}";

                case MSUMessageType.CombinationCoordination:
                    var coordMsg = message as CombinationCoordinationMessage;
                    return $"{{\"MessageType\":\"CombinationCoordination\",\"RequestType\":\"{coordMsg.RequestType}\",\"MasterMSUID\":\"{coordMsg.MasterMSUID}\",\"RequestedCombination\":\"{coordMsg.RequestedCombination}\"}}";

                case MSUMessageType.Heartbeat:
                    var heartbeatMsg = message as HeartbeatMessage;
                    return $"{{\"MessageType\":\"Heartbeat\",\"SourceMSUID\":\"{heartbeatMsg.SourceMSUID}\",\"Timestamp\":\"{heartbeatMsg.Timestamp:yyyy-MM-ddTHH:mm:ssZ}\"}}";

                case MSUMessageType.Discovery:
                    var discMsg = message as DiscoveryMessage;
                    return $"{{\"MessageType\":\"Discovery\",\"SourceMSUID\":\"{discMsg.SourceMSUID}\",\"IsResponse\":{discMsg.IsResponse.ToString().ToLower()}}}";

                default:
                    return "{}";
            }
        }

        private void CheckAdjacentMSUAvailability(string msuUID)
        {
            if (!_remoteMSUs.ContainsKey(msuUID)) return;

            var msu = _remoteMSUs[msuUID];
            
            // Check if this MSU is adjacent
            int deltaX = Math.Abs(msu.XCoord - _localXCoord);
            int deltaY = Math.Abs(msu.YCoord - _localYCoord);

            if ((deltaX == 1 && deltaY == 0) || (deltaX == 0 && deltaY == 1))
            {
                // This is an adjacent MSU
                AdjacentMSUAvailabilityChanged?.Invoke(this, new AdjacentMSUAvailabilityEventArgs
                {
                    MSUID = msuUID,
                    IsAvailable = msu.IsAvailable,
                    CombinationType = msu.CombinationType
                });
            }
        }

        private void CleanupTimedOutConnections()
        {
            var cutoffTime = DateTime.Now.AddMilliseconds(-ConnectionTimeoutMs);

            lock (_lockObject)
            {
                var timedOutConnections = _connections.Where(kvp => kvp.Value.LastActivity < cutoffTime).ToList();

                foreach (var timedOut in timedOutConnections)
                {
                    Debug.Console(1, this, "Connection to MSU {0} timed out", timedOut.Key);

                    timedOut.Value.Client?.DisconnectFromServer();
                    _connections.Remove(timedOut.Key);

                    if (_remoteMSUs.ContainsKey(timedOut.Key))
                    {
                        _remoteMSUs[timedOut.Key].IsAvailable = false;
                        CheckAdjacentMSUAvailability(timedOut.Key);
                    }
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                Debug.Console(1, this, "Disposing Inter-MSU Communication Service");

                // Stop timers
                _discoveryTimer?.Stop();
                _heartbeatTimer?.Stop();
                _discoveryTimer?.Dispose();
                _heartbeatTimer?.Dispose();

                // Close all connections
                lock (_lockObject)
                {
                    foreach (var connection in _connections.Values)
                    {
                        connection.Client?.DisconnectFromServer();
                    }
                    _connections.Clear();
                }

                // Stop server
                _server?.Stop();
                _server?.Dispose();

                _disposed = true;
                Debug.Console(1, this, "Inter-MSU Communication Service disposed");
            }
            catch (Exception ex)
            {
                Debug.Console(0, this, "Error disposing Inter-MSU Communication Service: {0}", ex.Message);
            }
        }

        #endregion
    }

    #region Supporting Classes and Enums

    /// <summary>
    /// MSU connection information
    /// </summary>
    public class MslConnection
    {
        public TcpClient Client { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsConnected { get; set; }
    }

    /// <summary>
    /// Remote MSU state information
    /// </summary>
    public class RemoteMSUState
    {
        public string MSUID { get; set; }
        public StudioCombinationType CombinationType { get; set; }
        public int XCoord { get; set; }
        public int YCoord { get; set; }
        public DateTime LastContact { get; set; }
        public bool IsAvailable { get; set; }
        public List<string> CombinedMSUIDs { get; set; } = new List<string>();
    }

    /// <summary>
    /// Message types for inter-MSU communication
    /// </summary>
    public enum MSUMessageType
    {
        StateUpdate,
        CombinationCoordination,
        Heartbeat,
        Discovery
    }

    /// <summary>
    /// Base class for MSU messages
    /// </summary>
    public abstract class MSUMessage
    {
        public MSUMessageType MessageType { get; set; }
        public string SourceMSUID { get; set; }
    }

    /// <summary>
    /// State update message
    /// </summary>
    public class StateUpdateMessage : MSUMessage
    {
        public StudioCombinationType CombinationType { get; set; }
        public int XCoord { get; set; }
        public int YCoord { get; set; }
        public List<string> CombinedMSUIDs { get; set; } = new List<string>();
    }

    /// <summary>
    /// Combination coordination message
    /// </summary>
    public class CombinationCoordinationMessage : MSUMessage
    {
        public CombinationRequestType RequestType { get; set; }
        public string MasterMSUID { get; set; }
        public StudioCombinationType RequestedCombination { get; set; }
        public List<string> TargetMSUIDs { get; set; } = new List<string>();
    }

    /// <summary>
    /// Heartbeat message
    /// </summary>
    public class HeartbeatMessage : MSUMessage
    {
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Discovery message
    /// </summary>
    public class DiscoveryMessage : MSUMessage
    {
        public bool IsResponse { get; set; }
    }

    /// <summary>
    /// Combination request types
    /// </summary>
    public enum CombinationRequestType
    {
        RequestCombination,
        AcceptCombination,
        RejectCombination,
        RequestUncombine,
        ConfirmUncombine
    }

    #endregion

    #region Event Arguments

    /// <summary>
    /// Remote MSU state changed event arguments
    /// </summary>
    public class RemoteMSUStateChangedEventArgs : EventArgs
    {
        public string MSUID { get; set; }
        public RemoteMSUState NewState { get; set; }
        public RemoteMSUState PreviousState { get; set; }
    }

    /// <summary>
    /// Adjacent MSU availability changed event arguments
    /// </summary>
    public class AdjacentMSUAvailabilityEventArgs : EventArgs
    {
        public string MSUID { get; set; }
        public bool IsAvailable { get; set; }
        public StudioCombinationType CombinationType { get; set; }
    }

    /// <summary>
    /// Combination coordination event arguments
    /// </summary>
    public class CombinationCoordinationEventArgs : EventArgs
    {
        public CombinationRequestType RequestType { get; set; }
        public string RequestingMSUID { get; set; }
        public StudioCombinationType RequestedCombination { get; set; }
        public List<string> TargetMSUIDs { get; set; }
    }

    #endregion
}

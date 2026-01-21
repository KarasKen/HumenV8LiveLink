using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

public class WebSocketServerManager : MonoBehaviour
{
    // WebSocket客户端连接类
    public class WebSocketClient
    {
        public TcpClient TcpClient { get; private set; }
        public NetworkStream NetworkStream { get; private set; }
        public string ClientIpAddress { get; private set; }
        public int ClientPort { get; private set; }
        public bool IsConnected { get; internal set; }

        public WebSocketClient(TcpClient tcpClient)
        {
            TcpClient = tcpClient;
            // 设置接收超时，避免无限等待
            tcpClient.ReceiveTimeout = 5000;
            tcpClient.SendTimeout = 5000;
            NetworkStream = tcpClient.GetStream();
            IPEndPoint remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
            ClientIpAddress = remoteEndPoint.Address.ToString();
            ClientPort = remoteEndPoint.Port;
            IsConnected = true;
        }
    }

    // 消息结构体，用于在队列中存储消息和客户端信息
    private struct WebSocketMessage
    {
        public string Message;
        public WebSocketClient Client;
    }

    // 连接事件结构体
    private struct ConnectionEvent
    {
        public WebSocketClient Client;
        public bool IsConnecting;
    }

    private TcpListener tcpListener = null;
    private List<WebSocketClient> clientConnections = new List<WebSocketClient>();
    private int port;
    private Action<string, WebSocketClient> onMessageReceived;
    private Action<WebSocketClient> onClientConnected;
    private Action<WebSocketClient> onClientDisconnected;
    private Action<string> onError;
    private CancellationTokenSource cancellationTokenSource;
    private bool isRunning;
    
    // 线程安全的消息队列
    private Queue<WebSocketMessage> messageQueue = new Queue<WebSocketMessage>();
    private object messageQueueLock = new object();
    
    // 线程安全的连接事件队列
    private Queue<ConnectionEvent> connectionEventQueue = new Queue<ConnectionEvent>();
    private object connectionEventQueueLock = new object();

    public bool IsRunning => isRunning;
    public int ClientCount => clientConnections.Count;

    /// <summary>
    /// 初始化WebSocket服务器
    /// </summary>
    /// <param name="serverPort">服务器端口</param>
    /// <param name="messageCallback">接收消息回调（参数：消息内容，发送消息的客户端）</param>
    /// <param name="clientConnectedCallback">客户端连接回调</param>
    /// <param name="clientDisconnectedCallback">客户端断开连接回调</param>
    /// <param name="errorCallback">错误回调</param>
    public void Initialize(int serverPort, Action<string, WebSocketClient> messageCallback, 
                          Action<WebSocketClient> clientConnectedCallback = null, 
                          Action<WebSocketClient> clientDisconnectedCallback = null, 
                          Action<string> errorCallback = null)
    {
        port = serverPort;
        onMessageReceived = messageCallback;
        onClientConnected = clientConnectedCallback;
        onClientDisconnected = clientDisconnectedCallback;
        onError = errorCallback;
    }

    /// <summary>
    /// 启动WebSocket服务器
    /// </summary>
    public void StartServer()
    {
        if (isRunning)
        {
            Debug.LogWarning("WebSocket服务器已经在运行");
            return;
        }

        try
        {
            cancellationTokenSource = new CancellationTokenSource();
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            isRunning = true;

            Debug.Log("WebSocket服务器已启动，监听端口: " + port);

            // 使用Task.Run在后台线程接受客户端连接，避免阻塞主线程
            Task.Run(() => AcceptClientsAsync());
        }
        catch (Exception ex)
        {
            Debug.LogError("启动WebSocket服务器失败: " + ex.Message);
            
            if (onError != null)
            {
                onError(ex.Message);
            }
        }
    }

    /// <summary>
    /// 停止WebSocket服务器
    /// </summary>
    public void StopServer()
    {
        if (!isRunning)
        {
            Debug.LogWarning("WebSocket服务器未在运行");
            return;
        }

        try
        {
            // 取消所有操作
            cancellationTokenSource?.Cancel();

            // 断开所有客户端连接
            lock (clientConnections)
            {
                foreach (var client in clientConnections)
                {
                    try
                    {
                        client.NetworkStream?.Close();
                        client.TcpClient?.Close();
                        client.IsConnected = false;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("关闭客户端连接失败: " + ex.Message);
                    }
                }
                clientConnections.Clear();
            }

            // 停止服务器
            tcpListener?.Stop();
            isRunning = false;

            Debug.Log("WebSocket服务器已停止");
        }
        catch (Exception ex)
        {
            Debug.LogError("停止WebSocket服务器失败: " + ex.Message);
            
            if (onError != null)
            {
                onError(ex.Message);
            }
        }
        finally
        {
            cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// 在后台线程中接受客户端连接
    /// </summary>
    private async Task AcceptClientsAsync()
    {
        while (isRunning)
        {
            try
            {
                // 异步接受客户端连接
                TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync();
                WebSocketClient client = new WebSocketClient(tcpClient);
                
                // 在后台线程处理WebSocket握手
                _ = HandleWebSocketHandshakeAsync(client);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                // 服务器停止时的正常中断
                break;
            }
            catch (Exception ex)
            {
                Debug.LogError("接受客户端连接失败: " + ex.Message);
                
                if (onError != null)
                {
                    // 简化处理，直接记录错误，不跨线程调用回调
                    Debug.LogError("WebSocket错误: " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// 处理WebSocket握手（异步版本）
    /// </summary>
    private async Task HandleWebSocketHandshakeAsync(WebSocketClient client)
    {
        try
        {
            // 读取HTTP请求头（异步）
            byte[] buffer = new byte[4096];
            int bytesRead = await client.NetworkStream.ReadAsync(buffer, 0, buffer.Length);
            string httpRequest = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            // 解析请求头获取Sec-WebSocket-Key
            string webSocketKey = ExtractWebSocketKey(httpRequest);
            if (string.IsNullOrEmpty(webSocketKey))
            {
                CloseClientConnection(client);
                return;
            }

            // 生成响应头
            string response = GenerateWebSocketResponse(webSocketKey);
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            
            // 发送响应（异步）
            await client.NetworkStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            await client.NetworkStream.FlushAsync();

            // 握手成功，添加到客户端列表
            lock (clientConnections)
            {
                clientConnections.Add(client);
            }

            string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
            Debug.Log("客户端连接: " + clientInfo);
            
            // 将连接事件添加到线程安全队列
            lock (connectionEventQueueLock)
            {
                connectionEventQueue.Enqueue(new ConnectionEvent
                {
                    Client = client,
                    IsConnecting = true
                });
            }

            // 开始接收消息（异步）
            _ = ReceiveMessagesFromClientAsync(client);
        }
        catch (Exception ex)
        {
            Debug.LogError("WebSocket握手失败: " + ex.Message);
            CloseClientConnection(client);
        }
    }

    /// <summary>
    /// 从客户端接收消息（异步版本）
    /// </summary>
    private async Task ReceiveMessagesFromClientAsync(WebSocketClient client)
    {
        while (client.IsConnected && client.NetworkStream.CanRead)
        {
            try
            {
                // 读取WebSocket帧头（异步）
                byte[] frameHeader = new byte[2];
                int bytesRead = await client.NetworkStream.ReadAsync(frameHeader, 0, 2);
                if (bytesRead == 0)
                {
                    // 连接已关闭
                    CloseClientConnection(client);
                    break;
                }

                // 解析帧头
                bool fin = (frameHeader[0] & 0x80) != 0;
                bool mask = (frameHeader[1] & 0x80) != 0;
                int opcode = frameHeader[0] & 0x0F;
                int payloadLength = frameHeader[1] & 0x7F;
                
                Debug.Log($"收到帧头: fin={fin}, mask={mask}, opcode={opcode}, payloadLength={payloadLength}");

                // 处理不同长度的payload
                if (payloadLength == 126)
                {
                    // 16位长度
                    byte[] lengthBytes = new byte[2];
                    bytesRead = await client.NetworkStream.ReadAsync(lengthBytes, 0, 2);
                    if (bytesRead != 2)
                    {
                        CloseClientConnection(client);
                        break;
                    }
                    // WebSocket使用网络字节序（大端序），需要转换
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthBytes);
                    }
                    payloadLength = BitConverter.ToUInt16(lengthBytes, 0);
                }
                else if (payloadLength == 127)
                {
                    // 64位长度
                    byte[] lengthBytes = new byte[8];
                    bytesRead = await client.NetworkStream.ReadAsync(lengthBytes, 0, 8);
                    if (bytesRead != 8)
                    {
                        CloseClientConnection(client);
                        break;
                    }
                    // WebSocket使用网络字节序（大端序），需要转换
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthBytes);
                    }
                    payloadLength = (int)BitConverter.ToUInt64(lengthBytes, 0);
                }

                // 读取掩码（如果有）
                byte[] maskingKey = null;
                if (mask)
                {
                    maskingKey = new byte[4];
                    bytesRead = await client.NetworkStream.ReadAsync(maskingKey, 0, 4);
                    if (bytesRead != 4)
                    {
                        CloseClientConnection(client);
                        break;
                    }
                }

                // 读取payload（异步）
                byte[] payloadData = new byte[payloadLength];
                bytesRead = await client.NetworkStream.ReadAsync(payloadData, 0, payloadLength);
                if (bytesRead != payloadLength)
                {
                    CloseClientConnection(client);
                    break;
                }

                // 解掩码
                if (mask)
                {
                    for (int i = 0; i < payloadData.Length; i++)
                    {
                        payloadData[i] ^= maskingKey[i % 4];
                    }
                }

                // 处理不同类型的消息
                switch (opcode)
                {
                    case 0x01: // 文本消息
                        string message = Encoding.UTF8.GetString(payloadData);
                        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
                        Debug.Log("收到消息来自 " + clientInfo + ": " + message);
                        
                        // 将消息添加到线程安全队列
                        lock (messageQueueLock)
                        {
                            messageQueue.Enqueue(new WebSocketMessage
                            {
                                Message = message,
                                Client = client
                            });
                        }
                        break;
                    case 0x08: // 关闭连接
                        CloseClientConnection(client);
                        break;
                    // 可以添加其他消息类型的处理
                }
            }
            catch (OperationCanceledException)
            {
                // 操作被取消，正常退出
                break;
            }
            catch (Exception ex)
            {
                Debug.LogError("接收消息失败: " + ex.Message);
                CloseClientConnection(client);
                break;
            }
        }
    }

    /// <summary>
    /// 向所有连接的客户端发送消息
    /// </summary>
    /// <param name="message">要发送的消息</param>
    public new void BroadcastMessage(string message)
    {
        lock (clientConnections)
        {
            foreach (var client in clientConnections)
            {
                try
                {
                    SendMessageToClient(client, message);
                }
                catch (Exception ex)
                {
                    Debug.LogError("向客户端发送消息失败: " + ex.Message);
                    
                    if (onError != null)
                    {
                        onError(ex.Message);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 向特定客户端发送消息（异步版本）
    /// </summary>
    /// <param name="client">要发送消息的客户端</param>
    /// <param name="message">要发送的消息</param>
    public async Task SendMessageToClientAsync(WebSocketClient client, string message)
    {
        if (client == null || !client.IsConnected)
        {
            Debug.LogError("客户端引用为空或未连接");
            return;
        }

        try
        {
            if (client.NetworkStream.CanWrite)
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                byte[] frame = CreateWebSocketFrame(messageBytes, true, 0x01);
                // 异步发送消息
                await client.NetworkStream.WriteAsync(frame, 0, frame.Length);
                await client.NetworkStream.FlushAsync();
            }
            else
            {
                Debug.LogWarning("客户端不可用，无法发送消息");
                CloseClientConnection(client);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("向客户端发送消息失败: " + ex.Message);
            CloseClientConnection(client);
            
            if (onError != null)
            {
                Debug.LogError("WebSocket错误: " + ex.Message);
            }
        }
    }
    
    /// <summary>
    /// 向特定客户端发送消息（同步版本，用于兼容现有代码）
    /// </summary>
    /// <param name="client">要发送消息的客户端</param>
    /// <param name="message">要发送的消息</param>
    public void SendMessageToClient(WebSocketClient client, string message)
    {
        // 调用异步版本，但不等待结果
        _ = SendMessageToClientAsync(client, message);
    }

    /// <summary>
    /// 创建WebSocket帧
    /// </summary>
    private byte[] CreateWebSocketFrame(byte[] payloadData, bool fin, byte opcode)
    {
        List<byte> frame = new List<byte>();

        // 帧头第一个字节：FIN + RSV + Opcode
        byte firstByte = (byte)(opcode);
        if (fin)
            firstByte |= 0x80;
        frame.Add(firstByte);

        // 帧头第二个字节：Mask + Payload Length
        byte secondByte = 0x00; // 服务器发送的消息不使用掩码
        int payloadLength = payloadData.Length;

        if (payloadLength < 126)
        {
            secondByte |= (byte)payloadLength;
            frame.Add(secondByte);
        }
        else if (payloadLength < 65536)
        {
            secondByte |= 126;
            frame.Add(secondByte);
            byte[] lengthBytes = BitConverter.GetBytes((ushort)payloadLength);
            // WebSocket使用网络字节序（大端序），需要转换
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }
            frame.AddRange(lengthBytes);
        }
        else
        {
            secondByte |= 127;
            frame.Add(secondByte);
            byte[] lengthBytes = BitConverter.GetBytes((ulong)payloadLength);
            // WebSocket使用网络字节序（大端序），需要转换
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }
            frame.AddRange(lengthBytes);
        }

        // 添加payload
        frame.AddRange(payloadData);

        return frame.ToArray();
    }

    /// <summary>
    /// 从HTTP请求头中提取Sec-WebSocket-Key
    /// </summary>
    private string ExtractWebSocketKey(string httpRequest)
    {
        string[] lines = httpRequest.Split(new[] { "\r\n" }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring("Sec-WebSocket-Key:".Length).Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// 生成WebSocket响应头
    /// </summary>
    private string GenerateWebSocketResponse(string webSocketKey)
    {
        // WebSocket握手响应需要的GUID
        const string webSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        
        // 拼接Key和GUID
        string combinedKey = webSocketKey + webSocketGuid;
        
        // 计算SHA1哈希
        byte[] sha1Hash = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(combinedKey));
        
        // Base64编码
        string base64Hash = Convert.ToBase64String(sha1Hash);

        // 构建响应头
        return "HTTP/1.1 101 Switching Protocols\r\n" +
               "Upgrade: websocket\r\n" +
               "Connection: Upgrade\r\n" +
               "Sec-WebSocket-Accept: " + base64Hash + "\r\n\r\n";
    }

    /// <summary>
    /// 关闭客户端连接
    /// </summary>
    private void CloseClientConnection(WebSocketClient client)
    {
        if (!client.IsConnected)
            return;

        try
        {
            client.IsConnected = false;
            // 关闭网络流和客户端连接
            client.NetworkStream?.Close();
            client.TcpClient?.Close();

            // 从客户端列表中移除
            lock (clientConnections)
            {
                clientConnections.Remove(client);
            }

            // 记录日志，但不要在后台线程调用Unity API
            string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
            Debug.Log("客户端断开连接: " + clientInfo);
            
            // 将断开连接事件添加到线程安全队列
            lock (connectionEventQueueLock)
            {
                connectionEventQueue.Enqueue(new ConnectionEvent
                {
                    Client = client,
                    IsConnecting = false
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("关闭客户端连接失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 获取所有连接的客户端
    /// </summary>
    /// <returns>客户端连接列表</returns>
    public List<WebSocketClient> GetAllClients()
    {
        lock (clientConnections)
        {
            return new List<WebSocketClient>(clientConnections);
        }
    }

    /// <summary>
    /// Unity Update方法，在主线程中处理消息队列
    /// </summary>
    private void Update()
    {
        // 处理连接事件队列
        ProcessConnectionEvents();
        
        // 处理消息队列
        ProcessMessageQueue();
    }

    /// <summary>
    /// 处理连接事件队列
    /// </summary>
    private void ProcessConnectionEvents()
    {
        Queue<ConnectionEvent> eventsToProcess = new Queue<ConnectionEvent>();
        
        // 批量取出所有连接事件
        lock (connectionEventQueueLock)
        {
            while (connectionEventQueue.Count > 0)
            {
                eventsToProcess.Enqueue(connectionEventQueue.Dequeue());
            }
        }
        
        // 处理所有连接事件
        while (eventsToProcess.Count > 0)
        {
            ConnectionEvent connectionEvent = eventsToProcess.Dequeue();
            
            if (connectionEvent.IsConnecting)
            {
                onClientConnected?.Invoke(connectionEvent.Client);
            }
            else
            {
                onClientDisconnected?.Invoke(connectionEvent.Client);
            }
        }
    }

    /// <summary>
    /// 处理消息队列
    /// </summary>
    private void ProcessMessageQueue()
    {
        Queue<WebSocketMessage> messagesToProcess = new Queue<WebSocketMessage>();
        
        // 批量取出所有消息，减少锁的持有时间
        lock (messageQueueLock)
        {
            while (messageQueue.Count > 0)
            {
                messagesToProcess.Enqueue(messageQueue.Dequeue());
            }
        }
        
        // 处理所有消息
        while (messagesToProcess.Count > 0)
        {
            WebSocketMessage wsMessage = messagesToProcess.Dequeue();
            onMessageReceived?.Invoke(wsMessage.Message, wsMessage.Client);
        }
    }

    private void OnDestroy()
    {
        StopServer();
    }
}

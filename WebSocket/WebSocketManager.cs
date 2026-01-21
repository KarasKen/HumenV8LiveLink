using UnityEngine;
using System;
using System.Collections;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class WebSocketManager : MonoBehaviour
{
    private ClientWebSocket webSocket = null;
    private CancellationTokenSource cancellationTokenSource;
    private Uri serverUri;
    private Action<string> onMessageReceived;
    private Action onConnected;
    private Action onDisconnected;
    private Action<string> onError;

    public bool IsConnected => webSocket?.State == WebSocketState.Open;

    public void Initialize(string uri, Action<string> messageCallback, Action connectedCallback = null, Action disconnectedCallback = null, Action<string> errorCallback = null)
    {
        serverUri = new Uri(uri);
        onMessageReceived = messageCallback;
        onConnected = connectedCallback;
        onDisconnected = disconnectedCallback;
        onError = errorCallback;
    }

    public async Task ConnectAsync()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            Debug.LogWarning("WebSocket is already connected");
            return;
        }

        try
        {
            webSocket = new ClientWebSocket();
            cancellationTokenSource = new CancellationTokenSource();
            
            await webSocket.ConnectAsync(serverUri, cancellationTokenSource.Token);
            
            Debug.Log("WebSocket connected to: " + serverUri);
            
            if (onConnected != null)
            {
                onConnected();
            }
            
            StartCoroutine(ReceiveMessages());
        }
        catch (Exception ex)
        {
            Debug.LogError("WebSocket connection error: " + ex.Message);
            
            if (onError != null)
            {
                onError(ex.Message);
            }
            
            await DisconnectAsync();
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open)
        {
            Debug.LogError("WebSocket is not connected");
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(bytes);
            
            await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            Debug.LogError("WebSocket send error: " + ex.Message);
            
            if (onError != null)
            {
                onError(ex.Message);
            }
            
            await DisconnectAsync();
        }
    }

    // 用于传递接收消息结果的结构体
    private struct ReceiveResult
    {
        public WebSocketReceiveResult Result;
        public bool HasError;
        public string ErrorMessage;
    }
    
    private IEnumerator ReceiveMessages()
    {
        var buffer = new ArraySegment<byte>(new byte[8192]);
        
        while (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            var message = new StringBuilder();
            bool shouldDisconnect = false;
            
            // 创建结果对象
            var receiveResult = new ReceiveResult();
            
            // 接收消息的协程，使用回调获取结果
            yield return StartCoroutine(ReceiveMessageCoroutine(buffer, (result) => receiveResult = result));
            
            if (receiveResult.HasError || receiveResult.Result == null)
            {
                goto HandleError;
            }
            
            if (receiveResult.Result.MessageType == WebSocketMessageType.Close)
            {
                shouldDisconnect = true;
            }
            else if (receiveResult.Result.MessageType == WebSocketMessageType.Text)
            {
                var decodedMessage = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, receiveResult.Result.Count);
                message.Append(decodedMessage);
                
                // 如果不是完整消息，继续接收
                while (!receiveResult.Result.EndOfMessage && !shouldDisconnect)
                {
                    // 创建新的结果对象
                    var partialReceiveResult = new ReceiveResult();
                    yield return StartCoroutine(ReceiveMessageCoroutine(buffer, (result) => partialReceiveResult = result));
                    receiveResult = partialReceiveResult;
                    
                    if (receiveResult.HasError || receiveResult.Result == null)
                    {
                        goto HandleError;
                    }
                    
                    if (receiveResult.Result.MessageType == WebSocketMessageType.Close)
                    {
                        shouldDisconnect = true;
                    }
                    else if (receiveResult.Result.MessageType == WebSocketMessageType.Text)
                    {
                        decodedMessage = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, receiveResult.Result.Count);
                        message.Append(decodedMessage);
                    }
                }
                
                if (!shouldDisconnect && receiveResult.Result.MessageType == WebSocketMessageType.Text && message.Length > 0)
                {
                    if (onMessageReceived != null)
                    {
                        onMessageReceived(message.ToString());
                    }
                }
            }
            
            // 处理关闭连接
            if (shouldDisconnect)
            {
                StartCoroutine(CloseConnectionCoroutine());
                yield break;
            }
            
            continue;
            
            HandleError:
            if (receiveResult.HasError)
            {
                Debug.LogError("WebSocket receive error: " + receiveResult.ErrorMessage);
                
                if (onError != null)
                {
                    onError(receiveResult.ErrorMessage);
                }
                
                // 使用协程处理断开连接
                StartCoroutine(DisconnectCoroutine());
                yield break;
            }
        }
        
        // 检查连接状态，如果需要则断开连接
        if (webSocket != null && webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
        {
            StartCoroutine(DisconnectCoroutine());
        }
    }
    
    private IEnumerator ReceiveMessageCoroutine(ArraySegment<byte> buffer, Action<ReceiveResult> resultCallback)
    {
        // 初始化结果
        var receiveResult = new ReceiveResult();
        receiveResult.Result = null;
        receiveResult.HasError = false;
        receiveResult.ErrorMessage = string.Empty;
        
        var receiveTask = webSocket.ReceiveAsync(buffer, cancellationTokenSource.Token);
        
        // 等待异步任务完成
        while (!receiveTask.IsCompleted)
        {
            yield return null;
        }
        
        try
        {
            receiveResult.Result = receiveTask.Result;
        }
        catch (Exception ex)
        {
            receiveResult.HasError = true;
            receiveResult.ErrorMessage = ex.Message;
        }
        
        // 通过回调返回结果
        if (resultCallback != null)
        {
            resultCallback(receiveResult);
        }
    }
    
    private IEnumerator CloseConnectionCoroutine()
    {
        if (webSocket != null)
        {
            var closeTask = webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            yield return new WaitUntil(() => closeTask.IsCompleted);
            
            if (closeTask.IsFaulted)
            {
                Debug.LogError("WebSocket close error: " + closeTask.Exception.Message);
            }
        }
        
        StartCoroutine(DisconnectCoroutine());
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
            
            if (webSocket != null)
            {
                if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.Connecting)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed by client", CancellationToken.None);
                }
                
                webSocket.Dispose();
                webSocket = null;
                
                Debug.Log("WebSocket disconnected");
                
                if (onDisconnected != null)
                {
                    onDisconnected();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("WebSocket disconnect error: " + ex.Message);
            
            if (onError != null)
            {
                onError(ex.Message);
            }
        }
    }

    private void OnDestroy()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            DisconnectAsync().Wait();
        }
    }

    public void Connect()
    {
        StartCoroutine(ConnectCoroutine());
    }

    private IEnumerator ConnectCoroutine()
    {
        var connectTask = ConnectAsync();
        yield return new WaitUntil(() => connectTask.IsCompleted);
    }

    public new void SendMessage(string message)
    {
        StartCoroutine(SendMessageCoroutine(message));
    }

    private IEnumerator SendMessageCoroutine(string message)
    {
        var sendTask = SendMessageAsync(message);
        yield return new WaitUntil(() => sendTask.IsCompleted);
    }

    public void Disconnect()
    {
        StartCoroutine(DisconnectCoroutine());
    }

    private IEnumerator DisconnectCoroutine()
    {
        var disconnectTask = DisconnectAsync();
        yield return new WaitUntil(() => disconnectTask.IsCompleted);
    }
}
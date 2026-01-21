using UnityEngine;
using System;

public class WebSocketServerForHumen : MonoBehaviour
{
    /// <summary>
    /// WebSocket服务器端口号
    /// </summary>
    [SerializeField] private int serverPort = 8080;
      
    private WebSocketServerManager webSocketServerManager;
    
    /// <summary>
    /// 收到消息时触发的事件
    /// </summary>
    public event Action<string> MessageReceived;

    private void Awake()
    {
        webSocketServerManager = gameObject.AddComponent<WebSocketServerManager>();
    }

    private void Start()
    {
        // 初始化WebSocket服务器
        webSocketServerManager.Initialize(
            serverPort,
            OnMessageReceived,
            OnClientConnected,
            OnClientDisconnected,
            OnError
        );
        // 启动WebSocket服务器
        webSocketServerManager.StartServer();
    }
    
      
      
    private void OnClientConnected(WebSocketServerManager.WebSocketClient client)
    {
        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
        Debug.Log("客户端连接: " + clientInfo);
    }

    private void OnClientDisconnected(WebSocketServerManager.WebSocketClient client)
    {
        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
        Debug.Log("客户端断开: " + clientInfo);
    }

    private void OnMessageReceived(string message, WebSocketServerManager.WebSocketClient client)
    {
        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
        // 示例：回显收到的消息
        webSocketServerManager.SendMessageToClient(client, "服务器回显: " + message);
        Debug.Log("发送给 " + clientInfo + ": 服务器回显: " + message);
        
        // 触发MessageReceived事件，通知订阅者
        MessageReceived?.Invoke(message);
    }

    private void OnError(string error)
    {
        Debug.LogError("错误: " + error);
    }

    /// <summary>
    /// 当对象被销毁时调用，确保关闭WebSocket服务器
    /// </summary>
    private void OnDestroy()
    {
        if (webSocketServerManager != null)
        {
            webSocketServerManager.StopServer();
            Debug.Log("WebSocket服务器已关闭");
        }
    }
}

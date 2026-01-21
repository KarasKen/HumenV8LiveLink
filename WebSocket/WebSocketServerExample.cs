using UnityEngine;
using UnityEngine.UI;
using System;

public class WebSocketServerExample : MonoBehaviour
{
    /// <summary>
    /// WebSocket服务器端口号
    /// </summary>
    [SerializeField] private int serverPort = 8080;
    
    /// <summary>
    /// 用于输入服务器端口号的输入框
    /// </summary>
    [SerializeField] private InputField portInput;
    
    /// <summary>
    /// 启动服务器的按钮
    /// </summary>
    [SerializeField] private Button startServerButton;
    
    /// <summary>
    /// 停止服务器的按钮
    /// </summary>
    [SerializeField] private Button stopServerButton;
    
    /// <summary>
    /// 广播消息的按钮
    /// </summary>
    [SerializeField] private Button broadcastButton;
    
    /// <summary>
    /// 用于输入广播消息的输入框
    /// </summary>
    [SerializeField] private InputField broadcastInput;
    
    /// <summary>
    /// 显示服务器状态的文本
    /// </summary>
    [SerializeField] private Text statusText;
    
    /// <summary>
    /// 显示消息历史记录的文本
    /// </summary>
    [SerializeField] private Text messageHistoryText;
    
    /// <summary>
    /// 显示客户端数量的文本
    /// </summary>
    [SerializeField] private Text clientCountText;

    private WebSocketServerManager webSocketServerManager;

    private void Awake()
    {
        webSocketServerManager = gameObject.AddComponent<WebSocketServerManager>();
    }

    private void Start()
    {
        // 检查并初始化UI组件
        InitializeUIComponents();
        
        // 初始化WebSocket服务器
        webSocketServerManager.Initialize(
            serverPort,
            OnMessageReceived,
            OnClientConnected,
            OnClientDisconnected,
            OnError
        );

        UpdateUI(false);
        UpdateClientCount(0);
    }
    
    private void InitializeUIComponents()
    {
        // 检查所有UI组件是否已分配
        if (portInput != null)
        {
            portInput.text = serverPort.ToString();
        }
        else
        {
            Debug.LogError("portInput is not assigned in the Inspector!");
        }
        
        if (statusText == null) Debug.LogError("statusText is not assigned in the Inspector!");
        if (messageHistoryText == null) Debug.LogError("messageHistoryText is not assigned in the Inspector!");
        if (clientCountText == null) Debug.LogError("clientCountText is not assigned in the Inspector!");
        
        // 为启动服务器按钮注册点击事件
        if (startServerButton != null)
        {
            startServerButton.onClick.AddListener(OnStartServerButtonClick);
        }
        else
        {
            Debug.LogError("startServerButton is not assigned in the Inspector!");
        }
        
        // 为停止服务器按钮注册点击事件
        if (stopServerButton != null)
        {
            stopServerButton.onClick.AddListener(OnStopServerButtonClick);
        }
        else
        {
            Debug.LogError("stopServerButton is not assigned in the Inspector!");
        }
        
        // 为广播消息按钮注册点击事件
        if (broadcastButton != null)
        {
            broadcastButton.onClick.AddListener(OnBroadcastButtonClick);
        }
        else
        {
            Debug.LogError("broadcastButton is not assigned in the Inspector!");
        }
        
        if (broadcastInput == null) Debug.LogError("broadcastInput is not assigned in the Inspector!");
    }
    
 
    public void OnStartServerButtonClick()
    {
       Debug.Log("点击了开始WebSocket服务器的按钮");
        // 确定要使用的端口号
        int portToUse = serverPort;
        if (portInput != null && int.TryParse(portInput.text, out int port))
        {
            portToUse = port;
        }
        
        // 重新初始化并启动服务器
        webSocketServerManager.Initialize(
            portToUse,
            OnMessageReceived,
            OnClientConnected,
            OnClientDisconnected,
            OnError
        );
        
        webSocketServerManager.StartServer();
        UpdateUI(true);
    }

    public void OnStopServerButtonClick()
    {
        if (webSocketServerManager != null)
        {
            webSocketServerManager.StopServer();
            UpdateUI(false);
            UpdateClientCount(0);
        }
    }

    public void OnBroadcastButtonClick()
    {
        if (broadcastInput != null && !string.IsNullOrEmpty(broadcastInput.text))
        {
            webSocketServerManager.BroadcastMessage(broadcastInput.text);
            AddMessageToHistory("广播: " + broadcastInput.text);
            broadcastInput.text = "";
        }
    }

    private void OnClientConnected(WebSocketServerManager.WebSocketClient client)
    {
        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
        if (statusText != null)
        {
            statusText.text = "状态: 服务器运行中";
            statusText.color = Color.green;
        }
        AddMessageToHistory("客户端连接: " + clientInfo);
        UpdateClientCount(webSocketServerManager.ClientCount);
    }

    private void OnClientDisconnected(WebSocketServerManager.WebSocketClient client)
    {
        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
        AddMessageToHistory("客户端断开: " + clientInfo);
        UpdateClientCount(webSocketServerManager.ClientCount);
    }

    private void OnMessageReceived(string message, WebSocketServerManager.WebSocketClient client)
    {
        string clientInfo = client.ClientIpAddress + ":" + client.ClientPort;
        AddMessageToHistory("接收自 " + clientInfo + ": " + message);
        Debug.Log("阚大剑接收自 " + clientInfo + ": " + message);
        // 示例：回显收到的消息
        webSocketServerManager.SendMessageToClient(client, "服务器回显: " + message);
        AddMessageToHistory("发送给 " + clientInfo + ": 服务器回显: " + message);
    }

    private void OnError(string error)
    {
        if (statusText != null)
        {
            statusText.text = "状态: 错误";
            statusText.color = Color.red;
        }
        AddMessageToHistory("错误: " + error);
    }

    private void UpdateUI(bool isRunning)
    {
        if (startServerButton != null) startServerButton.interactable = !isRunning;
        if (stopServerButton != null) stopServerButton.interactable = isRunning;
        if (broadcastButton != null) broadcastButton.interactable = isRunning;
        if (broadcastInput != null) broadcastInput.interactable = isRunning;
        if (portInput != null) portInput.interactable = !isRunning;
        
        if (!isRunning && statusText != null)
        {
            statusText.text = "状态: 服务器已停止";
            statusText.color = Color.red;
        }
    }

    private void UpdateClientCount(int count)
    {
        if (clientCountText != null)
        {
            clientCountText.text = "客户端数量: " + count;
        }
    }

    private void AddMessageToHistory(string message)
    {
        if (messageHistoryText != null)
        {
            messageHistoryText.text += DateTime.Now.ToString("HH:mm:ss") + " - " + message + "\n";
            
            // 限制历史消息数量，避免文本过长
            string[] lines = messageHistoryText.text.Split('\n');
            if (lines.Length > 50)
            {
                messageHistoryText.text = string.Join("\n", lines, lines.Length - 50, 50);
            }
        }
    }

    private void OnDestroy()
    {
        if (webSocketServerManager != null)
        {
            webSocketServerManager.StopServer();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

public class WebSocketExample : MonoBehaviour
{
    [SerializeField] private string webSocketUrl = "ws://echo.websocket.org";
    [SerializeField] private InputField messageInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button sendButton;
    [SerializeField] private Text statusText;
    [SerializeField] private Text messageHistoryText;

    private WebSocketManager webSocketManager;

    private void Awake()
    {
        webSocketManager = gameObject.AddComponent<WebSocketManager>();
    }

    private void Start()
    {
        webSocketManager.Initialize(
            webSocketUrl,
            OnMessageReceived,
            OnConnected,
            OnDisconnected,
            OnError
        );

        UpdateUI(false);
    }

    public void OnConnectButtonClick()
    {
        webSocketManager.Connect();
    }

    public void OnDisconnectButtonClick()
    {
        webSocketManager.Disconnect();
    }

    public void OnSendButtonClick()
    {
        if (!string.IsNullOrEmpty(messageInput.text))
        {
            webSocketManager.SendMessage(messageInput.text);
            AddMessageToHistory("发送: " + messageInput.text);
            messageInput.text = "";
        }
    }

    private void OnConnected()
    {
        statusText.text = "状态: 已连接";
        statusText.color = Color.green;
        AddMessageToHistory("已连接到服务器");
        UpdateUI(true);
    }

    private void OnDisconnected()
    {
        statusText.text = "状态: 已断开";
        statusText.color = Color.red;
        AddMessageToHistory("与服务器断开连接");
        UpdateUI(false);
    }

    private void OnMessageReceived(string message)
    {
        AddMessageToHistory("接收: " + message);
    }

    private void OnError(string error)
    {
        statusText.text = "状态: 错误";
        statusText.color = Color.red;
        AddMessageToHistory("错误: " + error);
    }

    private void UpdateUI(bool isConnected)
    {
        connectButton.interactable = !isConnected;
        disconnectButton.interactable = isConnected;
        sendButton.interactable = isConnected;
        messageInput.interactable = isConnected;
    }

    private void AddMessageToHistory(string message)
    {
        messageHistoryText.text += message + "\n";
        
        // 限制历史消息数量，避免文本过长
        string[] lines = messageHistoryText.text.Split('\n');
        if (lines.Length > 50)
        {
            messageHistoryText.text = string.Join("\n", lines, lines.Length - 50, 50);
        }
    }

    private void OnDestroy()
    {
        if (webSocketManager != null)
        {
            webSocketManager.Disconnect();
        }
    }
}
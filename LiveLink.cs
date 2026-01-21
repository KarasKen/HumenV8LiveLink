using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
//using System.Runtime.InteropServices.Marshalling;

public class LiveLink : MonoBehaviour
{

 
 
    Animator cachedAnimator;
    /// <summary>
    /// 用于存储渲染器和索引的简单结构体
    /// 替代C#元组，避免Unity序列化问题
    /// </summary>
    private struct RendererIndexPair
    {
        public SkinnedMeshRenderer renderer;
        public int index;
        
        public RendererIndexPair(SkinnedMeshRenderer renderer, int index)
        {
            this.renderer = renderer;
            this.index = index;
        }
    }

    [Header("WebSocket Server Object")]
    [Tooltip("场景中包含 WebSocketServerForHumen 组件的GameObject")]
    public GameObject WebSocketServerForHumenObject; //用于选择场景内含有 WebSocketServerForHumen 组件的 GameObject
    // Start is called before the first frame update
    public string LiveLinkSouceName = "V8";
    private WebSocketServerForHumen webSocketServerForHumen;
    
    [Header("BlendShape 控制")]
    [Tooltip("自动扫描得到的 SkinnedMeshRenderer（含 BlendShape）")]
[SerializeField] private List<SkinnedMeshRenderer> blendShapeRenderers = new();

    [Tooltip("当前所有 BlendShape 通道的权重快照，运行时可在 Inspector 实时调整")]
    [System.Serializable]
    public class BlendShapeChannel
    {
        [Tooltip("通道名称（与原 BlendShape 名称一致）")]
        public string name;
        [Range(0f, 100f)]
        public float weight;
    }

    [SerializeField] private List<BlendShapeChannel> blendShapeChannels = new();
    private readonly List<float> blendShapeWeights = new(); // 用于实时存储权重值的列表

    /// <summary>
    /// 记录每个 SkinnedMeshRenderer 里各 BlendShape 在 blendShapeWeights 列表中的下标
    /// 例：rendererToBlendShapeIndices[smr][i] = k 表示 smr 的第 i 个 BlendShape 对应 blendShapeWeights[k]
    /// 用于快速把权重值映射回正确的 Renderer 与 BlendShape 通道
    /// </summary>
    private readonly Dictionary<SkinnedMeshRenderer, int[]> rendererToBlendShapeIndices = new();
    
    /// <summary>
    /// 优化性能：BlendShape短名称到(渲染器,索引)列表的映射
    /// 标记为非序列化，避免Unity编辑器序列化冲突
    /// </summary>
    [System.NonSerialized]
    private readonly Dictionary<string, List<RendererIndexPair>> blendShapeShortNameMap = new();

    public void RefreshBlendShapeList()//刷新BlendShape列表
    {
        blendShapeRenderers.Clear();
        blendShapeWeights.Clear();
        rendererToBlendShapeIndices.Clear();
        blendShapeShortNameMap.Clear(); // 清空映射表

        // 收集自身及子物体中所有 SkinnedMeshRenderer
        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in renderers)
        {
            if (smr.sharedMesh == null) continue;
            int count = smr.sharedMesh.blendShapeCount;
            if (count == 0) continue;

            blendShapeRenderers.Add(smr);
            int[] indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                indices[i] = blendShapeWeights.Count;
                blendShapeWeights.Add(smr.GetBlendShapeWeight(i));
                
                // 构建短名称映射表
                string fullName = smr.sharedMesh.GetBlendShapeName(i);
                string shortName = fullName.Contains(".") ? fullName.Split('.')[1] : fullName;
                
                if (!blendShapeShortNameMap.ContainsKey(shortName))
                {
                    blendShapeShortNameMap[shortName] = new List<RendererIndexPair>();
                }
                blendShapeShortNameMap[shortName].Add(new RendererIndexPair(smr, i));
            }
            rendererToBlendShapeIndices[smr] = indices;
        }

        Debug.Log($"[LiveLink] 刷新完成，共找到 {blendShapeRenderers.Count} 个 SkinnedMeshRenderer，{blendShapeWeights.Count} 个 BlendShape 通道。");
    }

    /// <summary>
    /// 在 Inspector 中点击按钮，将当前 Inspector 中的权重值立即应用到模型
    /// </summary>
    [ContextMenu("应用权重到模型")]
    public void ApplyWeightsToModel()
    {
        // 遍历 Inspector 中列出的所有通道
        foreach (var channel in blendShapeChannels)
        {
            string targetShortName = channel.name.Contains(".") ? channel.name.Split('.')[1] : channel.name;

            // 使用映射表快速查找所有匹配的 BlendShape
            if (blendShapeShortNameMap.TryGetValue(targetShortName, out var shapeList))
            {
                // 直接遍历所有匹配的 BlendShape 并设置权重
                foreach (var pair in shapeList)
                {
                    SkinnedMeshRenderer smr = pair.renderer;
                    int index = pair.index;
                    
                    if (smr != null && smr.sharedMesh != null && index >= 0 && index < smr.sharedMesh.blendShapeCount)
                    {
                        smr.SetBlendShapeWeight(index, channel.weight);
                    }
                }
            }
            else
            {
                // 兼容模式：如果映射表中没有找到，回退到原始的遍历查找
                foreach (var smr in blendShapeRenderers)
                {
                    if (smr == null || smr.sharedMesh == null) continue;

                    int count = smr.sharedMesh.blendShapeCount;
                    for (int i = 0; i < count; i++)
                    {
                        string fullName = smr.sharedMesh.GetBlendShapeName(i);
                        string shortName = fullName.Contains(".") ? fullName.Split('.')[1] : fullName;

                        if (shortName == targetShortName)
                        {
                            smr.SetBlendShapeWeight(i, channel.weight);
                            break;
                        }
                    }
                }
            }
        }
    }
   

    /// <summary>
    /// 在 Inspector 中点击按钮，将当前 Inspector 中的权重值立即应用到模型
    /// 需在 Unity Editor 中手动调用或绑定到按钮/菜单
    /// </summary>
    [ContextMenu("更新 BlendShape (编辑器模式)")]
    public void UpdateBlendShape()
        {
            // 确保在编辑器模式下运行
            if (!Application.isPlaying)
            {
                RefreshBlendShapeList();
                RefreshBlendShapeData();
                ApplyWeightsToModel();
                UnityEditor.EditorUtility.SetDirty(this); // 标记对象已更改，触发序列化保存
            }
            else
            {
                Debug.LogWarning("[LiveLink] UpdateBlendShape 仅在编辑器模式下有效，运行时请使用 RefreshBlendShapeList / RefreshBlendShapeData / ApplyWeightsToModel。");
            }
        }

    /// <summary>
    /// 将 Inspector 中所有 BlendShape 通道的权重重置为 0
    /// </summary>
    [ContextMenu("还原所有 BlendShape 权重为 0")]
    public void ResetAllBlendShapeWeightsToZero()
    {
        for (int i = 0; i < blendShapeChannels.Count; i++)
        {
            blendShapeChannels[i].weight = 0f;
        }
        ApplyWeightsToModel();
    }


        
#if UNITY_EDITOR
  
    /// <summary>
    /// 编辑器模式下每帧执行：在 Scene 视图刷新时调用，用于实时预览
    /// </summary>
    private void OnValidate()
    {
        // 确保仅在编辑器模式下运行，不在运行时执行
        if (!Application.isPlaying)
        {
            ApplyWeightsToModel();
            UnityEditor.EditorUtility.SetDirty(this); // 标记对象已更改，触发序列化保存
        }
    }

     [ContextMenu("复制blendShape的通道名称")]
     public void CopyBlendShapeChannelNames()
     {
        string names = string.Join("\n", blendShapeChannels.Select(c => c.name));
        GUIUtility.systemCopyBuffer = names;
        Debug.Log("BlendShape通道名称已复制到剪贴板：\n" + names);
     }
   
#endif


    /// <summary>
    /// 获取当前所有 BlendShape 的完整信息：Renderer + 名称 + 权重
    /// </summary>
    public List<(SkinnedMeshRenderer renderer, string name, float weight)> GetAllBlendShapeData()
    {
        var list = new List<(SkinnedMeshRenderer, string, float)>();
        for (int r = 0; r < blendShapeRenderers.Count; r++)
        {
            var smr = blendShapeRenderers[r];
            if (smr == null || smr.sharedMesh == null) continue;
            int count = smr.sharedMesh.blendShapeCount;
            for (int i = 0; i < count; i++)
            {
                string name = smr.sharedMesh.GetBlendShapeName(i);
                float w = smr.GetBlendShapeWeight(i);
                list.Add((smr, name, w));
            }
        }
        return list;
    }


    /// <summary>
    /// 刷新当前 Inspector 中的 BlendShape 通道数据
    /// 运行后，在 Inspector 中右键点击该脚本组件，选择“刷新 BlendShape 数据”即可执行；
    /// 也可在脚本生命周期（如 Start）里手动调用：GetComponent<LiveLink>().RefreshBlendShapeData();
    /// </summary>
    public void RefreshBlendShapeData()
    {
        blendShapeChannels.Clear();
        var data = GetAllBlendShapeData();
        foreach (var (smr, name, w) in data)
        {
            string shortName = name.Contains(".") ? name.Split('.')[1] : name;
            if (!blendShapeChannels.Exists(c => c.name == shortName))
            {
                blendShapeChannels.Add(new BlendShapeChannel { name = shortName, weight = w });
            }
        }
        Debug.Log($"[LiveLink] 刷新完成，共找到 {blendShapeChannels.Count} 个 BlendShape 通道。");
    }

    void Start()
    {
        if (WebSocketServerForHumenObject == null)
        {
            Debug.LogError("WebSocketServerForHumenObject 未赋值！请在 Inspector 中选择场景内的 WebSocketServerForHumen 组件的 GameObject。");
            return;
        }
        // 从 GameObject 中获取 WebSocketServerForHumen 组件
        webSocketServerForHumen = WebSocketServerForHumenObject.GetComponent<WebSocketServerForHumen>();
        if (webSocketServerForHumen == null)
        {
            Debug.LogError("WebSocketServerForHumen 组件未找到！请确保 WebSocketServerForHumenObject 中包含 WebSocketServerForHumen 组件。");
            return;
        }
          webSocketServerForHumen.MessageReceived += ReceivedMessage;

        //得到Animator
        cachedAnimator = GetComponent<Animator>();
        if (cachedAnimator == null)
        {
            Debug.LogError("Animator 组件未找到！请确保 GameObject 中包含 Animator 组件。");
            return;
        }
    }
  
  //设置是否为说话状态
  public void SetSpeakState (bool Speaking)
    {
        if (cachedAnimator == null)
        {
            Debug.LogError("Animator 组件未找到！请确保 GameObject 中包含 Animator 组件。");
            return;
        }
        cachedAnimator.SetBool("Speaking", Speaking);
    }
    /// <summary>
    /// 供 WebSocketServerForHumen 在收到消息时调用的回调
    /// </summary>
    /// <param name="message">收到的字符串消息</param>
    // 用于JSON解析的临时类结构
    [System.Serializable]
    private class SourceData
    {
        public List<Curve> CurveArray;
    }
    
    [System.Serializable]
    private class Curve
    {
        public string Name;
        public float Value;
    }

    public void ReceivedMessage(string message)
    {
        // 在这里处理收到的消息
        Debug.Log($"LiveLink 收到消息: {message}");
        
        try
        {
            // 检查消息是否为有效的JSON格式
            if (!IsValidJson(message))
            {
                Debug.LogWarning($"[LiveLink] 收到的消息不是有效的JSON格式: {message}");
                return;
            }
            
            // 检查JSON是否包含LiveLinkSouceName指定的根字段
            // 使用字符串检查的方式，查找格式如 "LiveLinkSouceName": {...} 的结构
            string sourceFieldPattern = $"\"{LiveLinkSouceName}\":";
            
            // 提取JSON最外层key作为trimmed值
            string trimmed = "";
            int firstQuote = message.IndexOf('"');
            int secondQuote = message.IndexOf('"', firstQuote + 1);
            if (firstQuote >= 0 && secondQuote > firstQuote)
            {
                trimmed = message.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }
            Debug.Log($"trimmed: {trimmed}");
            if (!string.IsNullOrEmpty(trimmed) && !string.IsNullOrEmpty(LiveLinkSouceName) && trimmed == LiveLinkSouceName)
            {
                // 处理有效的json
               ManageMessageFromLiveLink(message);
            }
            else
            {
                Debug.LogWarning($"[LiveLink] 收到的JSON数据不包含有效的{LiveLinkSouceName}根字段: {message}");
            }
        }
        catch (Exception e) // 捕获上方 try 块里发生的任何异常，防止程序崩溃；e 里装着异常详细信息，下面用 LogError 把它打印出来方便调试
        {
            Debug.LogError($"[LiveLink] 解析JSON数据时出错: {e.Message}\n消息内容: {message}");
        }
    }

    string ExpressionKey = "Expression";        // json里表情的key名称
    string ContentKey = "Content";        // json里内容的key名称
    //处理webSoket收到的有效message，用来解析里面的json，然后根据不同内容做不同处理
    private void ManageMessageFromLiveLink(string jsonMessage)
    {
       
        // 提取 ExpressionKey 下的 JSON 数据
        string expressionJson = GetJsonValue(jsonMessage, ExpressionKey);
        Debug.Log($"expressionJson: {expressionJson}");
        
      // 执行SetBlendShapeWeightFromLiveLink函数
        SetBlendShapeWeightFromLiveLink(expressionJson);



        // 提取 ContentKey 下的 JSON 数据
        string contentJson = GetJsonValue(jsonMessage, ContentKey);
        //string contentJson = "";
        // int contentStart = jsonMessage.IndexOf($"\"{ContentKey}\":");
        // if (contentStart >= 0)
        //{
        // 找到数组的起始位置 [
        // int arrayStart = jsonMessage.IndexOf('[', contentStart + ContentKey.Length + 3);
        // if (arrayStart >= 0)
        // {
        //     // 找到匹配的结束位置 ]
        //     int bracketCount = 1;
        //     int arrayEnd = arrayStart + 1;
        //     for (int i = arrayStart + 1; i < jsonMessage.Length; i++)
        //     {
        //         if (jsonMessage[i] == '[')
        //             bracketCount++;
        //         else if (jsonMessage[i] == ']')
        //             bracketCount--;

        //         if (bracketCount == 0)
        //         {
        //             arrayEnd = i + 1;
        //             break;
        //         }
        //     }

        //     if (bracketCount == 0 && arrayEnd > arrayStart)
        //     {
        //         contentJson = jsonMessage.Substring(arrayStart, arrayEnd - arrayStart).Trim();
        //     }
        // }
        //}
        // 得到里面Speaking的值
        bool isSpeaking = false;
        
        // 先检查contentJson是否有效
        if (!string.IsNullOrEmpty(contentJson))
        {
            try
            {
                // 简单可靠的字符串解析方式
                int speakIndex = contentJson.IndexOf("\"Speaking\":");
                if (speakIndex >= 0)
                {
                    // 找到值的开始位置（冒号后面）
                    int valueStart = speakIndex + 11; // "Speaking": 的长度是11
                    
                    // 跳过空格
                    while (valueStart < contentJson.Length && char.IsWhiteSpace(contentJson[valueStart]))
                    {
                        valueStart++;
                    }
                    
                    // 找到值的结束位置（逗号或大括号之前）
                    int valueEnd = contentJson.IndexOfAny(new char[] { ',', '}' }, valueStart);
                    if (valueEnd < 0) valueEnd = contentJson.Length;
                    
                    // 提取值并转换为bool
                    string speakValue = contentJson.Substring(valueStart, valueEnd - valueStart).Trim('"', ' ', '\t', '\r', '\n');
                    isSpeaking = speakValue.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[LiveLink] 解析Speaking值时出错: {e.Message}");
            }
        }
        
        Debug.Log($"isSpeaking: {isSpeaking}");
        // 设置说话状态
        SetSpeakState(isSpeaking);
        // 解析Unicode转义序列，让中文正确显示
        string decodedContentJson = contentJson;
        if (!string.IsNullOrEmpty(contentJson))
        {
            decodedContentJson = System.Text.RegularExpressions.Regex.Replace(
                contentJson, 
                @"\\u([0-9a-fA-F]{4})", 
                match => ((char)System.Convert.ToInt32(match.Groups[1].Value, 16)).ToString()
            );
        }
        Debug.Log($"contentJson: {decodedContentJson}");
      
    }
    
  //得到json里某一层级的内容
  private string GetJsonValue(string json, string key)
    {
        int keyIndex = json.IndexOf($"\"{key}\":");
        if (keyIndex >= 0)
        {
            int valueStart = json.IndexOf('[', keyIndex+key.Length+3);
            int valueEnd = 0;
            if (valueStart >= 0) 
            {
                // 找到匹配的结束位置 ]
                int bracketCount = 1;
                int arrayEnd = valueStart + 1;
                for (int i = valueStart + 1; i < json.Length; i++)
                {
                    if (json[i] == '[')
                        bracketCount++;
                    else if (json[i] == ']')
                        bracketCount--;
                    
                    if (bracketCount == 0)
                    {
                        arrayEnd = i + 1;
                        break;
                    }
                }
                
                if (bracketCount == 0 && arrayEnd > valueStart)
                {
                    valueEnd = arrayEnd;
                }
            };
            return json.Substring(valueStart, valueEnd - valueStart).Trim();
        }
        return null;
    }
    
    private void SetBlendShapeWeightFromLiveLink(string jsonMessage)
    {
        try
        {
            // 手动创建SourceData对象并解析JSON数组
            SourceData sourceData = new SourceData();
            sourceData.CurveArray = new List<Curve>();
            
            // 使用JsonUtility解析需要先包装数组
            string wrappedJson = $"{{\"CurveArray\":{jsonMessage}}}";
            sourceData = JsonUtility.FromJson<SourceData>(wrappedJson);
            
            if (sourceData?.CurveArray != null)
            {
                // 更新BlendShape权重
                UpdateBlendShapeWeights(sourceData.CurveArray);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[LiveLink] 解析JSON数据时出错: {e.Message}\n消息内容: {jsonMessage}");
        }
    }
        
    
    
    /// <summary>
    /// 更新BlendShape权重值
    /// </summary>
    private void UpdateBlendShapeWeights(List<Curve> curveArray)
    {
        bool weightsUpdated = false;
        
        foreach (var curve in curveArray)
        {
            // 查找对应的BlendShape通道
            var blendShapeChannel = blendShapeChannels.Find(channel => channel.name == curve.Name);
            
            if (blendShapeChannel != null)
            {
                // 更新权重值（将0-1范围转换为0-100范围）
                float newWeight = curve.Value * 100f;
                newWeight = Mathf.Clamp(newWeight, 0f, 100f); // 确保权重在有效范围内
                
                if (Mathf.Abs(blendShapeChannel.weight - newWeight) > 0.01f) // 避免微小变化
                {
                    blendShapeChannel.weight = newWeight;
                    weightsUpdated = true;
                    Debug.Log($"[LiveLink] 更新BlendShape权重: {curve.Name} = {newWeight}");
                }
            }
            else
            {
                Debug.LogWarning($"[LiveLink] 未找到对应的BlendShape通道: {curve.Name}");
            }
        }
        
        // 如果权重有更新，应用到模型
        if (weightsUpdated)
        {
            ApplyWeightsToModel();
        }
    }
    /// <summary>
    /// 检查字符串是否为有效的JSON格式
    /// </summary>
    private bool IsValidJson(string jsonString)
    {
        jsonString = jsonString.Trim();
        return (jsonString.StartsWith("{") && jsonString.EndsWith("}")) ||
               (jsonString.StartsWith("[") && jsonString.EndsWith("]"));
    }
    


    void OnDisable()
    {
        // 取消注册，防止内存泄漏
        if (webSocketServerForHumen != null)
        {
            webSocketServerForHumen.MessageReceived -= ReceivedMessage;
        }
        
        // 清理映射表，避免在对象销毁后仍然持有引用
        if (blendShapeShortNameMap != null)
        {
            blendShapeShortNameMap.Clear();
        }
    }

   


    // Update is called once per frame
    void Update()
    {
        
    }

     private void OnDestroy()
    {
        OnDisable();
    }
}

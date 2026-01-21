import asyncio
import websockets
import json
import random

# 表情通道名称列表
blend_shape_names = [
    "browDownLeft",
    "browDownRight",
    "browInnerUp",
    "browOuterUpLeft",
    "browOuterUpRight",
    "cheekPuff",
    "cheekSquintLeft",
    "cheekSquintRight",
    "eyeBlinkLeft",
    "eyeBlinkRight",
    "eyeLookDownLeft",
    "eyeLookDownRight",
    "eyeLookInLeft",
    "eyeLookInRight",
    "eyeLookOutLeft",
    "eyeLookOutRight",
    "eyeLookUpLeft",
    "eyeLookUpRight",
    "eyeSquintLeft",
    "eyeSquintRight",
    "eyeWideLeft",
    "eyeWideRight",
    "jawForward",
    "jawLeft",
    "jawOpen",
    "jawRight",
    "mouthClose",
    "mouthDimpleLeft",
    "mouthDimpleRight",
    "mouthFrownLeft",
    "mouthFrownRight",
    "mouthFunnel",
    "mouthLeft",
    "mouthLowerDownLeft",
    "mouthLowerDownRight",
    "mouthPressLeft",
    "mouthPressRight",
    "mouthPucker",
    "mouthRight",
    "mouthRollLower",
    "mouthRollUpper",
    "mouthShrugLower",
    "mouthShrugUpper",
    "mouthSmileLeft",
    "mouthSmileRight",
    "mouthStretchLeft",
    "mouthStretchRight",
    "mouthUpperUpLeft",
    "mouthUpperUpRight",
    "noseSneerLeft",
    "noseSneerRight",
    "tongueOut"
]

async def send_hello_message():
    uri = "ws://localhost:8080"
    try:
        async with websockets.connect(uri) as websocket:
            print("已连接到WebSocket服务端")
            while True:
                # 生成随机表情值的CurveArray
                curve_array = []
                for name in blend_shape_names:
                    curve_array.append({"Name": name, "Value": random.uniform(0,0.3)})
                
                # 定义要发送的JSON消息
                message_data = {
                    "V8": {
                        "BoneArray": [
                            {"Name":"Hips","Parent":"None","Location":[0.0,0.0,0.0],"Rotation":[-0.01,1.0,0.01,0.05]},
                            {"Name":"Spine","Parent":"Hips","Location":[-0.0,75.76,0.0],"Rotation":[-0.04,-0.01,-0.0,1.0]},
                            {"Name":"Spine1","Parent":"Spine","Location":[-0.0,164.8,0.0],"Rotation":[-0.01,-0.0,0.01,1.0]},
                            {"Name":"Neck","Parent":"Spine1","Location":[-0.0,183.28,0.0],"Rotation":[0.04,-0.02,-0.02,1.0]},
                            {"Name":"Head","Parent":"Neck","Location":[-0.0,143.21,18.48],"Rotation":[0.0,-0.02,-0.01,1.0]},
                            {"Name":"LeftShoulder","Parent":"Spine1","Location":[36.59,144.4,-2.04],"Rotation":[0.03,0.03,0.13,0.99]},
                            {"Name":"LeftArm","Parent":"LeftShoulder","Location":[117.79,0.0,0.0],"Rotation":[0.05,-0.03,-0.14,0.99]},
                            {"Name":"LeftForeArm","Parent":"LeftArm","Location":[266.45,0.0,0.0],"Rotation":[-0.02,-0.03,0.02,1.0]},
                            {"Name":"LeftHand","Parent":"LeftForeArm","Location":[266.61,0.0,0.0],"Rotation":[-0.01,-0.06,-0.01,1.0]},
                            {"Name":"RightShoulder","Parent":"Spine1","Location":[-37.24,144.4,-2.04],"Rotation":[0.0,0.02,-0.16,0.99]},
                            {"Name":"RightArm","Parent":"RightShoulder","Location":[-117.79,0.0,0.0],"Rotation":[0.05,-0.02,0.14,0.99]},
                            {"Name":"RightForeArm","Parent":"RightArm","Location":[-266.45,0.0,0.0],"Rotation":[-0.05,0.01,0.0,1.0]},
                            {"Name":"RightHand","Parent":"RightForeArm","Location":[-266.61,0.0,0.0],"Rotation":[-0.06,0.02,-0.02,1.0]},
                            {"Name":"LeftUpLeg","Parent":"Hips","Location":[92.4,0.0,0.0],"Rotation":[0.03,0.04,-0.04,1.0]},
                            {"Name":"LeftLeg","Parent":"LeftUpLeg","Location":[-0.0,-383.75,0.0],"Rotation":[-0.06,0.1,0.02,0.99]},
                            {"Name":"LeftFoot","Parent":"LeftLeg","Location":[-0.0,-363.85,0.0],"Rotation":[-0.0,0.12,-0.01,0.99]},
                            {"Name":"LeftToeBase","Parent":"LeftFoot","Location":[-0.0,-60.06,138.59],"Rotation":[0.0,0.0,0.0,1.0]},
                            {"Name":"RightUpLeg","Parent":"Hips","Location":[-92.4,0.0,0.0],"Rotation":[0.04,-0.03,0.01,1.0]},
                            {"Name":"RightLeg","Parent":"RightUpLeg","Location":[-0.0,-383.75,0.0],"Rotation":[-0.05,-0.04,0.0,1.0]},
                            {"Name":"RightFoot","Parent":"RightLeg","Location":[-0.0,-363.85,0.0],"Rotation":[-0.03,-0.14,0.0,0.99]},
                            {"Name":"RightToeBase","Parent":"RightFoot","Location":[-0.0,-60.06,138.59],"Rotation":[0.0,0.0,0.0,1.0]}
                        ],
                        "Expression": curve_array,
                        "Content": [
                                   {"Speaking":"true"},
                                   {"Text":"你好"}
                                   ],
                        "Order": [
                                   {"AnimOrder":"招手"},
                                   {"Order2":"其他"}
                                   ],

                }}
                # 将字典转换为JSON字符串
                message = json.dumps(message_data)
                await websocket.send(message)
                print("发送JSON消息成功")
                # 可以打印消息的前100个字符作为预览
                print(f"消息预览: {message[:100]}...")
                await asyncio.sleep(0.03)
    except Exception as e:
        print(f"连接错误: {e}")
        # 尝试重新连接
        await asyncio.sleep(3)
        await send_hello_message()

if __name__ == "__main__":
    asyncio.run(send_hello_message())

import asyncio
import websockets

async def send_hello_message():
    uri = "ws://localhost:8080"
    try:
        async with websockets.connect(uri) as websocket:
            print("已连接到WebSocket服务端")
            while True:
                message = "你好"
                await websocket.send(message)
                print(f"发送消息: {message}")
                await asyncio.sleep(1)
    except Exception as e:
        print(f"连接错误: {e}")
        # 尝试重新连接
        await asyncio.sleep(3)
        await send_hello_message()

if __name__ == "__main__":
    asyncio.run(send_hello_message())

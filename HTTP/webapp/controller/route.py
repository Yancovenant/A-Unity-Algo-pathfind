#webapp.controller.route.py
# Updated to support WebSocket streaming from Unity instead of POST polling

from werkzeug.wrappers import Response, Request
from webapp.router import route
from ..layout import render_layout
from ..model.yolo_augv import agent_queues, agent_state, AgentYoloThread

import json, os, time, traceback, threading, base64, numpy as np
import asyncio
import websockets

IMAGE_SAVE_DIR = os.path.join(os.getcwd(), "debug_yolo_images")
os.makedirs(IMAGE_SAVE_DIR, exist_ok=True)

@route("/home", methods=['GET'])
def handle_home(request: Request):
    return Response(render_layout("web_layout.xml"), mimetype="text/html")

@route("/api/yolo/stream/<agent_id>", methods=['POST'])
def handle_yolo(request: Request, agent_id: str):
    if agent_id not in agent_queues:
        AgentYoloThread(agent_id).start()
        #return Response(json.dumps({"error": "unknown agent"}), status=404)
    try:
        data = request.get_data()
        width = int(request.headers.get("Width", 160))
        height = int(request.headers.get("Height", 160))

        frame = np.frombuffer(data, dtype=np.uint8).reshape((height, width, 3)).copy()

        q = agent_queues[agent_id]
        if not q.full():
            q.put(frame)
        
        return Response("OK", status=200)

    except Exception as e:
        traceback.print_exc()
        return Response(json.dumps({"error": str(e)}), status=500, mimetype="application/json")

@route("/api/yolo/check_all", methods=['GET'])
def handle_check_all(request: Request):
    return Response(json.dumps(agent_state), mimetype="application/json")

@route("/api/yolo/image/<agent_id>", methods=["GET"])
def stream_yolo_image(request: Request, agent_id: str):
    path = os.path.join(IMAGE_SAVE_DIR, f"{agent_id}.jpg")
    if not os.path.exists(path):
        return Response("Image not found", status=404)
    with open(path, "rb") as f:
        return Response(f.read(), content_type="image/jpeg")

@route("/api/yolo/monitor", methods=["GET"])
def monitor_yolo_all(request: Request):
    html = """
    <html><head>
        <style>
            body { font-family: sans-serif; background: #f9f9f9; padding: 10px; }
            .agent { display: inline-block; margin: 10px; padding: 10px; background: #fff; border-radius: 5px; box-shadow: 0 0 5px rgba(0,0,0,0.1); }
            .status-safe { color: green; font-weight: bold; }
            .status-blocked { color: red; font-weight: bold; }
            .status-error { color: orange; font-weight: bold; }
            img { border: 1px solid #ccc; width: 160px; height: 120px; }
        </style>
    </head><body>
    <h2>YOLO Monitor</h2>
    """

    for agent_id in sorted(agent_state.keys()):
        info = agent_state[agent_id]
        status = info.get("status", "unknown")
        css_class = f"status-{status}"

        html += f"""
        <div class="agent">
            <div><strong>{agent_id}</strong></div>
            <div class="{css_class}">{status}</div>
            <img src="/api/yolo/image/{agent_id}?t={time.time()}">
        </div>
        """

    html += "</body></html>"
    return Response(html, mimetype="text/html")

connected_agents = {}

def receive_image(agent_id: str, base64_img: str):
    try:
        if agent_id not in agent_queues:
            AgentYoloThread(agent_id).start()

        decoded = base64.b64decode(base64_img)
        arr = np.frombuffer(decoded, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)

        q = agent_queues[agent_id]
        if not q.full():
            q.put(img)
    except Exception as e:
        print(f"[receive_image error] {agent_id}: {e}")
        traceback.print_exc()

async def handle_ws(websocket):
    try:
        path = websocket.path
        _, _, agent_id = path.strip("/").split("/")  # e.g. /ws/yolo/AUGV_1

        print(f"[WebSocket] Agent {agent_id} connected")
        AgentYoloThread(agent_id).start()
        connected_agents[agent_id] = websocket

        async for message in websocket:
            try:
                data = json.loads(message)
                img_b64 = data.get("image")
                if img_b64:
                    receive_image(agent_id, img_b64)
            except Exception as e:
                print(f"[WebSocket error] {agent_id}: {e}")

    except Exception as e:
        print(f"[WebSocket Connection Error] {e}")

    finally:
        if agent_id in connected_agents:
            del connected_agents[agent_id]
        print(f"[WebSocket] Agent {agent_id} disconnected")

async def start_ws_server():
    print("[WebSocket] Starting on ws://localhost:9999/ws/yolo/<agent_id>")
    async with websockets.serve(handle_ws, "localhost", 9999):
        await asyncio.Future()  # run forever

def start_ws_thread_once():
    if not getattr(start_ws_thread_once, "started", False):
        start_ws_thread_once.started = True
        threading.Thread(target=lambda: asyncio.run(start_ws_server()), daemon=True).start()

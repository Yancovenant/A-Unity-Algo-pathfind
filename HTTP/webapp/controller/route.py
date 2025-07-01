#webapp.controller.route.py

from werkzeug.wrappers import Response, Request
from webapp.router import route
from ..layout import render_layout
from ..model.yolo_augv import agent_queues, agent_state, AgentYoloThread

import json
import os
import time
import traceback
import numpy as np

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
        <meta http-equiv="refresh" content="1">
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

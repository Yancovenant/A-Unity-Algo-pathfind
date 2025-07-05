# asgi_app.py
# Starlette ASGI app for AUGV YOLO backend: handles agent/monitor websockets, HTTP endpoints, and resource logging.

import os, asyncio, json, logging, psutil, threading, time, numpy as np, cv2, queue, traceback
from starlette.applications import Starlette
from starlette.responses import HTMLResponse, JSONResponse
from starlette.routing import Route, WebSocketRoute
from starlette.staticfiles import StaticFiles
from starlette.websockets import WebSocket, WebSocketDisconnect
from starlette.requests import Request
from webapp.model.yolo_augv import AgentYoloThread, agent_queues, agent_state

# Logging setup
logging.basicConfig(level=logging.INFO, format='[%(asctime)s] %(levelname)s: %(message)s')
logger = logging.getLogger("asgi_app")

# In-memory state
agent_frames = {}  # {agent_id: bytes}
monitor_clients = set()  # Set[WebSocket]
HEARTBEAT_INTERVAL, HEARTBEAT_TIMEOUT = 20, 40

# Resource usage logging
log_resource_usage = lambda: logger.info(f"[RESOURCE] Memory: {psutil.Process(os.getpid()).memory_info().rss/1024/1024:.2f} MB, Monitor clients: {len(monitor_clients)}, Agents: {len(agent_frames)}")

def log_agent_event(agent_id, msg): logger.info(f"[AUGV {agent_id}] {msg}")
def log_monitor_event(msg): logger.info(f"[MONITOR] {msg}")

# WebSocket endpoint for Unity agents
async def augv_ws(websocket: WebSocket):
    agent_id = websocket.path_params["agent_id"]
    await websocket.accept()
    log_agent_event(agent_id, "connected")
    if agent_id not in agent_queues: AgentYoloThread(agent_id).start()
    AGENT_BROADCAST_INTERVAL, last_sent = 0.2, 0
    try:
        while True:
            data = await websocket.receive_bytes()
            agent_frames[agent_id] = data
            try:
                frame_np = np.frombuffer(data, dtype=np.uint8)
                frame = cv2.imdecode(frame_np, cv2.IMREAD_COLOR)
                if frame is not None and not agent_queues[agent_id].full():
                    agent_queues[agent_id].put_nowait(frame)
            except Exception as e:
                log_agent_event(agent_id, f"frame decode error: {e}")
            now = time.time()
            if now >= last_sent:
                last_sent = now
                header = json.dumps({
                    "agent_id": agent_id,
                    "detections": agent_state.get(agent_id, {}).get("detections", []),
                    "road_outline": agent_state.get(agent_id, {}).get("road_outline", [])
                }).encode() + b'\n'
                payload = header + data
                send_tasks = [client.send_bytes(payload) for client in list(monitor_clients)]
                results = await asyncio.gather(*send_tasks, return_exceptions=True)
                for client, result in zip(list(monitor_clients), results):
                    if isinstance(result, Exception):
                        log_monitor_event(f"Client send failed: {result}\n{traceback.format_exc()}")
                        monitor_clients.discard(client)
            if len(agent_frames) % 10 == 0: log_resource_usage()
    except WebSocketDisconnect:
        log_agent_event(agent_id, "disconnected (WebSocketDisconnect)")
    except Exception as e:
        log_agent_event(agent_id, f"disconnected (Exception): {e}\n{traceback.format_exc()}")
    finally:
        log_agent_event(agent_id, "connection closed")
        agent_frames.pop(agent_id, None)

# WebSocket endpoint for frontend monitor
async def monitor_ws(websocket: WebSocket):
    await websocket.accept()
    monitor_clients.add(websocket)
    log_monitor_event(f"client connected (total: {len(monitor_clients)})")
    try:
        for agent_id, frame in agent_frames.items():
            try:
                header = json.dumps({"agent_id": agent_id}).encode() + b'\n'
                await websocket.send_bytes(header + frame)
            except WebSocketDisconnect:
                log_monitor_event("monitor disconnected (WebSocketDisconnect)")
            except Exception as e:
                log_monitor_event(f"failed to send initial frame for {agent_id}: {e}\n{traceback.format_exc()}")
        while True: await asyncio.sleep(10)
    except WebSocketDisconnect:
        log_monitor_event("client disconnected (WebSocketDisconnect)")
    except Exception as e:
        log_monitor_event(f"client disconnected (Exception): {e}\n{traceback.format_exc()}")
    finally:
        monitor_clients.discard(websocket)
        log_monitor_event(f"client removed (total: {len(monitor_clients)})")
        log_resource_usage()

# HTTP endpoint for /monitor
async def monitor(request: Request):
    EXPECTED_AGENTS = [f"AUGV_{i}" for i in range(1, 6)]
    agents = list(set(agent_frames.keys()) | set(EXPECTED_AGENTS))
    html = """
    <html><head>
    <title>YOLO Monitor</title>
    <style>
    body { font-family: sans-serif; background: #f9f9f9; padding: 10px; }
    .agent { display: inline-block; margin: 10px; padding: 10px; background: #fff; border-radius: 5px; box-shadow: 0 0 5px rgba(0,0,0,0.1); }
    .agent-name { font-weight: bold; margin-bottom: 5px; }
    canvas { border: 1px solid #ccc; width: 160px; height: 120px; display: block; }
    </style>
    </head><body>
    <h2>YOLO Monitor</h2>
    <div id="agents">
    """
    for agent in agents:
        html += f'''
        <div class="agent" id="agent-{agent}">
            <div class="agent-name">{agent}</div>
            <canvas id="canvas-{agent}" width="640" height="480"></canvas>
        </div>
        '''
    html += """
    </div>
    <script src="/static/src/js/monitor.js"></script>
    </body></html>
    """
    return HTMLResponse(html)

# Health check endpoint
async def health(request: Request):
    return JSONResponse({
        "status": "ok",
        "agents": list(agent_frames.keys()),
        "monitors": len(monitor_clients),
        "yolo": {agent_id: state for agent_id, state in agent_state.items() if isinstance(state, dict)}
    })

STATIC_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), "static")

routes = [
    WebSocketRoute("/ws/augv/{agent_id}", augv_ws),
    WebSocketRoute("/ws/monitor", monitor_ws),
    Route("/monitor", monitor, methods=["GET"]),
    Route("/health", health, methods=["GET"]),
]

app = Starlette(debug=False, routes=routes)
app.mount("/static", StaticFiles(directory=STATIC_DIR, html=True), name="static")

def log_resources():
    while True:
        p = psutil.Process(os.getpid())
        print(f"[RESOURCE] Mem: {p.memory_info().rss/1024/1024:.2f}MB, FDs: {p.num_fds() if hasattr(p, 'num_fds') else 'N/A'}")
        time.sleep(10)

threading.Thread(target=log_resources, daemon=True).start() 
# asgi_app.py
# Starlette ASGI app for AUGV YOLO backend: handles agent/monitor websockets, HTTP endpoints, and resource logging.

import os, asyncio, json, logging, psutil, threading, time, numpy as np, cv2, queue, traceback
from starlette.applications import Starlette
from starlette.responses import HTMLResponse, JSONResponse
from starlette.routing import Route, WebSocketRoute
from starlette.staticfiles import StaticFiles
from starlette.websockets import WebSocket, WebSocketDisconnect
from starlette.requests import Request

from webapp.tools.render import render_layout
# from webapp.model.yolo_augv import AgentYoloThread, agent_queues, agent_state

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
# async def augv_ws(websocket: WebSocket):
#     agent_id = websocket.path_params["agent_id"]
#     await websocket.accept()
#     log_agent_event(agent_id, "connected")
#     if agent_id not in agent_queues: AgentYoloThread(agent_id).start()
#     AGENT_BROADCAST_INTERVAL, last_sent = 0.2, 0
#     try:
#         while True:
#             data = await websocket.receive_bytes()
#             agent_frames[agent_id] = data
#             try:
#                 frame_np = np.frombuffer(data, dtype=np.uint8)
#                 frame = cv2.imdecode(frame_np, cv2.IMREAD_COLOR)
#                 if frame is not None and not agent_queues[agent_id].full():
#                     agent_queues[agent_id].put_nowait(frame)
#             except Exception as e:
#                 log_agent_event(agent_id, f"frame decode error: {e}")
#             now = time.time()
#             if now >= last_sent:
#                 last_sent = now
#                 header = json.dumps({
#                     "agent_id": agent_id,
#                     "detections": agent_state.get(agent_id, {}).get("detections", []),
#                     "road_outline": agent_state.get(agent_id, {}).get("road_outline", [])
#                 }).encode() + b'\n'
#                 payload = header + data
#                 send_tasks = [client.send_bytes(payload) for client in list(monitor_clients)]
#                 results = await asyncio.gather(*send_tasks, return_exceptions=True)
#                 for client, result in zip(list(monitor_clients), results):
#                     if isinstance(result, Exception):
#                         log_monitor_event(f"Client send failed: {result}\n{traceback.format_exc()}")
#                         monitor_clients.discard(client)
#             if len(agent_frames) % 10 == 0: log_resource_usage()
#     except WebSocketDisconnect:
#         log_agent_event(agent_id, "disconnected (WebSocketDisconnect)")
#     except Exception as e:
#         log_agent_event(agent_id, f"disconnected (Exception): {e}\n{traceback.format_exc()}")
#     finally:
#         log_agent_event(agent_id, "connection closed")
#         agent_frames.pop(agent_id, None)

# # WebSocket endpoint for frontend monitor
# async def monitor_ws(websocket: WebSocket):
#     await websocket.accept()
#     monitor_clients.add(websocket)
#     log_monitor_event(f"client connected (total: {len(monitor_clients)})")
#     try:
#         for agent_id, frame in agent_frames.items():
#             try:
#                 header = json.dumps({"agent_id": agent_id}).encode() + b'\n'
#                 await websocket.send_bytes(header + frame)
#             except WebSocketDisconnect:
#                 log_monitor_event("monitor disconnected (WebSocketDisconnect)")
#             except Exception as e:
#                 log_monitor_event(f"failed to send initial frame for {agent_id}: {e}\n{traceback.format_exc()}")
#         while True: await asyncio.sleep(10)
#     except WebSocketDisconnect:
#         log_monitor_event("client disconnected (WebSocketDisconnect)")
#     except Exception as e:
#         log_monitor_event(f"client disconnected (Exception): {e}\n{traceback.format_exc()}")
#     finally:
#         monitor_clients.discard(websocket)
#         log_monitor_event(f"client removed (total: {len(monitor_clients)})")
#         log_resource_usage()

# HTTP endpoint for /monitor
async def monitor(request: Request):
    EXPECTED_AGENTS = [f"AUGV_{i}" for i in range(1, 6)]
    agents = list(set(agent_frames.keys()) | set(EXPECTED_AGENTS))
    with open("static/xml/page_monitor.xml", "r", encoding="utf-8") as f:
        base_template = f.read()
    agents_monitor = ""
    for agent in agents:
        agents_monitor += f'''
        <div class="col-6 col-md-4 col-lg-3">
            <div class="agent" id="agent-{agent}">
                <div class="agent-name">{agent}</div>
                <canvas id="canvas-{agent}" width="640" height="480"></canvas>
            </div>
        </div>
        '''
    content = base_template.replace("<t t-agents/>", agents_monitor)
    return render_layout("Yolo Monitor", content)

from .tools.render import render_layout

async def map(request: Request):
    with open("static/xml/page_map.xml", "r", encoding="utf-8") as f:
        template = f.read()
    return render_layout("Map Editor", template)

async def home(request: Request):
    with open("static/xml/page_home.xml", "r", encoding="utf-8") as f:
        content = f.read()
    return render_layout("Home", content)

async def not_found(request: Request, exc):
    with open("static/xml/page_404.xml", "r", encoding="utf-8") as f:
        content = f.read()
    return render_layout("Page Not Found", content)

from starlette.middleware.errors import ServerErrorMiddleware
from starlette.exceptions import HTTPException

# Health check endpoint
# async def health(request: Request):
#     return JSONResponse({
#         "status": "ok",
#         "agents": list(agent_frames.keys()),
#         "monitors": len(monitor_clients),
#         "yolo": {agent_id: state for agent_id, state in agent_state.items() if isinstance(state, dict)}
#     })

STATIC_DIR = os.path.join(os.path.dirname(os.path.dirname(__file__)), "static")

routes = [
    # WebSocketRoute("/ws/augv/{agent_id}", augv_ws),
    # WebSocketRoute("/ws/monitor", monitor_ws),
    Route("/monitor", monitor, methods=["GET"]),
    # Route("/health", health, methods=["GET"]),
    Route("/map", map, methods=["GET"]),
    Route("/", home, methods=["GET"]),
]

app = Starlette(debug=False, routes=routes)
app.add_exception_handler(404, not_found)
app.mount("/static", StaticFiles(directory=STATIC_DIR, html=True), name="static")

def log_resources():
    while True:
        p = psutil.Process(os.getpid())
        print(f"[RESOURCE] Mem: {p.memory_info().rss/1024/1024:.2f}MB, FDs: {p.num_fds() if hasattr(p, 'num_fds') else 'N/A'}")
        time.sleep(10)

threading.Thread(target=log_resources, daemon=True).start() 

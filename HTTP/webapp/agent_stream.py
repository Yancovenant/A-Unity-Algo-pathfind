import asyncio
import threading
from starlette.websockets import WebSocket
from typing import Dict, Set, Any

# In-memory, thread-safe storage for latest agent frames
class AgentFrameStore:
    def __init__(self):
        self.frames: Dict[str, bytes] = {}
        self.lock = threading.Lock()
        self.clients: Set[WebSocket] = set()
        self.agent_last_seen: Dict[str, float] = {}  # For auto-discovery

    def set_frame(self, agent_id: str, frame: bytes):
        with self.lock:
            self.frames[agent_id] = frame
            import time
            self.agent_last_seen[agent_id] = time.time()

    def get_frame(self, agent_id: str) -> bytes:
        with self.lock:
            return self.frames.get(agent_id)

    def get_agents(self):
        with self.lock:
            import time
            now = time.time()
            # Only return agents seen in the last 10s
            return [aid for aid, ts in self.agent_last_seen.items() if now - ts < 10]

    def register_client(self, ws: WebSocket):
        with self.lock:
            self.clients.add(ws)

    def unregister_client(self, ws: WebSocket):
        with self.lock:
            self.clients.discard(ws)

    def get_clients(self):
        with self.lock:
            return list(self.clients)

AGENT_FRAMES = AgentFrameStore()

# WebSocket endpoint for Unity agents to send frames
def ws_unity_agent(agent_id: str):
    async def handler(websocket: WebSocket):
        await websocket.accept()
        try:
            while True:
                data = await websocket.receive_bytes()
                AGENT_FRAMES.set_frame(agent_id, data)
                # Broadcast to all frontend clients
                for client in AGENT_FRAMES.get_clients():
                    try:
                        await client.send_bytes(data)
                    except Exception:
                        pass
        except Exception:
            pass
    return handler

# WebSocket endpoint for frontend clients to receive all agent frames
def ws_monitor():
    async def handler(websocket: WebSocket):
        await websocket.accept()
        AGENT_FRAMES.register_client(websocket)
        try:
            while True:
                await asyncio.sleep(60)  # Keep alive
        except Exception:
            pass
        finally:
            AGENT_FRAMES.unregister_client(websocket)
    return handler

# Utility for use in controllers
get_active_agents = AGENT_FRAMES.get_agents
get_agent_frame = AGENT_FRAMES.get_frame 
# yolo_augv.py
# Threaded YOLO detection for each agent. Receives frames, runs YOLO, computes relative (dx, dy) grid offset, sends to Unity.

import threading, queue, os, numpy as np, cv2, base64, math, json, time
from ultralytics import YOLO
import socket

# Global state for agent communication
agent_queues, agent_state = {}, {}
IMAGE_SAVE_DIR = os.path.join(os.getcwd(), "debug_yolo_images"); os.makedirs(IMAGE_SAVE_DIR, exist_ok=True)
ALLOWED_CLASSES = {"person"}
DEBUG = True
ROAD_LOWER, ROAD_UPPER = np.array([240,40,0]), np.array([255,70,30])

# Camera parameters (fixed relative to agent)
CAMERA_HEIGHT = 0.6  # meters
CAMERA_FORWARD = 0.31  # meters (relative to node center 0.5)
CAMERA_ROT_X = 20  # degrees (downward)
FOV = 90  # degrees (vertical)
IMG_W, IMG_H = 640, 480  # assumed default
GRID_SIZE = 1.0  # 1x1 meter per node
NODE_CENTER = 0.5  # Center of node in Unity units

# Unity feedback socket (TCP for reliability)
UNITY_HOST, UNITY_PORT = "localhost", 8051

def send_obstacle_data_to_unity(agent_id, blocked_offsets):
    """Send obstacle data to Unity via TCP connection."""
    try:
        # Create a new connection for each message
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as unity_sock:
            unity_sock.connect((UNITY_HOST, UNITY_PORT))
            msg = json.dumps({
                "action": "obstacle",
                "data": {
                    "agent_id": agent_id, 
                    "blocked_offsets": list(blocked_offsets)
                }
            })
            unity_sock.send(msg.encode())
            print(f"Sent obstacle data to Unity: {msg}")
    except Exception as e:
        print(f"Failed to send obstacle data to Unity: {e}")

def image_to_agent_grid_offset(x_img, y_img, img_w, img_h, bbox_h):
    """Project image pixel (x_img, y_img) to (dx, dy) grid offset relative to agent/camera. Use distance-based bias for dy."""
    # Convert to normalized device coordinates
    x_ndc = (x_img / img_w - 0.5) * 2
    y_ndc = (y_img / img_h - 0.5) * 2
    # Camera intrinsic
    fov_rad = math.radians(FOV)
    aspect = img_w / img_h
    tan_fov = math.tan(fov_rad / 2)
    # Ray in camera space
    x_cam = x_ndc * tan_fov * aspect
    y_cam = -y_ndc * tan_fov
    z_cam = 1
    # Rotate by camera tilt (X axis)
    rot_x = math.radians(CAMERA_ROT_X)
    y_rot = y_cam * math.cos(rot_x) - z_cam * math.sin(rot_x)
    z_rot = y_cam * math.sin(rot_x) + z_cam * math.cos(rot_x)
    # Ray origin in agent-local space (camera at (0, CAMERA_HEIGHT, NODE_CENTER + CAMERA_FORWARD))
    cam_y = CAMERA_HEIGHT
    cam_z = NODE_CENTER + CAMERA_FORWARD
    # Intersect with ground plane Y=0
    t = -cam_y / y_rot if y_rot != 0 else 0
    world_x = x_cam * t
    world_z = cam_z + z_rot * t
    
    # Calculate distance from camera to object
    distance = math.sqrt(world_x**2 + world_z**2)
    
    # Distance-based bias: adjust bias based on how far the object is
    if distance <= 2.0:
        # Close objects (1-2 nodes): small bias to avoid overestimation
        bias = 0.2
    elif distance <= 4.0:
        # Medium objects (3-4 nodes): current bias that works well
        bias = 0.5
    elif distance <= 6.0:
        # Far objects (5-6 nodes): larger bias to compensate for perspective
        bias = 0.8
    else:
        # Very far objects (7+ nodes): even larger bias
        bias = 1.2
    
    dy = int(round((world_z + bias) / GRID_SIZE))
    dx = int(round(world_x / GRID_SIZE))
    #print(f"feet_pixel: ({x_img:.2f}, {y_img:.2f}), distance: {distance:.2f}, world: ({world_x:.2f}, {world_z:.2f}), bias: {bias:.2f}, offset: ({dx}, {dy})")
    return dx, dy

class AgentYoloThread(threading.Thread):
    """Thread for each agent: receives frames, runs YOLO, updates state, sends (dx, dy) to Unity."""
    def __init__(self, agent_id):
        super().__init__(daemon=True)
        self.agent_id = agent_id
        self.q = queue.Queue(maxsize=1)
        agent_queues[agent_id] = self.q
        agent_state[agent_id] = {'status': 'waiting', 'detections': []}
        self.last_send_time = 0
        self.last_sent_offsets = set()

    def run(self):
        try:
            model = YOLO("yolo11n-seg.pt")
            #print(f"[YOLO] Model loaded for {self.agent_id}: {model.names}")
        except Exception as e:
            agent_state[self.agent_id] = {'status': 'error', 'error': str(e)}
            return
        while True:
            try:
                frame = self.q.get()
                if frame is None: continue
                print(f"[AUGV {self.agent_id}] Frame received. Shape: {frame.shape}")
                now = time.time()
                image = np.ascontiguousarray(frame)
                img_h, img_w = image.shape[:2]
                results = model.predict(image, conf=0.4, verbose=False)[0]
                road_mask = cv2.inRange(image, ROAD_LOWER, ROAD_UPPER)
                contours, _ = cv2.findContours(road_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
                road_outline = []
                if contours:
                    largest = max(contours, key=cv2.contourArea)
                    largest = largest.squeeze() if largest.ndim == 3 else largest
                    if len(largest.shape) == 2: road_outline = [[int(pt[0]), int(pt[1])] for pt in largest]
                detections = []
                blocked_offsets = set()
                for box in getattr(results, "boxes", []):
                    cls_id = int(box.cls[0]); label = model.names[cls_id]
                    if label != "person": continue
                    conf = float(box.conf[0]); xywh = box.xywh[0].tolist()
                    x, y, w, h = xywh
                    # Bottom center of bbox (feet)
                    feet_x = x
                    feet_y = y + h/2
                    dx, dy = image_to_agent_grid_offset(feet_x, feet_y, img_w, img_h, h)
                    blocked_offsets.add((dx, dy) if dy > 0 and dy <= 5 else None)
                    detections.append({"label": label, "confidence": round(conf, 3), "bbox": [round(v,2) for v in xywh], "feet": [feet_x, feet_y], "offset": [dx, dy]})
                    if DEBUG:
                        color = (0,0,255)
                        x1, y1, x2, y2 = map(int, [x-w/2, y-h/2, x+w/2, y+h/2])
                        cv2.rectangle(image, (x1, y1), (x2, y2), color, 1)
                        cv2.putText(image, f"{label} {conf:.2f}", (x1, y1-4), cv2.FONT_HERSHEY_SIMPLEX, 0.3, color, 1)
                        cv2.circle(image, (int(feet_x), int(feet_y)), 3, (255,0,0), -1)
                if DEBUG:
                    cv2.drawContours(image, contours, -1, (0,255,255), 1)
                    cv2.imwrite(os.path.join(IMAGE_SAVE_DIR, f"{self.agent_id}.jpg"), image)
                # Send (dx, dy) offsets to Unity via TCP
                if blocked_offsets and (
                    now - self.last_send_time >= 0.5 or
                    blocked_offsets != self.last_sent_offsets
                ):
                    print(f"blocked offsets: {blocked_offsets}")
                    send_obstacle_data_to_unity(self.agent_id, blocked_offsets)
                    self.last_send_time = now
                    self.last_sent_offsets = blocked_offsets.copy()
                agent_state[self.agent_id] = {
                    'status': 'blocked' if blocked_offsets else 'safe',
                    'detections': detections,
                    'road_outline': road_outline,
                    'blocked_offsets': list(blocked_offsets)
                }
            except Exception as e:
                agent_state[self.agent_id] = {'status': 'error', 'error': str(e)}
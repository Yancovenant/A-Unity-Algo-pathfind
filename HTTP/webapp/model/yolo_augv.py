#webapp.model.yolo_augv

import threading
import queue
import os
import numpy as np
import cv2
import base64
from ultralytics import YOLO

# Internal state
agent_queues = {}        # { agent_id: Queue[bytes] }
agent_state = {}   # { agent_id: { "status": "safe" | "obstacle", "detections": [...] } }

# Directory to store last frames with drawings
IMAGE_SAVE_DIR = os.path.join(os.getcwd(), "debug_yolo_images")
os.makedirs(IMAGE_SAVE_DIR, exist_ok=True)

ALLOWED_CLASSES = {"person", "obstacle"}
DEBUG = True  # Set to True to save .jpg debug images

class AgentYoloThread(threading.Thread):
    def __init__(self, agent_id):
        super().__init__(daemon=True)
        self.agent_id = agent_id
        self.q = queue.Queue(maxsize=1)
        agent_queues[agent_id] = self.q
        agent_state[agent_id] = {'status': 'waiting', 'detections': []}
    
    def run(self):
        try:
            model = YOLO("yolov8n-seg.pt")
            print("[YOLO] Model classes:", model.names)
        except Exception as e:
            agent_state[self.agent_id] = {'status': 'error', 'error': str(e)}
            return
        
        while True:
            try:
                frame = self.q.get()
                if frame is None:
                    continue

                image = np.ascontiguousarray(frame)
                results = model.predict(image, classes=None, conf=0.4, verbose=False)[0]

                detections = []
                for box in getattr(results, "boxes", []):
                    cls_id = int(box.cls[0])
                    label = model.names[cls_id]
                    print(f"[YOLO] Detected: {label}")
                    """
                    if ALLOWED_CLASSES and label not in ALLOWED_CLASSES:
                        continue
                    """
                    conf = float(box.conf[0])
                    xywh = box.xywh[0].tolist()

                    detections.append({
                        "label": label,
                        "confidence": round(conf, 3),
                        "bbox": [round(v, 2) for v in xywh]
                    })

                    # Draw (for debug)
                    if DEBUG:
                        x, y, w, h = xywh
                        x1, y1 = int(x - w / 2), int(y - h / 2)
                        x2, y2 = int(x + w / 2), int(y + h / 2)
                        cv2.rectangle(image, (x1, y1), (x2, y2), (0, 255, 0), 1)
                        cv2.putText(image, f"{label} {conf:.2f}", (x1, y1 - 4),
                                    cv2.FONT_HERSHEY_SIMPLEX, 0.3, (0, 255, 0), 1)

                if DEBUG:
                    path = os.path.join(IMAGE_SAVE_DIR, f"{self.agent_id}.jpg")
                    cv2.imwrite(path, image)

                agent_state[self.agent_id] = {
                    'status': 'safe' if not detections else 'blocked',
                    'detections': detections
                }

            except Exception as e:
                agent_state[self.agent_id] = {'status': 'error', 'error': str(e)}

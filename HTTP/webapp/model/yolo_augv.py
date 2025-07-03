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

#Mask road color blue in BGR -> RGB (255,52,0)
ROAD_LOWER = np.array([240,40,0]) # BGR LOWER BOUND
ROAD_UPPER = np.array([255,70,30]) # BGR UPPER BOUND

class AgentYoloThread(threading.Thread):
    def __init__(self, agent_id):
        super().__init__(daemon=True)
        self.agent_id = agent_id
        self.q = queue.Queue(maxsize=1)
        agent_queues[agent_id] = self.q
        agent_state[agent_id] = {'status': 'waiting', 'detections': []}
    
    def run(self):
        try:
            model = YOLO("yolo11n-seg.pt")
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
                results = model.predict(image, conf=0.4, verbose=False)[0]

                #binary road mask
                road_mask = cv2.inRange(image, ROAD_LOWER, ROAD_UPPER)

                # detect road outline contours
                contours, _ = cv2.findContours(road_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
                #road_outline = [c.squeeze().tolist() for c in contours if c.shape[0] >= 3]
                #print("road_outline", road_outline)
                road_outline = []
                if contours:
                    largest = max(contours, key=cv2.contourArea)
                    if largest.ndim == 3:  # Ensure it's the right shape
                        largest = largest.squeeze()
                    if len(largest.shape) == 2:  # Confirm it’s a flat list of [x,y]
                        road_outline = [[int(pt[0]), int(pt[1])] for pt in largest]
                
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

                    x, y, w, h = xywh
                    x1, y1 = int(x - w / 2), int(y - h / 2)
                    x2, y2 = int(x + w / 2), int(y + h / 2)

                    #clip bounding box to image
                    h_img, w_img = road_mask.shape
                    x1 = max(0, min(x1, w_img - 1))
                    y1 = max(0, min(y1, h_img - 1))
                    x2 = max(0, min(x2, w_img - 1))
                    y2 = max(0, min(y2, h_img - 1))

                    crop = road_mask[y1:y2, x1:x2]
                    road_pixels = cv2.countNonZero(crop)
                    box_area = crop.size
                    on_road = box_area > 0 and (road_pixels / box_area) > 0.2

                    detections.append({
                        "label": label,
                        "confidence": round(conf, 3),
                        "bbox": [round(v, 2) for v in xywh],
                        "on_road": on_road
                    })

                    # Draw (for debug)
                    if DEBUG:
                        color = (0, 0, 255) if on_road else (0, 255, 0)
                        cv2.rectangle(image, (x1, y1), (x2, y2), color, 1)
                        cv2.putText(image, f"{label} {conf:.2f}", (x1, y1 - 4),
                                    cv2.FONT_HERSHEY_SIMPLEX, 0.3, color, 1)

                if DEBUG:
                    cv2.drawContours(image, contours, -1, (0, 255, 255), 1)
                    path = os.path.join(IMAGE_SAVE_DIR, f"{self.agent_id}.jpg")
                    cv2.imwrite(path, image)

                is_blocked = any(d['on_road'] for d in detections)
                agent_state[self.agent_id] = {
                    'status': 'blocked' if is_blocked else 'safe',
                    'detections': detections,
                    'road_outline': road_outline
                }

            except Exception as e:
                agent_state[self.agent_id] = {'status': 'error', 'error': str(e)}

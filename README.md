# Automated-Guided-Vehicles-AUGV-AStar-Navigating

`pip install ultralytics opencv-python`

```
Unity                Python (Flask/WebSocket)
 |                      |
 | -----------[WebSocket handshake]---------->
 |                      |
 | === send frame data in stream ===>         YOLO processes incoming video stream
 |                      |                          in its own thread per agent
 |                      |
 | <-- /check_all -- polling result --  lockstep only
```

## File Pathfinding.cs
**Purpose:** Implements A* pathfinding for single agent.
Key Points:
 - Finds a path from start to target using a standard A* algorithm.
 - Uses a grid, with each node having a cost.
 - Return a list of nodes representing the path.

## Pathcoordinator.cs
**Purpose:** The "brain" of the system, managing all agents, their paths, and resolving conflicts.
Key Points:
 - Assign routes to agents, from JSON input or by Computing the best paths.
 - Tracks "active paths" for each agent.
 - Lockstep logic: Waits for all agents to be ready before advancing.
 - Conflict Resolution: Detects when multiple agents want to occupy the same node at the same time and tries to resolve it using combinatorial scenario building.
 - Streaming: Starts camera streaming for eaech agent.
 - Visual Debug: Draws paths and conflicts in Unity's Gizmos.

## CameraCaptureWebSocket.cs
**Purpose:** Streams camera images from each agent to server using WebSockets. (ASGI)
Key Points:
 - Uses NativeWebSocket[dependancies] for robust streaming.
 - Sends JPEG frames at a target FPS.
 - Handles reconnects and heartbeats.

## RouteSocketServer.cs
**Purpose:** Receives Route assignments from an external (python) client via TCP socket.
Key Points:
 - Listens for incoming JSON route data.
 - Passes received routes to the Pathcoordinator.cs

# Algorithmic Concepts.
 - **A* Pathfinding** finds shortest path for a single agent, not inherently multi-agent aware.
 - **Multi Agnet Coordination** PathCoordinator tries to resolve conflicts by:
   - Detecting when 2 or more agents want the same node at the same time.
   - Generating scenarios (all avoid, one allowed, permutations of waiting).
   - Picking the best scenarion (least conflict, lowest cost).
 - **Lockstep Execution** All agents will wait until all ready, then move together, ensuring no one "jumps ahead of time".


# YOLO (You Only Look Once)
## Trained Classes
```
{
  "0": "person",
  "1": "bicycle",
  "2": "car",
  "3": "motorcycle",
  "4": "airplane",
  "5": "bus",
  "6": "train",
  "7": "truck",
  "8": "boat",
  "9": "traffic light",
  "10": "fire hydrant",
  "11": "stop sign",
  "12": "parking meter",
  "13": "bench",
  "14": "bird",
  "15": "cat",
  "16": "dog",
  "17": "horse",
  "18": "sheep",
  "19": "cow",
  "20": "elephant",
  "21": "bear",
  "22": "zebra",
  "23": "giraffe",
  "24": "backpack",
  "25": "umbrella",
  "26": "handbag",
  "27": "tie",
  "28": "suitcase",
  "29": "frisbee",
  "30": "skis",
  "31": "snowboard",
  "32": "sports ball",
  "33": "kite",
  "34": "baseball bat",
  "35": "baseball glove",
  "36": "skateboard",
  "37": "surfboard",
  "38": "tennis racket",
  "39": "bottle",
  "40": "wine glass",
  "41": "cup",
  "42": "fork",
  "43": "knife",
  "44": "spoon",
  "45": "bowl",
  "46": "banana",
  "47": "apple",
  "48": "sandwich",
  "49": "orange",
  "50": "broccoli",
  "51": "carrot",
  "52": "hot dog",
  "53": "pizza",
  "54": "donut",
  "55": "cake",
  "56": "chair",
  "57": "couch",
  "58": "potted plant",
  "59": "bed",
  "60": "dining table",
  "61": "toilet",
  "62": "tv",
  "63": "laptop",
  "64": "mouse",
  "65": "remote",
  "66": "keyboard",
  "67": "cell phone",
  "68": "microwave",
  "69": "oven",
  "70": "toaster",
  "71": "sink",
  "72": "refrigerator",
  "73": "book",
  "74": "clock",
  "75": "vase",
  "76": "scissors",
  "77": "teddy bear",
  "78": "hair drier",
  "79": "toothbrush"
}
```


![image](https://github.com/user-attachments/assets/e1db263f-cc92-44db-8ba2-86d8c53b5298)

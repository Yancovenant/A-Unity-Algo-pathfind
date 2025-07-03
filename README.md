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




![image](https://github.com/user-attachments/assets/e1db263f-cc92-44db-8ba2-86d8c53b5298)

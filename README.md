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

![image](https://github.com/user-attachments/assets/e1db263f-cc92-44db-8ba2-86d8c53b5298)

/**
* CameraCaptureWebSocket.cs
* Robust, production-ready camera streaming for Unity agents with heartbeat/ping.
*/

// =================================
// migrate to using NativeWebSocket Nadel, because Built In
// Microsoft Net. websocket really have some absurd connection timeout,
// and memory throttling issue on device
// =======================

using UnityEngine;
using System;
using NativeWebSocket;
//using System.Net.WebSockets;
//using System.Threading;
using System.Threading.Tasks;
using System.Collections;

public class CameraCaptureWebSocket : MonoBehaviour {
    [Header("WebSocket Settings")]
    public string agentId;
    public string host = "localhost";
    public int port = 8000;
    public int targetFps = 60; // Throttle FPS
    public int jpegQuality = 50; // JPEG quality (1-100)
    public int reconnectDelay = 2; // Seconds between reconnect attempts
    public int heartbeatInterval = 10; // Seconds between heartbeats

    private Camera cam;
    private RenderTexture rt;
    private Texture2D tex;
    private WebSocket ws;
    //private CancellationTokenSource cts;
    private Coroutine streamCoroutine;
    private string wsUrl => $"ws://{host}:{port}/ws/augv/{agentId}";

    private float frameInterval;
    private float lastSendTime;
    private float lastPingTime;
    private bool running = false;

    async void Start() {
        agentId = gameObject.name;
        Application.runInBackground = true;

        cam = GetComponentInChildren<Camera>();
        cam.forceIntoRenderTexture = true;

        rt = new RenderTexture(160, 120, 24);
        tex = new Texture2D(160, 120, TextureFormat.RGB24, false);
        cam.targetTexture = rt;

        running = true;
        frameInterval = 1f / Mathf.Max(1, targetFps);
        //streamCoroutine = StartCoroutine(StartStream());
        //await ConnectAndStream();

        ws = new WebSocket(wsUrl);

        ws.OnOpen += () => {
            Debug.Log($"[CameraWS] Connected: {wsUrl}");
            StartCoroutine(StreamLoop());
        };
        ws.OnError += (e) => Debug.LogWarning($"[CameraWS] Error: {e}");
        ws.OnClose += (e) => Debug.LogWarning($"[CameraWS] Closed with code {e}");

        //InvokeRepeating("SendStream", 0.0f, 0.3f);
        //InvokeRepeating(nameof(SendFrame), 0f, .3f);
        
        await ws.Connect();

        //_ = SendStream(); // fire-and-forget
    }
    private IEnumerator StreamLoop() {
        while (running && ws != null && ws.State == WebSocketState.Open) {
            byte[] frame = null;
            bool captureFailed = false;

            try {
                frame = CaptureFrame();
            } catch (Exception e) {
                Debug.LogWarning($"[CameraWS] Capture error: {e}");
                captureFailed = true;
            }

            if (captureFailed) {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            var sendTask = ws.Send(frame);
            while (!sendTask.IsCompleted) yield return null;

            if (sendTask.IsFaulted) {
                Debug.LogWarning($"[CameraWS] Send failed: {sendTask.Exception}");
                break; // Force disconnect
            }

            //yield return new WaitForSeconds(frameInterval);
            yield return null;
        }
        Debug.LogWarning("[CameraWS] Stream stopped. Connection likely closed.");
    }
    async void SendFrame() {
        if (!running || ws == null || ws.State != WebSocketState.Open) return;

        try {
            byte[] frame = CaptureFrame();
            await ws.Send(frame);
        } catch (Exception e) {
            Debug.LogWarning($"[CameraWS] Send error: {e}");
        }
    }

    private async void SendStream() {
        Debug.Log("[CameraWS] Streaming loop started");
        lastSendTime = Time.realtimeSinceStartup;
        lastPingTime = Time.realtimeSinceStartup;
        while (running && ws != null && ws.State == WebSocketState.Open) {
            float now = Time.realtimeSinceStartup;

            if (now - lastSendTime >= frameInterval) {
                try {
                    byte[] frame = CaptureFrame();
                    await ws.Send(frame);
                    lastSendTime = now;
                } catch (Exception e) {
                    Debug.LogWarning($"[CameraWS] Send error: {e}");
                    break; // force reconnect
                }
            }

            if (now - lastSendTime >= heartbeatInterval) {
                try {
                    await ws.Send(new byte[0]); // heartbeat
                    lastSendTime = now;
                } catch (Exception e) {
                    Debug.LogWarning($"[CameraWS] Ping error: {e}");
                    break; // force reconnect
                }
            }

            await Task.Delay(1); // let main thread breathe
        }
        Debug.LogWarning("[CameraWS] Exiting stream loop");
    }
    
    async Task ConnectAndStream() {
        while (running) {
            ws = new WebSocket(wsUrl);

            ws.OnOpen += () => Debug.Log($"[CameraWS] Connected: {wsUrl}");
            ws.OnError += (e) => Debug.LogWarning($"[CameraWS] Error: {e}");
            ws.OnClose += (e) => Debug.LogWarning($"[CameraWS] Closed with code {e}");
            
            try {
                await ws.Connect();
            } catch (Exception e) {
                Debug.LogWarning($"[CameraWS] Connect exception: {e}");
                await Task.Delay(reconnectDelay * 1000);
                continue;
            }

            lastSendTime = Time.realtimeSinceStartup;
            lastPingTime = Time.realtimeSinceStartup;
            Debug.Log("ok");
            while (running && ws.State == WebSocketState.Open) {
                float now = Time.realtimeSinceStartup;

                if (now - lastSendTime >= frameInterval) {
                    try {
                        byte[] frame = CaptureFrame();
                        await ws.Send(frame);
                        lastSendTime = now;
                    } catch (Exception e) {
                        Debug.LogWarning($"[CameraWS] Send error: {e}");
                        break; // force reconnect
                    }
                }

                if (now - lastSendTime >= heartbeatInterval) {
                    try {
                        await ws.Send(new byte[0]); // heartbeat
                        lastSendTime = now;
                    } catch (Exception e) {
                        Debug.LogWarning($"[CameraWS] Ping error: {e}");
                        break; // force reconnect
                    }
                }

                await Task.Yield(); // let main thread breathe
            }
            Debug.LogWarning($"[CameraWS] Disconnected, retrying in {reconnectDelay}s...");
            await ws.Close();
            await Task.Delay(reconnectDelay * 1000);
        }
    }
    /*
    private IEnumerator StartStream() {
        while (running) {
            ws = new WebSocket(wsUrl);

            ws.OnOpen += () => Debug.Log($"[CameraWS] Connected: {wsUrl}");
            ws.OnError += (e) => Debug.LogWarning($"[CameraWS] Error: {e}");
            ws.OnClose += (e) => Debug.LogWarning($"[CameraWS] Closed with code {e}");

            var connectTask = ws.Connect();
            yield return WaitForTask(connectTask);
            //while (!connectTask.IsCompleted) yield return null;
            
            lastSendTime = Time.realtimeSinceStartup;
            lastPingTime = Time.realtimeSinceStartup;
            Debug.Log("ok");
            while (ws.State == WebSocketState.Open && running) {
                float now = Time.realtimeSinceStartup;
                if (now - lastSendTime >= frameInterval) {
                    byte[] frame = null;
                    try {
                        frame = CaptureFrame();
                        Debug.Log($"[CameraWS] Captured frame size: {frame.Length} bytes");
                        //await ws.Send(frame);
                        //lastSendTime = now;
                    } catch (Exception e) {
                        Debug.LogWarning($"[CameraWS] Capture error: {e}");
                        break; // triggers reconnect
                    }
                    var sendTask = ws.Send(frame);
                    while (!sendTask.IsCompleted) yield return null;
                    if (sendTask.IsFaulted) {
                        Debug.LogWarning($"[CameraWS] Send failed: {sendTask.Exception}");
                        break;
                    }
                    lastSendTime = now;
                }

                if (now - lastPingTime >= heartbeatInterval) {
                    var pingTask = ws.Send(new byte[0]); // heartbeat
                    while (!pingTask.IsCompleted) yield return null;

                    if (pingTask.IsFaulted) {
                        Debug.LogWarning($"[CameraWS] Ping failed: {pingTask.Exception}");
                        break;
                    }
                    lastPingTime = now;
                }

                ws.DispatchMessageQueue();
                yield return null;
            }
            Debug.LogWarning($"[CameraWS] Disconnected. Reconnecting in {reconnectDelay}s...");
            yield return new WaitForSeconds(reconnectDelay);
        }
    }
    */

    /*
    private IEnumerator StreamLoop() {
        while (running) {
            ws = new ClientWebSocket();
            var connectTask = ws.ConnectAsync(new Uri(wsUrl), cts.Token);
            while (!connectTask.IsCompleted) yield return null;

            if (ws.State != WebSocketState.Open) {
                Debug.LogWarning($"[CameraWS] Could not connect to {wsUrl}, retrying in {reconnectDelay}s");
                yield return new WaitForSeconds(reconnectDelay);
                continue;
            }
            Debug.Log($"[CameraWS] Connected: {wsUrl}");

            float frameInterval = 1f / Mathf.Max(1, targetFps);
            var lastFrameTime = Time.realtimeSinceStartup;
            var lastSendTime = Time.realtimeSinceStartup;

            while (running && ws.State == WebSocketState.Open) {
                if (elapsed >= frameInterval) {
                    lastFrameTime = now;
                    byte[] jpg = null;
                    bool captureError = false;
                    try {
                        jpg = CaptureFrame();
                    } catch (Exception e) {
                        Debug.LogError($"[CameraWS] Capture error: {e}");
                        captureError = true;
                    }
                    if (captureError) {
                        yield return null;
                        continue;
                    }
                    bool sendError = false;
                    bool isCompleted = false;
                    try {
                        var sendTask = ws.SendAsync(new ArraySegment<byte>(jpg), WebSocketMessageType.Binary, true, cts.Token);
                        while (!sendTask.IsCompleted) isCompleted = true;
                        if (sendTask.IsFaulted || sendTask.IsCanceled) {
                            Debug.LogWarning($"[CameraWS] Send failed, reconnecting... Faulted: {sendTask.IsFaulted}, Canceled: {sendTask.IsCanceled}, Exception: {sendTask.Exception}");
                            sendError = true;
                        }
                    } catch (Exception e) {
                        Debug.LogWarning($"[CameraWS] Send exception: {e}, reconnecting...");
                        sendError = true;
                    }
                    if (sendError) {
                        break;
                    } else if (isCompleted) {
                        yield return null;
                    }
                    lastSendTime = now;
                    sentFrame = true;
                }
                *
                // Heartbeat: if no frame sent for heartbeatInterval, send a ping (empty message)
                bool isPingCompleted = false;
                if (!sentFrame && (now - lastSendTime) >= heartbeatInterval) {
                    bool pingError = false;
                    try {
                        var pingTask = ws.SendAsync(new ArraySegment<byte>(new byte[0]), WebSocketMessageType.Binary, true, cts.Token);
                        while (!pingTask.IsCompleted) isPingCompleted = true;
                        if (pingTask.IsFaulted || pingTask.IsCanceled) {
                            Debug.LogWarning($"[CameraWS] Heartbeat/ping failed, reconnecting...");
                            pingError = true;
                        }
                    } catch (Exception e) {
                        Debug.LogWarning($"[CameraWS] Heartbeat/ping exception: {e}, reconnecting...");
                        pingError = true;
                    }
                    if (pingError) {
                        break;
                    } else if (isPingCompleted) {
                        yield return null;
                    }
                    lastSendTime = now;
                }
                yield return null;
            }
            // Clean up and try to reconnect
            Debug.LogWarning($"[CameraWS] Disconnected from {wsUrl}, state: {ws.State}");
            yield return new WaitForSeconds(0.5f);
            Debug.LogWarning($"[CameraWS] After 0.5s, state: {ws.State}");
            try { ws?.Abort(); ws?.Dispose(); } catch (Exception e) { Debug.LogError($"[CameraWS] Dispose error: {e}"); }
            ws = null;
            if (running) {
                Debug.LogWarning($"[CameraWS] Reconnecting in {reconnectDelay}s");
                yield return null;
            }
        }
    }
    */

    byte[] CaptureFrame() {
        RenderTexture.active = rt;
        cam.Render();
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        return tex.EncodeToJPG(jpegQuality);
    }

    private async void CleanupWebSocket() {
        if (ws != null && ws.State == WebSocketState.Open) {
            await ws.Close();
        }
        ws = null;
    }

    void OnDestroy() {
        running = false;
        //cts?.Cancel();
        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        CleanupWebSocket();
        /*
        try { ws?.Abort(); ws?.Dispose(); } catch { }
        ws = null;
        tex = null;
        rt = null;
        cam = null;
        */
    }

    void OnApplicationQuit() {
        running = false;
        //cts?.Cancel();
        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        CleanupWebSocket();
        /*
        try { ws?.Abort(); ws?.Dispose(); } catch { }
        ws = null;
        tex = null;
        rt = null;
        cam = null;
        */
    }

    void Update() {
        GL.Clear(true, true, Color.black, 0);
        ws?.DispatchMessageQueue();
    }

}

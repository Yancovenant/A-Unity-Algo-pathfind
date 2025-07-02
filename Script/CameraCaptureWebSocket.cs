/**
* CameraCaptureWebSocket.cs
* Robust, production-ready camera streaming for Unity agents with heartbeat/ping.
*/

using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;

public class CameraCaptureWebSocket : MonoBehaviour {
    [Header("WebSocket Settings")]
    public string agentId;
    public string host = "localhost";
    public int port = 8000;
    public int targetFps = 10; // Throttle FPS
    public int jpegQuality = 50; // JPEG quality (1-100)
    public int reconnectDelay = 2; // Seconds between reconnect attempts
    public int heartbeatInterval = 10; // Seconds between heartbeats

    private Camera cam;
    private RenderTexture rt;
    private Texture2D tex;
    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    private bool running = false;
    private Coroutine streamCoroutine;
    private string wsUrl => $"ws://{host}:{port}/ws/augv/{agentId}";

    void Start() {
        cam = GetComponentInChildren<Camera>();
        rt = new RenderTexture(160, 120, 24);
        tex = new Texture2D(160, 120, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        running = true;
        cts = new CancellationTokenSource();
        streamCoroutine = StartCoroutine(StreamLoop());
    }

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
                float now = Time.realtimeSinceStartup;
                float elapsed = now - lastFrameTime;
                bool sentFrame = false;
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

    byte[] CaptureFrame() {
        RenderTexture.active = rt;
        cam.Render();
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        return tex.EncodeToJPG(jpegQuality);
    }

    void OnDestroy() {
        running = false;
        cts?.Cancel();
        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        try { ws?.Abort(); ws?.Dispose(); } catch { }
        ws = null;
        tex = null;
        rt = null;
        cam = null;
    }

    void OnApplicationQuit() {
        running = false;
        cts?.Cancel();
        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        try { ws?.Abort(); ws?.Dispose(); } catch { }
        ws = null;
        tex = null;
        rt = null;
        cam = null;
    }
}

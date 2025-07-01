/**
* CameraCaptureWebSocket.cs
*/

using UnityEngine;
using System;
using NativeWebSocket;
using System.Collections;
using System.Text;

public class CameraCaptureWebSocket : MonoBehaviour {
    public string agentId;
    private WebSocket ws;
    private Camera cam;
    private RenderTexture rt;
    private Texture2D tex;

    void Start() {
        cam = GetComponentInChildren<Camera>();
        rt = new RenderTexture(160, 120, 24);
        cam.targetTexture = rt;
        tex = new Texture2D(160, 120, TextureFormat.RGB24, false);

        StartCoroutine(CaptureLoop());
        StartWebSocket();
    }

    async void StartWebSocket() {
        ws = new WebSocket($"ws://localhost:9999/ws/yolo/{agentId}");

        ws.OnOpen += () => Debug.Log($"[WebSocket] Connected: {agentId}");
        ws.OnError += e => Debug.LogError($"[WebSocket] Error ({agentId}): {e}");
        ws.OnClose += e => Debug.Log($"[WebSocket] Closed: {agentId}");

        await ws.Connect();
    }

    IEnumerator CaptureLoop() {
        while (true) {
            yield return new WaitForEndOfFrame();
            yield return new WaitForSeconds(0.1f); // ~10 FPS

            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            byte[] jpg = tex.EncodeToJPG(50); // compressed
            string base64 = Convert.ToBase64String(jpg);

            if (ws != null && ws.State == WebSocketState.Open) {
                string payload = "{\"image\":\"" + base64 + "\"}";
                ws.SendText(payload);
            }
        }
    }

    async void OnApplicationQuit() {
        if (ws != null) await ws.Close();
    }
}

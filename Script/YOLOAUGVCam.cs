// === CameraCaptureSocket.cs ===

using System.Collections;
using System.Net.Sockets;
using System.IO;
using UnityEngine;

public class CameraCaptureSocket : MonoBehaviour {
    public RenderTexture rt;
    public string agentId;
    private Camera cam;
    private TcpClient client;

    void Start() {
        cam = GetComponent<Camera>();
        agentId = transform.root.name;
        ConnectToPython();
        Debug.Log($"{agentId}, {cam}");
        //StartCoroutine(CaptureRoutine());
    }
    void ConnectToPython() {
        try {
            client = new TcpClient("localhost", 8051);
            Debug.Log($"[{agentId}] Connected to Python server.");
        } catch {
            Debug.LogError("Failed to connect to Python.");
        }
    }
    public void CaptureAndSend() {
        if (client == null || !client.Connected) return;

        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        
        byte[] imageBytes = tex.EncodeToJPG(50); // quality 50%
        BinaryWriter writer = new BinaryWriter(client.GetStream());
        writer.Write(agentId);
        writer.Write(imageBytes.Length);
        writer.Write(imageBytes);

        RenderTexture.active = null;
        Destroy(tex);
    }
    void OnApplicationQuit() {
        client?.Close();
    }
}

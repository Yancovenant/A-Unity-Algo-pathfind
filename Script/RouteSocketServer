/**
* RouteSocketServer.cs
* Responsible for Listening any network get activity
* Which will be used by our python client to send
* JSON route to each AUGV
*/

using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.Concurrent;

public class RouteSocketServer : MonoBehaviour {
    public RouteLoader routeLoader; // Assign in inspector
    public int port = 8051;

    private TcpListener listener;
    private Thread socketThread;

    private ConcurrentQueue<string> incomingRoutes = new ConcurrentQueue<string>();

    void Start() {
        socketThread = new Thread(ListenForClients);
        socketThread.IsBackground = true;
        socketThread.Start();
    }
    void Update() {
        // Handle incoming JSON strings from socket in Unity's main thread
        while (incomingRoutes.TryDequeue(out string json)) {
            Debug.Log("[RouteSocketServer] Received route JSON: " + json);
            if (routeLoader != null) {
                routeLoader.LoadFromJsonString(json);
            } else {
                Debug.LogWarning("RouteLoader not assigned.");
            }
        }
    }
    void ListenForClients() {
        try {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Debug.Log("[RouteSocketServer] Listening on port " + port);

            while (true) {
                using (TcpClient client = listener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream()) {
                    byte[] buffer = new byte[client.ReceiveBufferSize];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    incomingRoutes.Enqueue(json);
                }
            }
        } catch (SocketException e) {
            Debug.LogError("[RouteSocketServer] Socket exception: " + e.Message);
        }
    }

    void OnApplicationQuit() {
        listener?.Stop();
        socketThread?.Abort();
    }
}

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
using System.Collections.Concurrent;


public class RouteSocketServer : MonoBehaviour {
    public int port = 8051;

    private TcpListener listener;
    private Thread serverThread;

    private ConcurrentQueue<string> incomingRoutes = new ConcurrentQueue<string>();

    void Start() {
        listener = new TcpListener(IPAddress.Any, 8051);
        listener.Start();
        serverThread = new Thread(HandleConnection);
        serverThread.IsBackground = true;
        serverThread.Start();
        Debug.Log("[RouteSocketServer] Listening on port " + port);
    }
    void Update() {
        // Handle incoming JSON strings from socket in Unity's main thread
        while (incomingRoutes.TryDequeue(out string json)) {
            Debug.Log("[RouteSocketServer] Received route JSON: " + json);
            PathCoordinator.Instance.AssignRoutesFromJSON(json);
        }
    }
    void HandleConnection() {
        try {
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
        serverThread?.Abort();
    }
}

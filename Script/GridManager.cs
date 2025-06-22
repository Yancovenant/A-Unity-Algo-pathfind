/**
* Creates the map grid
*/

using UnityEngine;
using System.Collections.Generic;

public class GridManager : MonoBehaviour {
    public LayerMask unwalkableMask;
    public Vector2 gridWorldSize;
    public float nodeRadius;

    Node[,] grid;
    float nodeDiameter;
    int gridSizeX, gridSizeY;

    void Awake() {
        Invoke(nameof(CreateGrid), .1f);
        nodeDiameter = nodeRadius * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);
        CreateGrid();
    }
    void CreateGrid() {
        if (nodeRadius <= 0) {
            Debug.LogError("Node radius harus lebih dari 0");
            return;
        }
        grid = new Node[gridSizeX, gridSizeY];
        Vector3 worldTopLeft = transform.position;
        for (int x = 0; x < gridSizeX; x++) {
            for (int y = 0; y < gridSizeY; y++) {
                Vector3 worldPoint = worldTopLeft +
                    Vector3.right * (x * nodeDiameter + nodeRadius) +
                    Vector3.forward * (y * nodeDiameter + nodeRadius);
                bool walkable = !(Physics.CheckSphere(worldPoint, nodeRadius, unwalkableMask));
                grid[x, y] = new Node(walkable, worldPoint, x, y);
            }
        }
    }
    public Node NodeFromWorldPoint(Vector3 worldPosition) {
        float percentX = Mathf.Clamp01((worldPosition.x) / gridWorldSize.x);
        float percentY = Mathf.Clamp01((worldPosition.z / gridWorldSize.y)); // invert Z

        int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
        int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);

        return grid[x, y];
    }
    public List<Node> GetNeighbours(Node node) {
        List<Node> neighbours = new List<Node>();
        int[,] directions = new int[,] {{1,0}, {-1,0}, {0,1}, {0,-1}};
        for (int i = 0; i < directions.GetLength(0); i++) {
            int dx = directions[i,0];
            int dy = directions[i,1];
            int checkX = node.gridX + dx;
            int checkY = node.gridY + dy;
            if (checkX >= 0 && checkX < gridSizeX && checkY >= 0 && checkY < gridSizeY) neighbours.Add(grid[checkX, checkY]);
        }
        return neighbours;
    }
    public Node[,] GetGrid() => grid;
    public List<Node> path;

    Dictionary<Node, string> reservedNodes = new Dictionary<Node, string>();

    public void ReservePath(List<Node> path, string agentId) {
        foreach (Node node in path) {
            reservedNodes[node] = agentId;
        }
    }
    public void ReleasePath(List<Node> path, string agentId) {
        foreach (Node node in path) {
            if(reservedNodes.ContainsKey(node) && reservedNodes[node] == agentId) {
                reservedNodes.Remove(node);
            }
        }
    }
    public bool IsReservedByOther(Node node, string agentId) {
        return reservedNodes.ContainsKey(node) && reservedNodes[node] != agentId;
    }
/*
#if UNITY_EDITOR
    void OnDrawGizmos() {
        Vector3 gizmoCenter = transform.position + new Vector3(gridWorldSize.x, 0, gridWorldSize.y) * .5f;
        Gizmos.DrawWireCube(gizmoCenter, new Vector3(gridWorldSize.x, .1f, gridWorldSize.y));
        if (grid != null) {
            foreach (var n in grid) {
                Gizmos.color = n.walkable ? Color.white : Color.red;
                if(path != null && path.Contains(n)) Gizmos.color = Color.black;
                Gizmos.DrawCube(n.worldPosition, new Vector3(1, .1f, 1));
            }
        }
    }
#endif
*/
}

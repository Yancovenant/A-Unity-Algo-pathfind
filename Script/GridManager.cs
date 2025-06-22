/**
* GridManager.cs
* Responsible for creating the grid for the map.
*/

using UnityEngine;
using System.Collections.Generic;

public class GridManager : MonoBehaviour {
    public LayerMask unwalkableMask; // Layer mask for unwalkable objects, tweak on inspector
    public Vector2 gridWorldSize; // Size of the grid in world units, tweak on inspector // TODO: Change to dynamic
    public float nodeRadius; // Radius of the nodes, tweak on inspector // TODO: Change to dynamic

    Node[,] grid; // Grid array
    float nodeDiameter; // Diameter of the nodes
    int gridSizeX, gridSizeY; // Size of the grid in nodes

    void Awake() {
        // Invoke CreateGrid after .1f to avoid race overlapping between mapGenerator and this script;
        Invoke(nameof(CreateGrid), .1f);

        // Calculate nodeDiameter, gridSizeX, gridSizeY;
        nodeDiameter = nodeRadius * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);

        // Create the grid
        CreateGrid();
    }
    void CreateGrid() {
        // Check if nodeRadius is valid
        if (nodeRadius <= 0) {
            Debug.LogError("Node radius harus lebih dari 0");
            return;
        }
        grid = new Node[gridSizeX, gridSizeY];
        // as for the naming convention,
        // i dont exactly know what is the best naming convention for this,
        Vector3 startPos = transform.position;
        for (int x = 0; x < gridSizeX; x++) { // foreach column
            for (int y = 0; y < gridSizeY; y++) { // foreach row
                Vector3 worldPoint = startPos +
                    Vector3.right * (x * nodeDiameter + nodeRadius) + // x * nodeDiameter + nodeRadius
                    Vector3.forward * (y * nodeDiameter + nodeRadius); // y * nodeDiameter + nodeRadius
                bool walkable = !(Physics.CheckSphere(worldPoint, nodeRadius, unwalkableMask)); // Check if the node is walkable
                grid[x, y] = new Node(walkable, worldPoint, x, y); // Create the node
            }
        }
    }

    /**
    * Returns the node from a world position.
    */
    public Node NodeFromWorldPoint(Vector3 worldPosition) {
        float percentX = Mathf.Clamp01((worldPosition.x) / gridWorldSize.x);
        float percentY = Mathf.Clamp01((worldPosition.z / gridWorldSize.y)); // invert Z

        int x = Mathf.RoundToInt((gridSizeX - 1) * percentX);
        int y = Mathf.RoundToInt((gridSizeY - 1) * percentY);

        return grid[x, y];
    }

    /**
    * Returns the neighbours of a node.
    */
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

    /**---------- RESERVATION SYSTEM ----------**/
    /**
    * So this part i still dont know if we could use this later on the future,
    * this responsible for before @see:AStarPathfinding.cs, to reserve a path for agent,
    * but now we are using the PathCoordinator.cs to handle the reservation, recomputing etc.
    */
    public Node[,] GetGrid() => grid;
    public List<Node> path;

    Dictionary<Node, string> reservedNodes = new Dictionary<Node, string>(); // Dictionary for reserved nodes

    /**
    * Reserve a path for an agent.
    */
    public void ReservePath(List<Node> path, string agentId) {
        foreach (Node node in path) {
            reservedNodes[node] = agentId;
        }
    }
    /**
    * Release a path for an agent.
    */
    public void ReleasePath(List<Node> path, string agentId) {
        foreach (Node node in path) {
            if(reservedNodes.ContainsKey(node) && reservedNodes[node] == agentId) {
                reservedNodes.Remove(node);
            }
        }
    }
    /**
    * Returns true if the node is reserved by another agent.
    */
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

/**
* === GridManager.cs ===
* Responsible for creating the grid for the map.
*/

using UnityEngine;
using System.Collections.Generic;

public class GridManager : MonoBehaviour {
    public LayerMask unwalkableMask;
    public Vector2 gridWorldSize;
    public float nodeRadius;
    public Node[,] grid;

    private float nodeDiameter;
    private int gridSizeX, gridSizeY;
    public bool gridReady = false;

    public void CreateGrid() {
        nodeDiameter = nodeRadius * 2;
        gridSizeX = Mathf.RoundToInt(gridWorldSize.x / nodeDiameter);
        gridSizeY = Mathf.RoundToInt(gridWorldSize.y / nodeDiameter);
        grid = new Node[gridSizeX, gridSizeY];
        // as for the naming convention,
        // i dont exactly know what is the best naming convention for this,
        // so use at ur own risk.
        Vector3 startPos = transform.position;

        for (int x = 0; x < gridSizeX; x++) {
            for (int y = 0; y < gridSizeY; y++) {
                Vector3 worldPoint = startPos +
                    Vector3.right * (x * nodeDiameter + nodeRadius) +
                    Vector3.forward * (y * nodeDiameter + nodeRadius);
                bool walkable = !(Physics.CheckSphere(worldPoint, nodeRadius, unwalkableMask));
                grid[x, y] = new Node(walkable, worldPoint, x, y);
            }
        }
        gridReady = true;
        //Debug.Log($"grid ready {grid.GetHashCode()}");
    }

    /**
    * Returns the node from a world position.
    */
    public Node NodeFromWorldPoint(Vector3 worldPosition) {
        int x = Mathf.FloorToInt(worldPosition.x);
        int y = Mathf.FloorToInt(worldPosition.z);
        // Clamp to grid bounds
        x = Mathf.Clamp(x, 0, gridSizeX - 1);
        y = Mathf.Clamp(y, 0, gridSizeY - 1);
        return grid[x, y];
    }
}

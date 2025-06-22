/**
* Helper for auto generating map
*/

using UnityEngine;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour {
    /**
    * param: mapLayout {string} -> B = BUILDING, R = ROAD, M = MAT, . = EMPTY, W = WAREHOUSE.
    */
    public GameObject roadPrefab, buildingPrefab, warehousePrefab, spawnMarkerPrefab, garageDoorPrefab, loadingSpotPrefab;
    
    string[] mapLayout = new string[]
    {
        "..BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
        "...........................................B",
        "RRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR.B",
        "R...R........................R...........R.B",
        "R...R.BBBBBBBBBBBBBBBB.......W..BBBBBBBB.R.B",
        "R...R.BBBBBBBBBBBBBBBB.......R..BBBBBBBB.R.B",
        "R...R.BBBBBBBBBBBBBBBB.RRRRRRR..BBBBBBBB.R.B",
        "RRWRR.BBBBBBBBBBBBBBBB.R........BBBBBBBB.R.B",
        "R...R.BBBBBBBBBBBBBBBB.R.BBBBBBBBBBBBBBB.R.B",
        "R.B.R.BBBBBBBBBBBBBBBB.R.BBBBBBBBBBBBBBB.R.B",
        "R...R..................R.BBBBBBBBBBBBBBB.R.B",
        "RRRRRRRRRRRRRRRRR......R...........BBBBB.R.B",
        "R...............RRRRRRRRRRRRRRRRRR.BBBBB.R.B",
        "R.BBBBBBBBBBBBB.R..R......R......R.BBBBB.R.B",
        "R.BBBBBBBBBBBBB.R..W..BB..W..BB..W.BBBBB.R.B",
        "R.BBBBBBBBBBBBB.R..R......R......R....BB.R.B",
        "R.BBBBBBBBBBBBB.RRRRRRRRRRRRRRRRRRRRRR.B.R.B",
        "R.BBBBBBBBBBBBB.R............R.BBBBB.R.B.R.B",
        "R.BBBBBBBBBBBBB.R.BBBBBBBBBB.R.BBBBB.R.B.R.B",
        "R.BBBBBBBBBBBBB.R.BBBBBBBBBB.R.BBBBB.R.B.R.B",
        "R.BBBBBBBBBBBBB.R.BBBBBBBBBB.R.BBBBB.R.B.R.B",
        "R.........BBBBB.R.BBBBBBBBBB.R.B.....R.B.R.B",
        "RRRRRRRRR.BBBBB.R.BBBBBBBBBB.R.B.....R...R.B",
        "....R.B.R.BBBBB.R............R.B.W...RRWRR.B",
        ".WRRR.B.R.BBBBB.RRRRRRRRR....R.B.R...R.....B",
        "....R.B.R.BBBBB.R.......RRRWRR.B.RRRRR......",
        ".BB.R.B.R.......R.BBBBB.R....R...R..........",
        ".BB.R.B.RRRRRRRRR.BBBBB.R.BB.RRRRR.BBBBBBBBB",
        ".BB.R...R.......R.BBBBB.R.BB.R...R.BBBBBBBBB",
        ".BB.RRWRR.BBBBB.W.BBBBB.R.BB.RRWRR.BBBBBBBBB",
        ".BB.R...R.......R.......R....R...R.BBBBBBBBB",
        ".BB.RRRRRRRRRRRRRRRRRRRRRRRRRRRRRR.BBBBBBBBB",
        ".BB.......R...R...R...R...R........BBBBBBBBB",
        ".BBBBBB...R...R...R...R...R..BBBBBBBBBBBBBBB",
        ".BBBBBB..MR..MR..MR..MR..MR..BBBBBBBBBBBBBBB",
        ".BBBBBB..MS..MS..MS..MS..MS..BBBBBBBBBBBBBBB",
        ".........MM..MM..MM..MM..MM..BBBBBBBBBBBBBBB",
    };
    public Vector2Int mapSize = new Vector2Int(45,37); // TODO: Change to dynamic

    public List<Vector3> spawnPoints = new List<Vector3>(); // Public exposed list for spawnpoints
    public List<Transform> warehouseTargets = new List<Transform>(); // Public exposed list for warehouse targets

    void Start() {
        // On start generate map
        GenerateMap();
        mapSize = new Vector2Int(mapLayout[0].Length, mapLayout.Length);
        Debug.Log($"Map size: {mapSize}");
    }
    void GenerateMap() {
        // Foreach row
        for (int y = 0; y < mapLayout.Length; y++) {
            // foreach column
            var line = mapLayout[y];
            for (int x = 0; x < line.Length; x++) {
                Vector3 pos = new Vector3(x + 0.5f, 0f, mapLayout.Length - y - 1 + .5f); // Flip y to z
                char tile = line[x];
                switch (tile) {
                    case 'R': // Road
                        GameObject road = Instantiate(roadPrefab, pos + new Vector3(0, 0.05f, 0), Quaternion.identity, transform);
                        road.transform.localScale = new Vector3(1f, 0.1f, 1f);
                        break;
                    case 'B': // Building
                        var b = Instantiate(buildingPrefab, pos + new Vector3(0, 0.5f, 0), Quaternion.identity, transform);
                        b.layer = LayerMask.NameToLayer("Unwalkable");
                        break;
                    case 'W': // Warehouse
                        GameObject warehouse = Instantiate(warehousePrefab, pos, Quaternion.identity, transform);
                        warehouse.name = $"Warehouse_{warehouseTargets.Count + 1}";
                        if(HasAdjacentRoad(x,y)) OpenDoorOnWarehouse(warehouse, x, y);
                        Transform targetPoint = warehouse.transform.Find("TargetPoint");
                        if(targetPoint != null) warehouseTargets.Add(targetPoint);
                        break;
                    case 'S': // Spawnpoint
                        Instantiate(roadPrefab, pos + new Vector3(0, 0.05f, 0), Quaternion.identity, transform);
                        spawnPoints.Add(pos + Vector3.up * 0.5f); // Raise agent a bit
                        break;
                    case '.': // empty blocker
                        GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        blocker.transform.position = pos + new Vector3(0, 0.5f, 0);
                        blocker.transform.localScale = new Vector3(.9f,1f,.9f);
                        blocker.layer = LayerMask.NameToLayer("Obstacle");
                        DestroyImmediate(blocker.GetComponent<Renderer>()); // make it invisible
                        blocker.transform.parent = transform;
                        break;
                    case 'M': // Mat
                        var m = Instantiate(loadingSpotPrefab, pos + new Vector3(0, 0.025f, 0), Quaternion.identity, transform);
                        m.layer = LayerMask.NameToLayer("Unwalkable");
                        break;
                }
            }
        }
    }
    /**
    * Private method for Warehouse,
    * Returns if warehouse on each wall, NSWE has an adjacentRoad.
    */
    bool HasAdjacentRoad(int x, int y) {
        int[,] dirs = { {0,1}, {1,0}, {0,-1}, {-1,0} };
        for (int i = 0; i < dirs.GetLength(0); i++) {
            int nx = x + dirs[i, 0];
            int ny = y + dirs[i, 1];
            if(ny < 0 || ny >= mapLayout.Length) continue;
            string line = mapLayout[ny];
            if(nx < 0 || nx >= line.Length) continue;
            char tile = line[nx];
            if(tile=='R' || tile=='S') return true;
        }
        return false;
    }
    /**
    * Private method for Warehouse
    * Responsible for door spawning on the adjacent road.
    */
    void OpenDoorOnWarehouse(GameObject warehouse, int x, int y) {
        Dictionary<Vector2Int, (string wallName, float angle)> directions = new Dictionary<Vector2Int, (string,float)> {
            { new Vector2Int(0, 1), ("Wall_N", 0f) },
            { new Vector2Int(0, -1), ("Wall_S", 180f) },
            { new Vector2Int(1, 0), ("Wall_E", 90f) },
            { new Vector2Int(-1, 0), ("Wall_W", -90f) }
        };
        foreach (var dir in directions) {
            int nx = x + dir.Key.x;
            int ny = y + dir.Key.y;

            if (ny < 0 || ny >= mapLayout.Length) continue;
            string line = mapLayout[ny];
            if (nx < 0 || nx >= line.Length) continue;

            char tile = line[nx];
            if (tile == 'R' || tile == 'S') {
                Transform wall = warehouse.transform.Find(dir.Value.wallName);
                if (wall != null) Destroy(wall.gameObject);
                Vector3 offset = new Vector3(dir.Key.x * 1.5f, 0.6f, dir.Key.y * 1.5f);
                Quaternion rotation = Quaternion.Euler(0, dir.Value.angle, 0);
                Instantiate(garageDoorPrefab, warehouse.transform.position + offset, rotation, warehouse.transform);
            }
        }
    }
}

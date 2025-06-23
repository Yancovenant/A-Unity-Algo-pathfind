/**
* PathCoordinator.cs
* Central Coordination System for all AUGV Agents.
* Handles: path planning, occupancy checking, dynamic re-routing, and collision prediction.
* Global manager for all of the agents
*/

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

// =======================================================================================================
// Path Coordinator: The Central Brain of the Multi-Agent System
// =======================================================================================================

public class PathCoordinator : MonoBehaviour {
    public static PathCoordinator Instance { get; private set; }
    private GridManager grid;

    private Dictionary<string, List<Node>> activePaths = new Dictionary<string, List<Node>>();
    public AUGVAgent[] agents;
    
    IEnumerator Start() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            yield break;
        }
        Instance = this;

        // Find dependencies
        var mapGen = FindAnyObjectByType<MapGenerator>();
        grid = FindAnyObjectByType<GridManager>();
        var spawner = FindAnyObjectByType<AUGVSpawner>();
        if (mapGen == null || grid == null || spawner == null) {
            Debug.LogError("[PathCoordinator] Missing MapGenerator, GridManager, or AUGVSpawner.");
            yield break;
        }

        // 1. Generate map
        mapGen.GenerateMap();
        yield return null; // Wait a frame for map to be generated

        // 2. Create grid and spawn agents (can run in parallel)
        grid.CreateGrid();
        spawner.SpawnAgents();
        yield return null; // Wait a frame for grid and agents

        // 3. Assign node costs
        // Find all warehouse parent objects by name
        GameObject[] allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        HashSet<Vector2Int> warehousePositions = new HashSet<Vector2Int>();
        foreach (var obj in allObjects) {
            if (obj.name.StartsWith("Warehouse_")) {
                Vector3 pos = obj.transform.position;
                Vector2Int nPos = new Vector2Int(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.z));
                warehousePositions.Add(nPos);
            }
        }
        for (int x = 0; x < grid.gridWorldSize.x; x++) {
            for (int y = 0; y < grid.gridWorldSize.y; y++) {
                Node node = grid.NodeFromWorldPoint(new Vector3(x, 0, y));
                if (!node.walkable) continue;
                Vector2Int nodePos = new Vector2Int(node.gridX, node.gridY);
                node.cost = warehousePositions.Contains(nodePos) ? 2 : 1;
            }
        }
        #if UNITY_EDITOR
        UnityEditor.SceneView.RepaintAll();
        #endif
        // 4. Find agents for update loop
        agents = FindObjectsByType<AUGVAgent>(FindObjectsSortMode.None);
    }

    // Returns a path from startPos to endPos using A*
    public List<Node> RequestPath(string agentId, Vector3 startPos, Vector3 endPos) {
        if (grid == null) return null;
        activePaths[agentId] = AStarPathfinder.FindPath(grid, startPos, endPos);
        return activePaths[agentId];
    }

    void Update() {
        foreach (var agent in agents) {
            if (!activePaths.ContainsKey(agent.name) || activePaths[agent.name] == null || activePaths[agent.name].Count == 0) 
                continue;

            var agentPath = activePaths[agent.name];
            var agentPos = agent.transform.position;

            // Find the closest node to the agent that's in their path
            Node closestNode = null;
            float closestDist = float.MaxValue;
            int closestIndex = -1;

            for (int i = 0; i < agentPath.Count; i++) {
                var node = agentPath[i];
                float dist = Vector3.Distance(
                    new Vector3(node.worldPosition.x, agentPos.y, node.worldPosition.z),
                    agentPos
                );
                if (dist < closestDist) {
                    closestDist = dist;
                    closestNode = node;
                    closestIndex = i;
                }
            }

            // If agent is very close to a node (at its center), remove all previous nodes
            if (closestDist < 0.1f && closestIndex > 0) {
                activePaths[agent.name] = agentPath.GetRange(closestIndex, agentPath.Count - closestIndex);
            }
        }
    }

    /**
    * Returns dictionary of current agent paths (for debug/gizmo)
    */
    public Dictionary<string, List<Node>> GetActivePaths() {
        return activePaths;
    }
    /**
    * Returns true if a neighbour node is an intersection
    */
    public bool IsIntersection(Node node) {
        var neighbours = grid.GetNeighbours(node);
        return neighbours.Count(n => n.walkable) > 2;
    }
    /*** ------ debugging gizmos ------ ***/
    public Color[] agentColors;
    void OnDrawGizmos() {
        if (activePaths == null) return;
        // Draw all walkable node costs for debugging
        if (grid != null) {
            for (int x = 0; x < grid.gridWorldSize.x; x++) {
                for (int y = 0; y < grid.gridWorldSize.y; y++) {
                    Node node = grid.NodeFromWorldPoint(new Vector3(x, 0, y));
                    if (!node.walkable) continue;
                    //Debug.Log($"Gizmos, ({node.gridX}, {node.gridY}) on instance {node.GetHashCode()}");
                    Vector3 pos = node.worldPosition + new Vector3(0, 0.35f, 0);
#if UNITY_EDITOR
                    UnityEditor.Handles.Label(pos, $"{node.cost}");
                    //if (node.cost == 2) Debug.Log($"Gizmos sees cost=2 at ({node.gridX},{node.gridY}) instance {node.GetHashCode()}");
#endif
                }
            }
        }
        // For each agent, build a list of cumulative costs to each node in their path
        Dictionary<string, List<(Node node, int cumulativeCost)>> agentCostPaths = new Dictionary<string, List<(Node, int)>>();
        foreach (var kvp in activePaths) {
            var path = kvp.Value;
            if (path == null) continue;
            List<(Node, int)> costPath = new List<(Node, int)>();
            int costSum = 0;
            foreach (var node in path) {
                costSum += node.cost;
                costPath.Add((node, costSum));
            }
            agentCostPaths[kvp.Key] = costPath;
        }
        // Build a dictionary: (node, cumulativeCost) -> list of agent names
        Dictionary<(Node, int), List<string>> nodeCostToAgents = new Dictionary<(Node, int), List<string>>();
        foreach (var kvp in agentCostPaths) {
            string agentName = kvp.Key;
            var costPath = kvp.Value;
            foreach (var (node, cumCost) in costPath) {
                var key = (node, cumCost);
                if (!nodeCostToAgents.ContainsKey(key)) nodeCostToAgents[key] = new List<string>();
                nodeCostToAgents[key].Add(agentName);
            }
        }
        // Draw agent paths and contested nodes as before
        int agentIdx = 0;
        foreach (var kvp in agentCostPaths) {
            string agentName = kvp.Key;
            var costPath = kvp.Value;
            int colorIndex = 0;
            if (agentColors.Length > 0) {
                if(agentName.StartsWith("AUGV_")) {
                    if(int.TryParse(agentName.Substring(5), out int parsedIndex)) {
                        colorIndex = parsedIndex % agentColors.Length;
                    }
                } else {
                    int hash = 0;
                    foreach (char c in agentName) hash +=c;
                    colorIndex = hash % agentColors.Length;
                }
                Gizmos.color = agentColors[colorIndex];
            } else {
                Gizmos.color = Color.white;
            }
            for (int i = 0; i < costPath.Count; i++) {
                var (node, cumCost) = costPath[i];
                var key = (node, cumCost);
                bool isContested = nodeCostToAgents[key].Count > 1;
                Gizmos.color = isContested ? Color.red : (agentColors.Length > 0 ? agentColors[colorIndex] : Color.white);
                // Offset each agent's label horizontally by 0.25 units per agent
                float xOffset = agentIdx * 0.25f;
                Vector3 pos = node.worldPosition + new Vector3(xOffset, .15f, 0);
                Gizmos.DrawCube(pos, new Vector3(1, .1f, 1));
#if UNITY_EDITOR
                UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, agentName);
                UnityEditor.Handles.Label(pos + Vector3.up * 0.2f, $"{cumCost}");
#endif
            }
            agentIdx++;
        }
    }

    /**
    * So there is a problem where agent is stuck, roadblock, and no one is moving
    * because they share the same path, see@predictConflict
    * this calculate in the future index, where there is a node,
    * that is being used by the other agent.
    * more complex scenario could happen when the road width, length
    * is similar to each like a mirror
    * thus making the agent is not progressing at all.
    * to solve this:
    * 1. Global Reservation Table, for each node maintain reservation table
    * that which agent will occupy it at which timestep.
    * and when requesting a path, they would avoid this node;
    * 2. Priority system deadlock, maintain agent in global, and when the deadlock is detected.
    * we could allow the highest-priority agent to move.
    * 3. wait actions, they would wait in the path for a timestep if the next node is reserved.
    *
    * Reference research Cooperative A\* (CA)
    * - https://movingai.com/benchmarks/
    * - https://en.wikipedia.org/wiki/Cooperative_A*
    *
    * Reference research WHCA (windowed hierarchical cooperative A)
    * - https://www.aaai.org/Papers/AAAI/2004/AAAI04-072.pdf
    */

    /**
    * Too avaid all agents yielding, we needed to introduce a way,
    * so that agent that has 'higher priority',
    * will not yield, and only the rest is yielding and recalculating,
    * we do this by path length or gCost + hCost,
    * only the lower priority agents should yield.
    */

    
}

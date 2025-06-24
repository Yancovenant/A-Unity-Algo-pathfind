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
using System.IO;

// =======================================================================================================
// Path Coordinator: The Central Brain of the Multi-Agent System
// =======================================================================================================

public class PathCoordinator : MonoBehaviour {
    public static PathCoordinator Instance { get; private set; }
    private GridManager grid;

    private Dictionary<string, List<Node>> activePaths = new Dictionary<string, List<Node>>();
    public AUGVAgent[] agents;

    // New: Store waypoints for each agent
    private Dictionary<string, Queue<Vector3>> agentWaypoints = new Dictionary<string, Queue<Vector3>>();

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
        ResolveContestedNodes();
        return activePaths[agentId];
    }

    // New: Assign routes from JSON (called by RouteSocketServer)
    public void AssignRoutesFromJSON(string json)
    {
        // Example JSON: { "AUGV_1": ["Warehouse_1", "Warehouse_2"], ... }
        var parsed = MiniJSON.Json.Deserialize(json) as Dictionary<string, object>;
        if (parsed == null) {
            Debug.LogError("[PathCoordinator] Failed to parse route JSON");
            return;
        }
        foreach (var kvp in parsed)
        {
            string agentName = kvp.Key;
            var waypointNames = kvp.Value as List<object>;
            if (waypointNames == null) continue;
            var waypoints = new Queue<Vector3>();
            foreach (var nameObj in waypointNames)
            {
                string targetName = nameObj.ToString();
                GameObject targetObj = GameObject.Find(targetName);
                if (targetObj == null)
                {
                    Debug.LogWarning($"[PathCoordinator] Target not found: {targetName}");
                    continue;
                }
                waypoints.Enqueue(targetObj.transform.position);
            }
            agentWaypoints[agentName] = waypoints;
        }
        Debug.Log("[PathCoordinator] Assigned routes from JSON");
    }

    void Update() {
        // 1. Trim agent paths as before
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

        // 2. Assign new paths to idle agents
        List<string> idleAgents = new List<string>();
        foreach (var agent in agents) {
            // Agent is idle if it has no path or finished its path
            if (!activePaths.ContainsKey(agent.name) || activePaths[agent.name] == null || activePaths[agent.name].Count == 0) {
                //Debug.Log($"agent {agent.name} is idle checking waypoint left");
                // Only assign if there are waypoints left
                if (agentWaypoints.ContainsKey(agent.name) && agentWaypoints[agent.name].Count > 0) {
                    idleAgents.Add(agent.name);
                }
            } else {
                //Debug.Log($"agent {agent.name} is having activePaths {activePaths[agent.name]}, of {activePaths[agent.name].Count} left");
            }
        }
        // If there are idle agents with waypoints, plan all their paths together
        if (idleAgents.Count > 0) {
            // Compute all new paths
            Dictionary<string, List<Node>> newPaths = new Dictionary<string, List<Node>>();
            foreach (var agentName in idleAgents) {
                var agentObj = agents.FirstOrDefault(a => a.name == agentName);
                if (agentObj == null) continue;
                Vector3 startPos = agentObj.transform.position;
                Vector3 endPos = agentWaypoints[agentName].Peek(); // Don't dequeue yet
                var path = AStarPathfinder.FindPath(grid, startPos, endPos);
                if (path != null && path.Count > 0) {
                    newPaths[agentName] = path;
                }
            }
            // Temporarily assign to activePaths for conflict resolution
            foreach (var kvp in newPaths) {
                activePaths[kvp.Key] = kvp.Value;
            }
            // Resolve conflicts
            ResolveContestedNodes();
            // After conflict resolution, assign the resolved path to the agent and dequeue the waypoint
            foreach (var agentName in idleAgents) {
                if (activePaths.ContainsKey(agentName) && activePaths[agentName] != null && activePaths[agentName].Count > 0) {
                    // Dequeue the waypoint (now being processed)
                    if (agentWaypoints.ContainsKey(agentName) && agentWaypoints[agentName].Count > 0) {
                        agentWaypoints[agentName].Dequeue();
                    }
                    // Send the path to the agent (call a method on the agent to start moving)
                    var agentObj = agents.FirstOrDefault(a => a.name == agentName);
                    if (agentObj != null) {
                        agentObj.SendMessage("FollowPath", activePaths[agentName], SendMessageOptions.DontRequireReceiver);
                    }
                }
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
        if (activePaths == null || grid == null || grid.grid == null) return;
        // --- 1. Build contested node dictionary as before ---
        Dictionary<string, List<(Node node, int cumulativeCost)>> agentCostPaths = new Dictionary<string, List<(Node, int)>>();
        foreach (var kvp in activePaths) {
            var path = kvp.Value;
            if (path == null) continue;
            List<(Node, int)> costPath = new List<(Node, int)>();
            int costSum = 0;
            foreach (var node in path) {
                costSum += 1; // All nodes cost 1 for this logic
                costPath.Add((node, costSum));
            }
            agentCostPaths[kvp.Key] = costPath;
        }

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
        /** ====================================== **/
        // --- 2. Compute occupied warehouse areas ---
        HashSet<Node> occupiedNodes = new HashSet<Node>();
        foreach (var agent in agents) {
            if (!activePaths.ContainsKey(agent.name) || activePaths[agent.name] == null || activePaths[agent.name].Count == 0)
                continue;
            var path = activePaths[agent.name];
            Node endNode = path[path.Count - 1];
            // Check if endNode is a warehouse (by name in scene)
            GameObject[] allObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            bool isWarehouse = false;
            foreach (var obj in allObjs) {
                if (obj.name.StartsWith("Warehouse_")) {
                    Vector3 pos = obj.transform.position;
                    if (Mathf.RoundToInt(pos.x) == endNode.gridX && Mathf.RoundToInt(pos.z) == endNode.gridY) {
                        isWarehouse = true;
                        break;
                    }
                }
            }
            if (!isWarehouse) continue;
            // Check if agent is inside 3x3 area centered on endNode
            int cx = endNode.gridX, cy = endNode.gridY;
            int ax = Mathf.RoundToInt(agent.transform.position.x);
            int ay = Mathf.RoundToInt(agent.transform.position.z);
            if (Mathf.Abs(ax - cx) <= 1 && Mathf.Abs(ay - cy) <= 1) {
                // Mark all walkable nodes in 3x3 area as occupied
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dy = -1; dy <= 1; dy++) {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= grid.grid.GetLength(0) || ny >= grid.grid.GetLength(1)) continue;
                        Node n = grid.grid[nx, ny];
                        if (n.walkable) occupiedNodes.Add(n);
                    }
                }
            }
        }
        /** ======================================= **/
        // --- 3. Draw contested and occupied nodes ---
        // Draw contested nodes (red)
        HashSet<Node> drawnContested = new HashSet<Node>();
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
            foreach (var (node, cumCost) in costPath) {
                var key = (node, cumCost);
                bool isContested = nodeCostToAgents[key].Count > 1;
                Gizmos.color = isContested ? Color.red : (agentColors.Length > 0 ? agentColors[colorIndex] : Color.white);
                float xOffset = agentIdx * 0.20f;
                //Vector3 pos = node.worldPosition + new Vector3(xOffset, .15f, 0);
                Vector3 pos = node.worldPosition + new Vector3(0, .15f, 0);
                Gizmos.DrawCube(pos, new Vector3(1f, .1f, 1));
#if UNITY_EDITOR
                //UnityEditor.Handles.Label(pos + Vector3.up * 0.5f, agentName);
                //UnityEditor.Handles.Label(pos + Vector3.up * 0.3f, $"{cumCost}");
#endif
            }
            agentIdx++;
            /*
            for (int i = 0; i < costPath.Count; i++) {
                var (node, cumCost) = costPath[i];
                var key = (node, cumCost);
                bool isContested = nodeCostToAgents[key].Count > 1;
                Gizmos.color = isContested ? Color.red : (agentColors.Length > 0 ? agentColors[colorIndex] : Color.white);
                Vector3 pos = node.worldPosition + new Vector3(0, .15f, 0);
                Gizmos.DrawCube(pos, new Vector3(1, .1f, 1));
            }
            */
        }
        // Draw occupied warehouse nodes (green, only if not already drawn as contested)
        foreach (var node in occupiedNodes) {
            if (drawnContested.Contains(node)) continue;
            Gizmos.color = Color.green;
            Vector3 pos = node.worldPosition + new Vector3(0, .18f, 0);
            Gizmos.DrawCube(pos, new Vector3(1, .1f, 1));
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

    // Helper: Find all intersections on a path before a given node
    private List<Node> FindIntersectionsBeforeNode(List<Node> path, Node targetNode) {
        var intersections = new List<Node>();
        for (int i = 0; i < path.Count; i++) {
            if (path[i] == targetNode) break;
            if (IsIntersection(path[i])) intersections.Add(path[i]);
        }
        return intersections;
    }

    // Helper: Reroute from a given node to the goal, treating certain nodes as unwalkable
    private List<Node> RerouteFromNode(Node from, Node goal, HashSet<Node> blockedNodes) {
        // Temporarily mark blocked nodes as unwalkable
        var originalWalkable = new Dictionary<Node, bool>();
        foreach (var n in blockedNodes) {
            originalWalkable[n] = n.walkable;
            n.walkable = false;
        }
        var newPath = AStarPathfinder.FindPath(grid, from.worldPosition, goal.worldPosition);
        // Restore walkable
        foreach (var n in blockedNodes) n.walkable = originalWalkable[n];
        return newPath;
    }

    // Helper: Export all scenarios to a JSON file for debugging
    private void ExportScenariosToJson(List<Dictionary<string, List<Node>>> scenarios, Node contestedNode)
    {
        var exportList = new List<object>();
        int scenarioIdx = 0;
        foreach (var scenario in scenarios)
        {
            var scenarioObj = new Dictionary<string, object>();
            scenarioObj["scenarioIndex"] = scenarioIdx;
            var agentsList = new List<object>();
            foreach (var kvp in scenario)
            {
                var agentObj = new Dictionary<string, object>();
                agentObj["agentName"] = kvp.Key;
                //agentObj["path"] = kvp.Value.Select(n => new { x = n.gridX, y = n.gridY }).ToList();
                agentObj["path"] = kvp.Value.Select(n => new Dictionary<string, int> { { "x", n.gridX }, { "y", n.gridY } }).ToList();
                agentsList.Add(agentObj);
            }
            scenarioObj["agents"] = agentsList;
            exportList.Add(scenarioObj);
            scenarioIdx++;
        }
        var exportObj = new Dictionary<string, object>
        {
            ["contestedNode"] = new Dictionary<string, int> { { "x", contestedNode.gridX }, {"y", contestedNode.gridY } },
            ["scenarios"] = exportList
        };
        string json = MiniJSON.Json.Serialize(exportObj);
        string filePath = System.IO.Path.Combine(Application.persistentDataPath, $"scenarios_contested_{contestedNode.gridX}_{contestedNode.gridY}.json");
        System.IO.File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
        Debug.Log($"[PathCoordinator] Exported scenarios to {filePath}");
    }

    // Main: Resolve contested nodes
    public void ResolveContestedNodes() {
        ResolveContestedNodesRecursive(0);
    }

    private bool HasContestedNodes(Dictionary<string, List<Node>> paths) {
        // Build cost paths
        Dictionary<string, List<(Node node, int cumulativeCost)>> agentCostPaths = new Dictionary<string, List<(Node, int)>>();
        foreach (var kvp in paths) {
            var path = kvp.Value;
            if (path == null) continue;
            List<(Node, int)> costPath = new List<(Node, int)>();
            int costSum = 0;
            foreach (var node in path) {
                costSum += 1;
                costPath.Add((node, costSum));
            }
            agentCostPaths[kvp.Key] = costPath;
        }

        // Check for contested nodes
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

        return nodeCostToAgents.Any(kvp => kvp.Value.Count > 1);
    }

    private void ResolveContestedNodesRecursive(int depth, int maxDepth = 10) {
        if (depth >= maxDepth) {
            Debug.LogWarning("[PathCoordinator] Max recursion depth reached in conflict resolution");
            return;
        }

        // 1. Build cost paths as in OnDrawGizmos
        Dictionary<string, List<(Node node, int cumulativeCost)>> agentCostPaths = new Dictionary<string, List<(Node, int)>>();
        foreach (var kvp in activePaths) {
            var path = kvp.Value;
            if (path == null) continue;
            List<(Node, int)> costPath = new List<(Node, int)>();
            int costSum = 0;
            foreach (var node in path) {
                costSum += 1;
                costPath.Add((node, costSum));
            }
            agentCostPaths[kvp.Key] = costPath;
        }

        // 2. Find all contested nodes
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

        // 3. For each contested node (one at a time)
        var contestedNodes = nodeCostToAgents.Where(kvp => kvp.Value.Count > 1).ToList();
        if (contestedNodes.Count == 0) return; // No more conflicts to resolve

        foreach (var contest in contestedNodes) {
            Node contestedNode = contest.Key.Item1;
            int contestCost = contest.Key.Item2;
            var agentsInvolved = contest.Value;

            Debug.Log($"[PathCoordinator] Resolving contested node at ({contestedNode.gridX}, {contestedNode.gridY}) at depth {depth} with agents: {string.Join(", ", agentsInvolved)}");

            // For each agent, get their path
            var agentPaths = new Dictionary<string, List<Node>>();
            foreach (var agentName in agentsInvolved) {
                var path = activePaths[agentName];
                agentPaths[agentName] = path;
            }

            // Build all scenarios
            var scenarios = new List<Dictionary<string, List<Node>>>();

            // (a) All avoid
            var allAvoid = new Dictionary<string, List<Node>>();
            foreach (var agentName in agentsInvolved) {
                var path = agentPaths[agentName];
                if (path == null || path.Count == 0) continue;
                Node rerouteFrom = path[0];
                Node goal = path.Last();
                var newPath = RerouteFromNode(rerouteFrom, goal, new HashSet<Node> { contestedNode });
                if (newPath == null || newPath.Count == 0) goto skipAllAvoid;
                allAvoid[agentName] = newPath;
            }
            scenarios.Add(allAvoid);
            skipAllAvoid:;

            // (b) Each agent allowed
            foreach (var allowedAgent in agentsInvolved) {
                var scenario = new Dictionary<string, List<Node>>();
                foreach (var agentName in agentsInvolved) {
                    var path = agentPaths[agentName];
                    if (path == null || path.Count == 0) goto skipScenario;
                    Node rerouteFrom = path[0];
                    Node goal = path.Last();
                    HashSet<Node> blocked = agentName == allowedAgent ? new HashSet<Node>() : new HashSet<Node> { contestedNode };
                    var newPath = RerouteFromNode(rerouteFrom, goal, blocked);
                    if (newPath == null || newPath.Count == 0) goto skipScenario;
                    scenario[agentName] = newPath;
                }
                scenarios.Add(scenario);
                skipScenario:;
            }

            // (c) Each agent waits 1 step
            foreach (var waitingAgent in agentsInvolved) {
                var scenario = new Dictionary<string, List<Node>>();
                foreach (var agentName in agentsInvolved) {
                    var path = agentPaths[agentName];
                    if (path == null || path.Count == 0) goto skipWaitScenario;
                    if (agentName == waitingAgent) {
                        // Duplicate the first node to simulate waiting
                        var waitPath = new List<Node>(path.Count + 1);
                        waitPath.Add(path[0]);
                        waitPath.AddRange(path);
                        scenario[agentName] = waitPath;
                    } else {
                        scenario[agentName] = new List<Node>(path);
                    }
                }
                scenarios.Add(scenario);
                skipWaitScenario:;
            }

            // Export scenarios for debugging
            ExportScenariosToJson(scenarios, contestedNode);

            // 4. Evaluate scenarios and recursively check for conflicts
            scenarios = scenarios.Where(s => s.Count == agentsInvolved.Count).ToList();
            if (scenarios.Count == 0) continue;

            int bestTotalCost = int.MaxValue;
            Dictionary<string, List<Node>> bestScenario = null;

            foreach (var scenario in scenarios) {
                // Create a temporary copy of activePaths with this scenario
                var tempPaths = new Dictionary<string, List<Node>>(activePaths);
                foreach (var kvp in scenario) {
                    tempPaths[kvp.Key] = kvp.Value;
                }

                // Check if this scenario introduces new conflicts
                bool hasNewConflicts = HasContestedNodes(tempPaths);
                int totalCost = scenario.Values.Sum(path => path.Count);

                // If this scenario has no conflicts or has a better cost than current best
                if (!hasNewConflicts || totalCost < bestTotalCost) {
                    bestTotalCost = totalCost;
                    bestScenario = scenario;

                    // If no conflicts, we can use this scenario immediately
                    if (!hasNewConflicts) {
                        Debug.Log($"[PathCoordinator] Found conflict-free scenario at depth {depth}");
                        break;
                    }
                }
            }

            // 5. Apply best scenario and recursively resolve any new conflicts
            if (bestScenario != null) {
                foreach (var kvp in bestScenario) {
                    activePaths[kvp.Key] = kvp.Value;
                }

                // Recursively resolve any new conflicts
                ResolveContestedNodesRecursive(depth + 1, maxDepth);
            }
        }

        // Log final state
        Debug.Log($"[PathCoordinator] Conflict resolution completed at depth {depth}");
        foreach (var kvp in activePaths) {
            Debug.Log($"[PathCoordinator] Final path for {kvp.Key}: {kvp.Value.Count} nodes");
        }
    }

    public void NotifyPathComplete(string agentName) {
        if (activePaths.ContainsKey(agentName)) {
            activePaths.Remove(agentName);
            Debug.Log($"[PathCoordinator] Agent {agentName} completed their path. Removed from activePaths.");
        }
    }
}

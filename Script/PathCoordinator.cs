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

    /**
    * wow i am getting brain freeze at this moment. 24/6
    * so i tried debuging all this below code.
    * and the current code seems should be working right.
    * and yes in the runtime, it get the best path without no conflict.
    * but then in another runtime, it would reach maxdepth calculation and return a path with conflict.
    * like hell.
    * i call this The "Lucky algorithm".
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

    // New Helper: Finds all contested nodes and returns them as a list.
    private List<KeyValuePair<(Node, int), List<string>>> GetContestedNodes(Dictionary<string, List<Node>> paths) {
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

        // Find all contested nodes
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
        
        // Return a list of all nodes where more than one agent is present at the same time-step
        return nodeCostToAgents.Where(kvp => kvp.Value.Count > 1).ToList();
    }

    /***
    /* CORE: IMPORTANT RESOLVEING THE PATH GIVEN BY A* IS HERE.
    /* WE ARE THE SUPERVISOR THAT RESPONSIBLE FOR EVERY AGENT IN THE ENVIRONMENT.
    /* TO SEND INSTRUCTION (PATH) TO EACH OF OUR AGENT. THAT IS THE BEST (USING A*).
    /* AND ISNT CONFLICTING WITH THEIR PEERS.
    /*
    /*******************************************************************************
    * TO SOLVE THIS, WE IMPLEMENT A COMBINATORIAL INCREASE LOGIC BASED SCENARIO BUILDING SYSTEM.
    * EACH SCENARIO WOULD BE; (Let N be the number of AUGV'S involved in a conflict);
    * CASE A => ALL AVOID; = 1 SCENARIO;
    * CASE B => 1 OF AGENT IS ALLOWED THE REST IS NOT; = N SCENARIO'S;
    * CASE C => NON-EMPTY SUBSETS OF AGENT IS 'WAIT' EXCEPT FULLSET.
    * ========> A,B,C => NON-EMPTY SUBSET = {A}, {B}, {C}, {A,B}, {A,C}, {B,C}, {A,B,C}. <= EXCEPT THE FULL SET. 
    * ========> 2^N - 2 SCENARIO's;
    * ========> -----> Let k be the steps which the conflict occurs.
    * ========> -----> FOR AUGV'S IN SUBSET {A} THE WAIT IS THE NUMBER OF POSSIBLE WAIT STEPS FROM 1 UP TO.
    * ========> -----> (K-1)
    * ========> -----> FOR MULTI-AGENT SUBSETS {A,B} THE WAIT TIME IS ALL PAIRS WHERE waitA ≠ waitB.
    * ========> -----> Let m be size of Subset {a} -> m: 1, {a,b} m: 2;
    * ========> -----> P(k,m) = k! / (k - m)!;
    * ========> -----> together become n-1 n
    * ========> ----->                  ∑ ( )⋅P(k,m)  SCENARIO'S;
    * ========> ----->                 m=1 m
    * ========> THE FORMULA TILL THIS POINT OF TIME IS :
    * ========> 1 + N + THE ABOVE CODE, ITS HARD TO REPEAT IT.
    * ========> EXAMPLES: 
    * ========> FOR N = 3, K = 3;
    * ========> SUBSETS: {A}, {B}, {C}, {A,B}, {A,C}, {B,C}
    * ========> for m=1: 3 Subsets, each has 3 wait times = 9
    * ========> for m=2: 3 subsets, each has P(3,2) = 6 assignments = 18
    * ========> total wait scenarios is 9 + 18 = 27
    * ========> plus 1 (all avoid) + 3 (each allowed) = 31 SCENARIO'S
    *************************************************************************************
    * WE COULD HOWEVER IN THE FUTURE, ADD MORE CASES, TO HELP OR COMPUTE THIS LOGIC BETTER,
    * BASED ON RESEARCH DATA, RUNTIME, ETC.
    /*/

    private void ResolveContestedNodesRecursive(int depth, int maxDepth = 10) {
        if (depth >= maxDepth) {
            Debug.LogWarning("[PathCoordinator] Max recursion depth reached in conflict resolution");
            return;
        }

        // 1. Find all contested nodes in the current set of active paths.
        // We sort them to ensure a deterministic order of resolution.
        var contestedNodes = GetContestedNodes(activePaths)
            .OrderBy(c => c.Key.Item2) // Order by cost (time)
            //.ThenBy(c => c.Key.Item1.gridX) // Then by X
            //.ThenBy(c => c.Key.Item1.gridY) // Then by Y
            .ToList();

        if (contestedNodes.Count == 0) {
            Debug.Log($"[PathCoordinator] No conflicts found at depth {depth}. Resolution complete.");
            return; // No more conflicts to resolve
        }

        Debug.Log($"[PathCoordinator] Found {contestedNodes.Count} conflicts at depth {depth}. Planning resolutions...");
        
        var nextActivePaths = new Dictionary<string, List<Node>>(activePaths);

        // 2. For each contested node, find the best local resolution and plan to apply it.
        // Do NOT apply it to activePaths or recurse yet.
        foreach (var contest in contestedNodes) {
            Node contestedNode = contest.Key.Item1;
            var agentsInvolved = contest.Value;

            // Important: Get the LATEST paths for the agents involved from our 'nextActivePaths' state,
            // as a previous resolution in this same loop might have already updated them.
            var agentPaths = new Dictionary<string, List<Node>>();
            foreach (var agentName in agentsInvolved) {
                agentPaths[agentName] = nextActivePaths[agentName];
            }

            // Build all scenarios for the current conflict
            var scenarios = new List<Dictionary<string, List<Node>>>();

            // (a) All avoid
            var allAvoid = new Dictionary<string, List<Node>>();
            foreach (var agentName in agentsInvolved) {
                var path = agentPaths[agentName];
                if (path == null || path.Count == 0) continue;
                var newPath = RerouteFromNode(path[0], path.Last(), new HashSet<Node> { contestedNode });
                if (newPath != null && newPath.Count > 0) allAvoid[agentName] = newPath;
            }
            if (allAvoid.Count == agentsInvolved.Count) scenarios.Add(allAvoid);

            // (b) Each agent allowed
            foreach (var allowedAgent in agentsInvolved) {
                var scenario = new Dictionary<string, List<Node>>();
                foreach (var agentName in agentsInvolved) {
                    var path = agentPaths[agentName];
                    if (path == null || path.Count == 0) continue;
                    HashSet<Node> blocked = agentName == allowedAgent ? new HashSet<Node>() : new HashSet<Node> { contestedNode };
                    var newPath = RerouteFromNode(path[0], path.Last(), blocked);
                    if (newPath != null && newPath.Count > 0) scenario[agentName] = newPath;
                }
                if (scenario.Count == agentsInvolved.Count) scenarios.Add(scenario);
            }

            // (c) Wait scenarios for all non-empty subsets except the full set
            int k = contest.Key.Item2; // The step at which the conflict occurs
            int agentCount = agentsInvolved.Count;
            // Generate all non-empty, non-full subsets
            var agentList = agentsInvolved.ToList();
            for (int subsetMask = 1; subsetMask < (1 << agentCount) - 1; subsetMask++) {
                // Build the subset
                var subset = new List<int>();
                for (int i = 0; i < agentCount; i++) {
                    if (((subsetMask >> i) & 1) != 0) subset.Add(i);
                }
                int m = subset.Count;
                if (m == 0 || m == agentCount) continue;
                // For this subset, generate all unique permutations of wait times (from 1 to k) for the agents in the subset
                var waitTimes = Enumerable.Range(1, k).ToArray();
                foreach (var waitAssignment in GetPermutations(waitTimes, m)) {
                    var scenario = new Dictionary<string, List<Node>>();
                    // First, copy all agent paths
                    for (int i = 0; i < agentCount; i++) {
                        string agentName = agentList[i];
                        var path = agentPaths[agentName];
                        if (path == null || path.Count == 0) continue;
                        scenario[agentName] = new List<Node>(path);
                    }
                    // Now, for each agent in the subset, insert the assigned number of waits
                    for (int j = 0; j < m; j++) {
                        int agentIdx = subset[j];
                        string agentName = agentList[agentIdx];
                        int waitSteps = waitAssignment[j];
                        var path = scenario[agentName];
                        if (path == null || path.Count == 0) continue;
                        var waitPath = new List<Node>(path);
                        for (int w = 0; w < waitSteps; w++) {
                            waitPath.Insert(0, path[0]);
                        }
                        scenario[agentName] = waitPath;
                    }
                    if (scenario.Count == agentCount) scenarios.Add(scenario);
                }
            }

            if (scenarios.Count == 0) continue; // Could not generate any valid scenarios for this conflict
            ExportScenariosToJson(scenarios, contestedNode);
            // 3. Evaluate scenarios to find the best one for this specific conflict
            int bestTotalCost = int.MaxValue;
            Dictionary<string, List<Node>> bestScenario = null;
            bool foundConflictFree = false;
            
            foreach (var scenario in scenarios) {
                bool hasConflict = HasContestedNodes(scenario);
                int totalCost = scenario.Values.Sum(path => path.Count);
                if (!hasConflict) {
                    // If we find a conflict-free scenario, always prefer it
                    if (!foundConflictFree || totalCost < bestTotalCost) {
                        bestTotalCost = totalCost;
                        bestScenario = scenario;
                        foundConflictFree = true;
                    }
                } else if (!foundConflictFree && totalCost < bestTotalCost) {
                    // Only consider conflicted scenarios if no conflict-free one has been found
                    bestTotalCost = totalCost;
                    bestScenario = scenario;
                }
            }
            
            // 4. Apply the best local scenario to our temporary 'next' state.
            if (bestScenario != null) {
                //Debug.Log($"Found best scenario for conflict at ({contestedNode.gridX}, {contestedNode.gridY}). Applying to planned state.");
                foreach (var kvp in bestScenario) {
                    nextActivePaths[kvp.Key] = kvp.Value;
                }
            }
        }

        // 5. AFTER planning all resolutions for this level, update the main activePaths.
        activePaths = nextActivePaths;
        
        // 6. NOW, make a single recursive call to handle any new conflicts created by our changes.
        ResolveContestedNodesRecursive(depth + 1, maxDepth);
    }

    public void NotifyPathComplete(string agentName) {
        if (activePaths.ContainsKey(agentName)) {
            activePaths.Remove(agentName);
            Debug.Log($"[PathCoordinator] Agent {agentName} completed their path. Removed from activePaths.");
        }
    }

    // Helper: Check if a scenario has any contested nodes
    private bool HasContestedNodes(Dictionary<string, List<Node>> scenario) {
        return GetContestedNodes(scenario).Count > 0;
    }

    // Helper: Generate all permutations of a given array, taken m at a time
    private static IEnumerable<int[]> GetPermutations(int[] arr, int m) {
        return Permute(arr, 0, m);
    }
    private static IEnumerable<int[]> Permute(int[] arr, int start, int m) {
        if (start == m) {
            yield return arr.Take(m).ToArray();
        } else {
            for (int i = start; i < arr.Length; i++) {
                Swap(ref arr[start], ref arr[i]);
                foreach (var perm in Permute(arr, start + 1, m)) yield return perm;
                Swap(ref arr[start], ref arr[i]);
            }
        }
    }
    private static void Swap(ref int a, ref int b) {
        int temp = a; a = b; b = temp;
    }
}

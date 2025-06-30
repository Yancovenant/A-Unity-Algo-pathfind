/**
* PathCoordinator.cs
* Central Coordination System for all AUGV Agents.
* Handles: path planning, occupancy checking, dynamic re-routing, and collision prediction.
* Global manager for all of the agents
*/

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

// =======================================================================================================
// Path Coordinator: The Central Brain of the Multi-Agent System
// =======================================================================================================

public class PathCoordinator : MonoBehaviour {
    public static PathCoordinator Instance { get; private set; }

    public GridManager grid;
    public AUGVAgent[] agents;
    public Color[] agentColors;
    
    public Dictionary<string, List<Node>> activePaths = new Dictionary<string, List<Node>>();
    private Dictionary<string, Queue<Vector3>> agentWaypoints = new Dictionary<string, Queue<Vector3>>();
    private Dictionary<string, int> agentStepProgress = new Dictionary<string, int>();
    private Dictionary<string, (List<Node> path, int waitUntilStep)> waitingAssignments = new Dictionary<string, (List<Node>, int)>();

    private HashSet<string> activeAgents = new HashSet<string>();
    private bool lockstepInProgress = false;
    private int globalStepIndex = 0;
    
    private List<GameObject> warehouse;
    public HashSet<Node> warehouseNodes = new HashSet<Node>();

    IEnumerator Start() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            yield break;
        }
        Instance = this;

        var mapGen = FindAnyObjectByType<MapGenerator>();
        grid = FindAnyObjectByType<GridManager>();
        var spawner = FindAnyObjectByType<AUGVSpawner>();

        if (!mapGen || !grid || !spawner) {
            Debug.LogError("[PathCoordinator] Missing MapGenerator, GridManager, or AUGVSpawner.");
            yield break;
        }

        mapGen.GenerateMap(); yield return null;

        grid.CreateGrid(); spawner.SpawnAgents(); yield return null;

        agents = FindObjectsByType<AUGVAgent>(FindObjectsSortMode.None);
        /*
        GameObject[] allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in allObjects) {
            if (obj.name.StartsWith("Warehouse_")) warehouse.Add(obj);
        }
        */
        warehouse = FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                        .Where(o => o.name.StartsWith("Warehouse_")).ToList();
    }

    /**
    * Responsible to assign routes this supervisor received from client.
    * Using MiniJSON, for handling Json data.
    */
    public void AssignRoutesFromJSON(string json) {
        if (!(MiniJSON.Json.Deserialize(json) is Dictionary<string, object> parsed)) {
            Debug.LogError("[PathCoordinator] Failed to parse route JSON");
            return;
        }
        foreach (var (agentName, value) in parsed) {
            if (string.IsNullOrEmpty(agentName) || !(value is List<object> wpList)) continue;
            agentWaypoints[agentName] = new Queue<Vector3>(
                wpList.Select(o => GameObject.Find(o.ToString()))
                      .Where(g => g != null)
                      .Select(g => g.transform.position)
            );
        }
        Debug.Log("[PathCoordinator] Assigned routes from JSON");
        AssignNewPathsToIdleAgents();
    }

    public void AssignNewPathsToIdleAgents() {
        foreach (var agent in agents.Where(a =>
            (!activePaths.ContainsKey(a.name) || activePaths[a.name]?.Count == 0) &&
            agentWaypoints.TryGetValue(a.name, out var wps) && wps.Count > 0)) {
            computeBestPath(agent.name);
            //Debug.Log($"computting for {agent.name}");
        }
    }
    
    public void AssignNextPathToAgent(string agentId) {
        if(!HasWaypoints(agentId)) {
            activePaths.Remove(agentId);
            return;
        }
        computeBestPath(agentId);
    }
    
    public void computeBestPath(string agentId) {
        var agentObj = agents.FirstOrDefault(a => a.name == agentId);
        if (agentObj == null) return;

        Vector3 start = agentObj.transform.position;
        Vector3 end = agentWaypoints[agentId].Peek();
        var path = AStarPathfinder.FindPath(grid, start, end);
        if (path?.Count > 0) {
            activePaths[agentId] = path;
            ResolveContestedNodes();
            AssignPath(agentId, path);
            if (agentWaypoints[agentId].Count > 0) agentWaypoints[agentId].Dequeue();
        }
    }

    public bool HasWaypoints(string agentId) =>
        agentWaypoints.TryGetValue(agentId, out var q) && q.Count > 0;

    public void AssignPath(string agentId, List<Node> path) =>
        agents.FirstOrDefault(a => a.name == agentId)?.AssignPath(path);

    public void ReportAgentActive(string agentId) =>
        activeAgents.Add(agentId);

    void Update() {
        var readyAgents = agents.Where(a => 
            a.State == AUGVAgent.AgentState.WaitingForStep).ToArray();
        
        if (!lockstepInProgress && readyAgents.All(a => a.IsReadyToStep()) && readyAgents.Length > 0) {
            lockstepInProgress = true;
            activeAgents.Clear();
            foreach (var a in readyAgents) ReportAgentActive(a.name);
        }
        if (lockstepInProgress && readyAgents.Length == activeAgents.Count && readyAgents.Length > 0) {
            globalStepIndex++;
            activeAgents.Clear();
            ResolveContestedNodes();
            foreach (var a in readyAgents) {
                //CameraCaptureSocket capture = agent.GetComponentInChildren<CameraCaptureSocket>();
                //capture?.CaptureAndSend();
                a.Advance();
            }
        }
        TrimPathsToAgentPosition();
    }

    private void TrimPathsToAgentPosition() {
        foreach (var agent in agents) {
            if (!activePaths.TryGetValue(agent.name, out var path) || path == null || path.Count == 0) continue;
            float cDist = float.MaxValue;
            int cIdx = -1;
            var aPos = agent.transform.position;
            for (int i = 0; i < path.Count; i++) {
                float _d = Vector3.Distance(
                    new Vector3(path[i].worldPosition.x, aPos.y, path[i].worldPosition.z),
                    aPos
                );
                if (_d < cDist) {
                    cDist = _d;
                    cIdx = i;
                }
            }
            if (cDist < .1f && cIdx > 0) activePaths[agent.name] = path.GetRange(cIdx, path.Count - cIdx);
            if (path.Count == 0) activePaths[agent.name].Clear();
        }
    }

    /**
    * Main: Resolved Contested Node.
    */
    public void ResolveContestedNodes() => ResolveContestedNodesRecursive(0);

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
        var contestedNodes = GetContestedNodes(activePaths)
            .Select(kvp => (node: kvp.Key.Item1, step: kvp.Key.Item2, tags: kvp.Value))
            .OrderBy(c => c.step).ToList();
        
        var nextActivePaths = new Dictionary<string, List<Node>>(activePaths);
        
        if (contestedNodes.Count == 0) return;

        Debug.Log($"[PathCoordinator] Found {contestedNodes.Count} conflicts at depth {depth}. Planning resolutions...");
        
        HashSet<Node> blockedNodes = warehouseNodes.Count > 0 ? new HashSet<Node>(warehouseNodes) : new HashSet<Node>();
        foreach (var contest in contestedNodes) {
            var node = contest.node;
            var step = contest.step;
            var tags = contest.tags;
            var ags = tags.Keys.ToList();
            var paths = ags.ToDictionary(a => a, a => nextActivePaths[a]); // this is important in making sure to use the latest activepath from recursive solve path;

            bool isWarehouseOnly = tags.All(kvp => kvp.Value.All(t => t == "Warehouse_Occupied"));
            var scenarios = new List<Dictionary<string, List<Node>>>();
            
            if (isWarehouseOnly) {
                Debug.Log($"[WarehouseOnly] Handling warehouse conflict at ({node.gridX},{node.gridY})");
                /*
                foreach (var agentName in ags) {
                    var path = paths[agentName];
                    var goal = path[^1];

                    // Find intersection before goal
                    Node intersection = FindIntersectionsBeforeNode(path, goal);
                    if (intersection == null) {
                        Debug.LogWarning($"[WarehouseOnly] No intersection found for {agentName} before warehouse. Skipping delay logic.");
                        continue;
                    }

                    // Count steps to intersection
                    int stopIndex = path.IndexOf(intersection);
                    if (stopIndex < 0) continue;

                    // Create a wait-at-intersection path
                    List<Node> waitPath = new List<Node>();
                    for (int i = 0; i <= stopIndex; i++) {
                        waitPath.Add(path[i]);
                    }

                    // Simulate wait at that intersection (e.g., wait 3 steps)
                    for (int w = 0; w < 3; w++) {
                        waitPath.Insert(stopIndex, intersection);
                    }
                    // Add to modified path
                    nextActivePaths[agentName] = waitPath;

                    Debug.Log($"[WarehouseOnly] Agent {agentName} will wait at intersection ({intersection.gridX},{intersection.gridY})");
                }
                */
                continue; // skip normal ABC logic
            } else {
                // (a) All avoid
                var allAvoid = ags.ToDictionary(a => a, a => RerouteFromNode(paths[a][0], paths[a].Last(), new HashSet<Node> { node }));
                if (allAvoid.Values.All(p => p != null && p.Count > 0)) scenarios.Add(allAvoid);

                // (b) Each agent allowed
                foreach (var allowed in ags) {
                    var s = ags.ToDictionary(a => a, a => RerouteFromNode(paths[a][0], paths[a].Last(),
                        a == allowed ? new HashSet<Node>() : new HashSet<Node> { node }));
                    if (s.Values.All(p => p != null && p.Count > 0)) scenarios.Add(s);
                }

                // (c) Wait Permutations scenarios for all non-empty subsets except the full set
                int k = contest.step, n = ags.Count;
                // Generate all non-empty, non-full subsets
                var agentList = ags.ToList();
                for (int mask = 1; mask < (1 << n) - 1; mask++) {
                    // Build the subset
                    var subset = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).ToList();
                    // For this subset, generate all unique permutations of wait times (from 1 to k) for the agents in the subset
                    foreach (var waitCombo in GetPermutations(Enumerable.Range(1, k).ToArray(), subset.Count)) {
                        var s = ags.ToDictionary(a => a, a => new List<Node>(paths[a]));
                        for (int j = 0; j < subset.Count; j++) {
                            var a = agentList[subset[j]];
                            for (int w = 0; w < waitCombo[j]; w++) s[a].Insert(0, s[a][0]);
                        }
                        scenarios.Add(s);
                    }
                }
            }

            ExportScenariosToJson(scenarios, node);
            // 3. Evaluate scenarios to find the best one for this specific conflict
            var best = scenarios
                .Select(s => (s, hasConflict: HasContestedNodes(s), cost: s.Values.Sum(p => p.Count)))
                .OrderBy(t => (t.hasConflict ? 1 : 0, t.cost))
                .FirstOrDefault();
            if (best.s != null) foreach (var kvp in best.s) nextActivePaths[kvp.Key] = kvp.Value;
        }

        // 5. AFTER planning all resolutions for this level, update the main activePaths.
        activePaths = nextActivePaths;
        
        // 6. NOW, make a single recursive call to handle any new conflicts created by our changes.
        ResolveContestedNodesRecursive(depth + 1, maxDepth);
    }

    private List<KeyValuePair<(Node, int), Dictionary<string, List<string>>>> GetContestedNodes(Dictionary<string, List<Node>> paths) {
        // Build cost paths
        var costPaths = paths.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.Select((n,i) => (n, i + 1)).ToList()
        );
        // Find all contested nodes
        var nodeToAgents = new Dictionary<(Node, int), Dictionary<string, List<string>>>();
        foreach (var kvp in costPaths) {
            foreach (var (node, step) in kvp.Value) {
                var key = (node, step);
                if (!nodeToAgents.ContainsKey(key)) nodeToAgents[key] = new Dictionary <string, List<string>>();
                if (!nodeToAgents[key].ContainsKey(kvp.Key)) nodeToAgents[key][kvp.Key] = new List<string>();
                nodeToAgents[key][kvp.Key].Add("Path");
            }
            /*
            kvp.Value?.ForEach(t => {
                if (!nodeToAgents.ContainsKey(t)) nodeToAgents[t] = new Dictionary<string, List<string>>();
                nodeToAgents[t].AddIfMissing(kvp.Key);
                nodeToAgents[t][kvp.Key]("Path");
            });
            */
        }
        // Add swap conflict detection
        var keys = costPaths.Keys.ToArray();
        for (int i = 0; i < keys.Length; i++) {
            for (int j = i + 1; j < keys.Length; j++) {
                var a = costPaths[keys[i]];
                var b = costPaths[keys[j]];
                for (int k = 1; k < Mathf.Min(a.Count, b.Count); k++) {
                    if (a[k].Item1 == b[k - 1].Item1 && b[k].Item1 == a[k - 1].Item1 && a[k].Item2 == b[k].Item2) {
                        var key = (a[k].Item1, a[k].Item2);
                        if (!nodeToAgents.ContainsKey(key)) nodeToAgents[key] = new Dictionary<string, List<string>>();
                        if (!nodeToAgents[key].ContainsKey(keys[i])) nodeToAgents[key][keys[i]] = new List<string>();
                        if (!nodeToAgents[key].ContainsKey(keys[j])) nodeToAgents[key][keys[j]] = new List<string>();
                        nodeToAgents[key][keys[i]].Add("Path");
                        nodeToAgents[key][keys[j]].Add("Path");
                    }
                }
            }
        }
        /*
        warehouseNodes = agents.SelectMany(agent => {
            var path = activePaths.GetValueOrDefault(agent.name);
            if (path == null || path.Count == 0) return Enumerable.Empty<Node>();
            var end = path[^1];
            var warehouse = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                .Any(o => o.name.StartsWith("Warehouse_") &&
                          Mathf.RoundToInt(o.transform.position.x) == end.gridX &&
                          Mathf.RoundToInt(o.transform.position.z) == end.gridY);
            if (!warehouse) return Enumerable.Empty<Node>();
            int cx = end.gridX, cy = end.gridY;
            int ax = Mathf.RoundToInt(agent.transform.position.x), ay = Mathf.RoundToInt(agent.transform.position.z);
            return Mathf.Abs(ax - cx) <= 1 && Mathf.Abs(ay - cy) <= 1
                ? from dx in Enumerable.Range(-1, 3)
                  from dy in Enumerable.Range(-1, 3)
                  let nx = cx + dx
                  let ny = cy + dy
                  where nx >= 0 && ny >= 0 && nx < grid.grid.GetLength(0) && ny < grid.grid.GetLength(1)
                  let n = grid.grid[nx, ny]
                  where n.walkable
                  select n
                : Enumerable.Empty<Node>();
        }).ToHashSet();
        foreach (var node in warehouseNodes) {
            var key = (node, 9999);
            if (!nodeToAgents.ContainsKey(key)) nodeToAgents[key] = new List<string>();
            nodeToAgents[key].AddIfMissing("WAREHOUSE_OCCUPIED");
        }
        */
        var warehousePos = warehouse.Select(o => new Vector2Int(
            Mathf.RoundToInt(o.transform.position.x),
            Mathf.RoundToInt(o.transform.position.z))
        ).ToHashSet();
        var gridW = grid.grid.GetLength(0);
        var gridH = grid.grid.GetLength(1);

        foreach (var a in agents) {
            if (!activePaths.TryGetValue(a.name, out var path) || path == null || path.Count == 0) continue;
            var end = path[^1];
            int cx = end.gridX, cy = end.gridY;
            if (!warehousePos.Contains(new Vector2Int(cx, cy))) continue;
            int ax = Mathf.RoundToInt(a.transform.position.x), ay = Mathf.RoundToInt(a.transform.position.z);
            if (Mathf.Abs(ax - cx) > 1 || Mathf.Abs(ay - cy) > 1) continue;
            /*
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= grid.grid.GetLength(0) || ny >= grid.grid.GetLength(1)) continue;
                    var n = grid.grid[nx, ny];
                    if (!n.walkable) continue;

                    var key = (n, 9999);
                    if (!nodeToAgents.ContainsKey(key)) nodeToAgents[key] = new Dictionary<string, List<string>>();
                    if (!nodeToAgents[key].ContainsKey(a.name)) nodeToAgents[key][a.name] = new List<string>();
                    nodeToAgents[key][a.name].Add("Warehouse_occupied");

                    // Also add any other agents who plan to pass through this node
                    foreach (var other in agents) {
                        if (other.name == a.name) continue;
                        var otherPath = activePaths.GetValueOrDefault(other.name);
                        if (otherPath != null && otherPath.Contains(n)) {
                            if (!nodeToAgents[key].ContainsKey(other.name)) nodeToAgents[key][other.name] = new List<string>();
                            nodeToAgents[key][other.name].Add("Warehouse_occupied");
                        }
                    }
                }
            }
            */
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) {
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= gridW || (uint)ny >= gridH) continue;
                var n = grid.grid[nx, ny];
                if (!n.walkable) continue;

                var key = (n, 9999);
                if (!nodeToAgents.TryGetValue(key, out var dict)) nodeToAgents[key] = dict = new();
                if (!dict.TryGetValue(a.name, out var tagList)) dict[a.name] = tagList = new();
                tagList.AddIfMissing("Warehouse_occupied");

                foreach (var b in agents) {
                    if (b.name == a.name) continue;
                    if (activePaths.TryGetValue(b.name, out var op) && op != null && op.Contains(n)) {
                        if (!dict.TryGetValue(b.name, out var tags)) dict[b.name] = tags = new();
                        tags.AddIfMissing("Warehouse_occupied");
                    }
                }
            }
        }
        /*
        foreach (var agent in agents) {
            var path = activePaths.GetValueOrDefault(agent.name);
            if (path == null || path.Count == 0) continue;
            
            var end = path[^1];
            var wh = warehouse
                .Any(o => Mathf.RoundToInt(o.transform.position.x) == end.gridX &&
                          Mathf.RoundToInt(o.transform.position.z) == end.gridY);
            if (!wh) continue;

            int cx = end.gridX, cy = end.gridY;
            int ax = Mathf.RoundToInt(agent.transform.position.x), ay = Mathf.RoundToInt(agent.transform.position.z);

            if (Mathf.Abs(ax - cx) > 1 || Mathf.Abs(ay - cy) > 1) continue;

            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= grid.grid.GetLength(0) || ny >= grid.grid.GetLength(1)) continue;
                    var n = grid.grid[nx, ny];
                    if (!n.walkable) continue;

                    var key = (n, 9999);
                    if (!nodeToAgents.ContainsKey(key)) nodeToAgents[key] = new Dictionary<string, List<string>>();
                    if (!nodeToAgents[key].ContainsKey(agent.name)) nodeToAgents[key][agent.name] = new List<string>();
                    nodeToAgents[key][agent.name].Add("Warehouse_occupied");

                    // Also add any other agents who plan to pass through this node
                    foreach (var other in agents) {
                        if (other.name == agent.name) continue;
                        var otherPath = activePaths.GetValueOrDefault(other.name);
                        if (otherPath != null && otherPath.Contains(n)) {
                            if (!nodeToAgents[key].ContainsKey(other.name)) nodeToAgents[key][other.name] = new List<string>();
                            nodeToAgents[key][other.name].Add("Warehouse_occupied");
                        }
                    }
                }
            }
        }
        */
        

        //foreach (var n in nodeToAgents) Debug.Log($"node {n.Key.Item1.gridX} {n.Key.Item1.gridY} has {n.Value.Count}");
        // Return a list of all nodes where more than one agent is present at the same time-step
        return nodeToAgents.Where(kvp => kvp.Value.Count > 1).ToList();
    }
    
    private void occupiedWarehouseNodes() {
        // 1. Build set of occupied warehouse area nodes (3x3 around goal)
        warehouseNodes.Clear(); // Clear global set
        foreach (var a in agents) {
            if (!activePaths.TryGetValue(a.name, out var path) || path == null || path.Count == 0) continue;
            var end = path[^1];
            var endVec = new Vector2Int(end.gridX, end.gridY);

            // Fast check: is this end a warehouse?
            bool isWarehouse = warehouse.Any(o =>
                Mathf.RoundToInt(o.transform.position.x) == endVec.x &&
                Mathf.RoundToInt(o.transform.position.z) == endVec.y
            );
            if (!isWarehouse) continue;

            // Agent must be within 3x3 of that warehouse
            int ax = Mathf.RoundToInt(a.transform.position.x);
            int ay = Mathf.RoundToInt(a.transform.position.z);
            if (Mathf.Abs(ax - endVec.x) > 1 || Mathf.Abs(ay - endVec.y) > 1) continue;

            // Add 3x3 area nodes
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) {
                int nx = endVec.x + dx, ny = endVec.y + dy;
                if ((uint)nx >= grid.grid.GetLength(0) || (uint)ny >= grid.grid.GetLength(1)) continue;
                var n = grid.grid[nx, ny];
                if (n.walkable) warehouseNodes.Add(n);
            }
        }
    }

    // Helper: Check if a scenario has any contested nodes
    private bool HasContestedNodes(Dictionary<string, List<Node>> scenario) =>
        GetContestedNodes(scenario).Count > 0;

    /**
    * Helper: Reroute from a given node, to the goal, plus treating certain nodes as unwalkable/
    * Return:
    * - a new path based on the input, Node From, Node Goal, Blocked Node.
    */
    private List<Node> RerouteFromNode(Node from, Node goal, HashSet<Node> blocked) {
        // Temporarily mark blocked nodes as unwalkable
        var backup = blocked.ToDictionary(n => n, n => n.walkable);
        foreach (var n in blocked) n.walkable = false;
        var path = AStarPathfinder.FindPath(grid, from.worldPosition, goal.worldPosition);
        // Restore walkable
        foreach (var n in blocked) n.walkable = backup[n];
        return path;
    }

    /**
    * Helper: Generate all permutations of a given array, taken m at a time.
    */
    private static IEnumerable<int[]> GetPermutations(int[] arr, int m) =>
        Permute(arr, 0, m);

    private static IEnumerable<int[]> Permute(int[] arr, int start, int m) {
        if (start == m) yield return arr.Take(m).ToArray();
        else {
            for (int i = start; i < arr.Length; i++) {
                (arr[start], arr[i]) = (arr[i], arr[start]);
                foreach (var p in Permute(arr, start + 1, m)) yield return p;
                (arr[start], arr[i]) = (arr[i], arr[start]);
            }
        }
    }

    /**
    * Just a private helper, used for debugging scenarios to file json. that i could check.
    */
    private void ExportScenariosToJson(List<Dictionary<string, List<Node>>> scenarios, Node contestedNode) {
        var exportObj = new Dictionary<string, object> {
            ["contestedNode"] = new Dictionary<string, int> { ["x"] = contestedNode.gridX, ["y"] = contestedNode.gridY },
            ["scenarios"] = scenarios.Select((s, i) => new Dictionary<string, object> {
                ["scenarioIndex"] = i,
                ["agents"] = s.Select(kvp => new Dictionary<string, object> {
                    ["agentName"] = kvp.Key,
                    ["path"] = kvp.Value.Select(n => new Dictionary<string, int> { ["x"] = n.gridX, ["y"] = n.gridY}).ToList()
                }).ToList()
            }).ToList()
        };
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(Application.persistentDataPath, $"scenarios_contested_{contestedNode.gridX}_{contestedNode.gridY}.json"),
            MiniJSON.Json.Serialize(exportObj),
            System.Text.Encoding.UTF8
        );
    }

    /**
    * Returns true if a neighbour node is an intersection
    */
    public bool IsIntersection(Node node) {
        var neighbours = grid.GetNeighbours(node);
        return neighbours.Count(n => n.walkable) > 2;
    }

    // Helper: Find all intersections on a path before a given nodeAdd commentMore actions
    private Node FindIntersectionsBeforeNode(List<Node> path, Node targetNode) {
        Node lastIntersection = null;
        foreach (var node in path) {
            if (node == targetNode) break;
            if (IsIntersection(node)) lastIntersection = node;
        }
        return lastIntersection;
    }





    // // For real-time events (e.g., YOLO), stop all agents, replan, and resume in lockstep
    // public void HandleRealtimeEventAndReplan() {
    //     //StopAllAgentsImmediately();
    //     // Replan for all agents (example: just re-assign current waypoints)
    //     AssignNewPathsToIdleAgents();
    // }

    // /**
    // * Returns dictionary of current agent paths (for debug/gizmo)
    // */
    // public Dictionary<string, List<Node>> GetActivePaths() {
    //     return activePaths;
    // }
    // /**
    // * Returns true if a neighbour node is an intersection
    // */
    // public bool IsIntersection(Node node) {
    //     var neighbours = grid.GetNeighbours(node);
    //     return neighbours.Count(n => n.walkable) > 2;
    // }

    /*** ------ debugging gizmos ------ ***/
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

        // Add swap conflict detection
        var keys = agentCostPaths.Keys.ToArray();
        for (int i = 0; i < keys.Length; i++) {
            for (int j = i + 1; j < keys.Length; j++) {
                var pathA = agentCostPaths[keys[i]];
                var pathB = agentCostPaths[keys[j]];

                for (int k = 1; k < pathA.Count && k < pathB.Count; k++) {
                    var (aPrev, aStepPrev) = pathA[k - 1];
                    var (aNow, aStepNow) = pathA[k];
                    var (bPrev, bStepPrev) = pathB[k - 1];
                    var (bNow, bStepNow) = pathB[k];

                    if (aNow == bPrev && bNow == aPrev && aStepNow == bStepNow) {
                        var swapKey = (aNow, aStepNow);
                        if (!nodeCostToAgents.ContainsKey(swapKey))
                            nodeCostToAgents[swapKey] = new List<string>();
                        if (!nodeCostToAgents[swapKey].Contains(keys[i]))
                            nodeCostToAgents[swapKey].Add(keys[i]);
                        if (!nodeCostToAgents[swapKey].Contains(keys[j]))
                            nodeCostToAgents[swapKey].Add(keys[j]);
                    }
                }
            }
        }
        // --- new: build set of swap conflict nodes --- //
        HashSet<Node> swapConflictNodes = new HashSet<Node>();
        foreach (var kvp in nodeCostToAgents) {
            if (kvp.Value.Count > 1) {
                swapConflictNodes.Add(kvp.Key.Item1); // .Item1 = Node
            }
        }
        /** ====================================== **/
        // --- 2. Compute occupied warehouse areas ---
        /*
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
        */
        var warehouseNodes = agents.SelectMany(agent => {
            var path = activePaths.GetValueOrDefault(agent.name);
            if (path == null || path.Count == 0) return Enumerable.Empty<Node>();
            var end = path[^1];
            var warehouse = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                .Any(o => o.name.StartsWith("Warehouse_") &&
                            Mathf.RoundToInt(o.transform.position.x) == end.gridX &&
                            Mathf.RoundToInt(o.transform.position.z) == end.gridY);
            if (!warehouse) return Enumerable.Empty<Node>();
            int cx = end.gridX, cy = end.gridY;
            int ax = Mathf.RoundToInt(agent.transform.position.x), ay = Mathf.RoundToInt(agent.transform.position.z);
            return Mathf.Abs(ax - cx) <= 1 && Mathf.Abs(ay - cy) <= 1
                ? from dx in Enumerable.Range(-1, 3)
                    from dy in Enumerable.Range(-1, 3)
                    let nx = cx + dx
                    let ny = cy + dy
                    where nx >= 0 && ny >= 0 && nx < grid.grid.GetLength(0) && ny < grid.grid.GetLength(1)
                    let n = grid.grid[nx, ny]
                    where n.walkable
                    select n
                : Enumerable.Empty<Node>();
        }).ToHashSet();
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
                bool isSwapConflict = swapConflictNodes.Contains(node);
                bool isContested = nodeCostToAgents[key].Count > 1;
                if (isSwapConflict) {
                    Gizmos.color = Color.red;
                } else if (isContested) {
                    Gizmos.color = Color.yellow;
                } else {
                    Gizmos.color = agentColors.Length > 0 ? agentColors[colorIndex] : Color.white;
                }
                //Gizmos.color = agentColors.Length > 0 ? agentColors[colorIndex] : Color.white;
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
        }
        // Draw occupied warehouse nodes (green, only if not already drawn as contested)
        foreach (var node in warehouseNodes) {
            if (drawnContested.Contains(node)) continue;
            Gizmos.color = Color.black;
            Vector3 pos = node.worldPosition + new Vector3(0, .18f, 0);
            Gizmos.DrawCube(pos, new Vector3(1, .1f, 1));
        }
    }
}

// Extension Helper
public static class DictionaryExtensions {
    public static List<T> AddIfMissing<T>(this List<T> list, params T[] items) {
        foreach (var item in items) if (!list.Contains(item)) list.Add(item);
        return list;
    }
    public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key) =>
        dict.TryGetValue(key, out var v) ? v : default;
}

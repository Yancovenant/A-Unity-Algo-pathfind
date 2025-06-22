/**
* PathCoordinator.cs
* Central Coordination System for all AUGV Agents.
* Handles: path planning, occupancy checking, dynamic re-routing, and collision prediction.
*/

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PathCoordinator : MonoBehaviour {
    public static PathCoordinator Instance {get; private set;}

    private GridManager grid;
    private Dictionary<string, AUGVAgent> agents = new Dictionary<string, AUGVAgent>();
    private Dictionary<Node, string> nodeOccupancy = new Dictionary<Node, string>();
    private Dictionary<string, List<Node>> activePaths = new Dictionary<string, List<Node>>();

    void Awake() {
        if(Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        grid = FindAnyObjectByType<GridManager>();
        if (grid == null) Debug.LogError("[PathCoordinator] No GridManager found.");
    }

    /**
    * Regis AUGVAgent with the coordinator.
    */
    public void RegisterAgent(AUGVAgent agent) {
        if (!agents.ContainsKey(agent.name)) {
            agents[agent.name] = agent;
            Debug.Log("[PathCoordinator] Registered " + agent.name);
        }
    }

    /**
    * Unregis AUGVAgent
    */
    public void UnregisterAgent(AUGVAgent agent) {
        if(agents.ContainsKey(agent.name)) agents.Remove(agent.name);
    }

    /**
    * Called by RouteLoader to request a path for the agent to a waypoint;
    */
    public List<Node> RequestPath(string agentName, Vector3 startPos, Vector3 endPos) {
        if(grid == null) return null;
        List<Node> path = AStarPathfinder.FindPath(grid, startPos, endPos);
        if(path == null) {
            Debug.LogWarning("[PathCoordinator] No path found for " + agentName);
            return null;
        }
        activePaths[agentName] = path;
        /*
        foreach(var kvp in activePaths) {
            string otherAgent = kvp.Key;
            if(otherAgent == agentName) continue;
            List<Node> otherPath = kvp.Value;
            for (int i = 0; i < path.Count; i++) {
                Node step = path[i];
                int conflictIndex = otherPath.IndexOf(step);
                if(conflictIndex != -1 && Mathf.Abs(conflictIndex - i) <= 1) {
                    Debug.LogWarning($"[Conflict] {agentName} and {otherAgent} would collide at node {step.gridX},{step.gridY}");
                    if (path.Count > otherPath.Count) return null;
                }
            }
        }
        */
        return path;
    }

    /**
    * Called by agents when they enter a node.
    */
    public void NotifyEnterNode(string agentName, Node node) {
        nodeOccupancy[node] = agentName;
        /*
        if(nodeOccupancy.TryGetValue(node, out string occupant)) {
            if(occupant == agentName) return;
            Debug.LogWarning($"[PathCoordinator] Node {node.gridX},{node.gridY} already occupied by {occupant}");
        } else {
            nodeOccupancy[node] = agentName;
        }
        
        if(!nodeOccupancy.ContainsKey(node)) {
            nodeOccupancy[node] = agentName;
        } else {
            Debug.LogWarning($"[PathCoordinator] Node {node.gridX},{node.gridY} already occupied by {nodeOccupancy[node]}");
        }
        */
    }

    /**
    * Called by agents when they leave a node.
    */
    public void NotifyLeaveNode(string agentName, Node node) {
        if (nodeOccupancy.ContainsKey(node) && nodeOccupancy[node] == agentName) {
            nodeOccupancy.Remove(node);
        }
    }

    /**
    * Returns true if a node is currently occupied by any agent.
    */
    public bool IsNodeOccupied(Node node) {
        return nodeOccupancy.ContainsKey(node);
    }

    /**
    * Returns dictionary of current agent paths (for debug/gizmo)
    */
    public Dictionary<string, List<Node>> GetActivePaths() {
        return activePaths;
    }

    /**
    * Too avaid all agents yielding, we needed to introduce a way,
    * so that agent that has 'higher priority',
    * will not yield, and only the rest is yielding and recalculating,
    * we do this by path length or gCost + hCost,
    * only the lower priority agents should yield.
    */
    public bool ShouldYieldToOtherAgent(string agentName, Node currentNode, List<Node> myPath) {
        int myIndex = myPath.IndexOf(currentNode);
        if (myIndex < 0) return false; // Well what am i here for? i dont use this node.
        
        /// UPDATE: "Innocent until proven guilty" or "confident unless proven otherwise" approach;
        //
        // I CHANGE THE LOGIC FROM, HARFIAHLY PESSIMISTIC,
        // WHERE THEY WOULD YIELD THE MOMENT THEY FAILED AT CONTEST,
        // INTO I'LL ONLY YIELD IF ALL CONTEST SAY I SHOULD YIELD,
        // IF THERE IS EVEN ONE CASE WHERE ANOTHER AGENT SHOULD YIELD TO ME
        // I DO NOT YIELD
        // ONLY IF ALL OTHERS BEAT ME -> I YIELD
        bool shouldYield = false;

        foreach (var kvp in activePaths) {
            string otherAgent = kvp.Key;
            if (otherAgent == agentName) continue;

            List<Node> otherPath = kvp.Value;
            int otherIndex = otherPath.IndexOf(currentNode);

            if(otherIndex >= 0) {
                bool IArriveLater = myIndex > otherIndex; // i yield;
                bool SameTime = myIndex == otherIndex; // new contest;
                bool IHaveLongerPath = myPath.Count > otherPath.Count; // i should yield;
                bool IHaveLexicallyLaterName = string.Compare(agentName, otherAgent) > 0;

                // Special check: if I'm already on the road and going straight, I won't yield
                //if (IsGoingStraight(myPath, myIndex)) return false;

                bool iShouldYield = IArriveLater || (SameTime && (IHaveLongerPath || IHaveLexicallyLaterName));
                if(iShouldYield) {
                    shouldYield = true;
                    continue;
                } else {
                    // i dont need to yield to this, -> skip yielding;
                    return false;
                }
            }   
                /*
                {
                    // I yield here
                    // But if the current node is blocked by other agent, tell THEM to move first;
                    if(nodeOccupancy.TryGetValue(currentNode, out string occupant) && occupant == otherAgent) {
                        // force lower priority agent to move forward (not yield);
                        if(agents.TryGetValue(otherAgent, out var blockingAgent)) {
                            blockingAgent.ForceStep(); // AUGVAgent.cs
                            Debug.Log($"[{agentName}] Conflict with {otherAgent}, forcing them to move at node {currentNode.gridX},{currentNode.gridY}");
                        }
                        return false; // i will not yield, they'll get out of my way
                    }
                    return true;
                }
                */
            // handle intersection + approaching road logic
            if (IsIntersection(currentNode)) {
                for (int i = myIndex; i < myPath.Count; i++) {
                    Node futureNode = myPath[i];
                    int otherFutureIndex = otherPath.IndexOf(futureNode);
                    if (otherFutureIndex >= 0) {
                        bool IArriveLater = i > otherFutureIndex;
                        bool sameTime = i == otherFutureIndex;
                        //Debug.Log($"[Yield Check] {agentName} vs {otherAgent} at future node {futureNode.gridX},{futureNode.gridY}, i={i}, otherFutureIndex={otherFutureIndex}");

                        // Again, check if already on road going straight — if so, don't yield
                        //if (IsGoingStraight(myPath, myIndex)) return false;

                        if (IArriveLater) {
                            shouldYield = true; // they get there first, i should yield
                            continue;
                        }
                        
                        if (sameTime) {
                            bool myPathIsLonger = myPath.Count > otherPath.Count;
                            bool nameIsLater =  string.Compare(agentName, otherAgent) > 0;
                            //Debug.Log($"[TieBreaker] {agentName} vs {otherAgent} at node {futureNode.gridX},{futureNode.gridY} | Longer: {myPathIsLonger}, Lex: {nameIsLater}");
                            if (myPathIsLonger || nameIsLater) {
                                shouldYield = true;
                                continue;
                            } else {
                                return false;
                            }
                        }
                    }
                }
            }
        }
        return shouldYield;
    }

    /**
    * We introduce new method to give solutions for,
    * priority inversion, where sometimes lower-priority agent is blocking,
    * higher-priority one.
    * this means the see:@ShouldYieldToOtherAgent() needs to work both directions;
    */
    public void TryResolveBlockage(string yieldingAgent, Node conflictNode) {
        foreach (var kvp in activePaths) {
            string otherAgentId = kvp.Key;
            if (otherAgentId == yieldingAgent) continue;
            List<Node> otherPath = kvp.Value;
            int index = otherPath.IndexOf(conflictNode);
            if (index == -1) continue;

            if (agents.TryGetValue(otherAgentId, out AUGVAgent blockingAgent)) {
                // Agent A is yielding, Agent B is on conflict node. Should Agent B be forced forward?
                List<Node> yieldingPath = activePaths[yieldingAgent];
                int myIndex = yieldingPath.IndexOf(conflictNode);
                int otherIndex = otherPath.IndexOf(conflictNode);

                bool agentYShouldGo = false;
                if (myIndex < otherIndex) agentYShouldGo = true;
                else if (myIndex == otherIndex) {
                    if (yieldingPath.Count < otherPath.Count) agentYShouldGo = true;
                    else if (yieldingPath.Count == otherPath.Count) {
                        if (string.Compare(yieldingAgent, otherAgentId) < 0) agentYShouldGo = true;
                    }
                }

                if (agentYShouldGo) {
                    Debug.Log($"[{yieldingAgent}] is blocked by [{otherAgentId}] but has higher priority. Forcing [{otherAgentId}] to move.");
                    blockingAgent.ForceStep();
                }
            }
        }
    }


    /**
    * Predicts if the target node will be contested by another agent soon;
    */
    /*
    public bool PredictConflict(string agentName, List<Node> myPath) {
        if (!agents.TryGetValue(agentName, out var thisAgent)) return false;
        int myStep = thisAgent.GetStepIndex();
        int maxSteps = myPath.Count;
        foreach(var kvp in agents) {
            string otherAgentId = kvp.Key;
            if(otherAgentId == agentName) continue;
            var otherAgent = kvp.Value;
            if (!activePaths.TryGetValue(otherAgentId, out var otherPath)) continue;
            int otherStep = otherAgent.GetStepIndex();
            for (int step = 0; step < maxSteps; step++) {
                int myFutureStep = myStep + step;
                int otherFutureStep = otherStep + step;
                if (myFutureStep < myPath.Count && otherFutureStep < otherPath.Count) {
                    Node myNode = myPath[myFutureStep];
                    Node otherNode = otherPath[otherFutureStep];
                    if( myNode == otherNode) return true;
                }
            }
        }
        return false;
    }
    */
    public bool PredictConflict(string agentName, List<Node> myPath) {
        if (!agents.TryGetValue(agentName, out var thisAgent)) return false;
        int myStep = thisAgent.GetStepIndex();

        foreach (var kvp in agents) {
            string otherAgentId = kvp.Key;
            if (otherAgentId == agentName) continue;
            //if (string.Compare(agentName, otherAgentId) > 0) continue;
            if (!activePaths.TryGetValue(otherAgentId, out var otherPath)) continue;
            if (!agents.TryGetValue(otherAgentId, out var otherAgent)) continue;

            int otherStep = otherAgent.GetStepIndex();

            // We'll compare each future step of this agent
            for (int i = myStep; i < myPath.Count && i - myStep < 5; i++) {
                Node myNode = myPath[i];
                int iOffset = i - myStep;

                // Check if the other agent will also be on this node in the next few steps
                for (int j = otherStep; j < otherPath.Count && j - otherStep < 5; j++) {
                    Node otherNode = otherPath[j];

                    // If both agents will step onto same node in future
                    if (myNode == otherNode && iOffset == j - otherStep) {
                        // Allow going-straight agents to ignore conflict
                        //if (IsGoingStraight(myPath, i)) continue;
                        //if (IsGoingStraight(myPath, myStep)) {
                        //    Debug.Log($"[StraightCheck - Conflict] {agentName} starting at {myPath[myStep].gridX},{myPath[myStep].gridY} is going straight, ignoring conflict with {otherAgentId}");
                        //    continue;
                        //}
                        // Conflict detected on same timestep
                        Debug.Log($"[PredictConflict] {agentName} and {otherAgentId} will both be at {myNode.gridX},{myNode.gridY} on step {iOffset}");
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /**
    * Returns true if an agent is in going straight
    */
    
    public bool IsGoingStraight(List<Node> path, int currentIndex) {
        if (path == null || path.Count < currentIndex + 3) return false; // Not enough info to predict

        Vector2Int curr = new Vector2Int(path[currentIndex].gridX, path[currentIndex].gridY);
        Vector2Int next = new Vector2Int(path[currentIndex + 1].gridX, path[currentIndex + 1].gridY);
        Vector2Int next2 = new Vector2Int(path[currentIndex + 2].gridX, path[currentIndex + 2].gridY);

        Vector2Int dir1 = next - curr;
        Vector2Int dir2 = next2 - next;

        Debug.Log($"[StraightCheck] Agent at {curr} | Dir1={dir1}, Dir2={dir2}");

        return dir1 == dir2; // Still facing same direction = going straight
        /*
        if (currentIndex <= 0 || currentIndex + 2 >= path.Count)
            return false; // not enough info

        Vector2Int prev = new Vector2Int(path[currentIndex - 1].gridX, path[currentIndex - 1].gridY);
        Vector2Int curr = new Vector2Int(path[currentIndex].gridX, path[currentIndex].gridY);
        Vector2Int next = new Vector2Int(path[currentIndex + 1].gridX, path[currentIndex + 1].gridY);
        Vector2Int afterNext = new Vector2Int(path[currentIndex + 2].gridX, path[currentIndex + 2].gridY);

        Vector2Int dir1 = next - curr;
        Vector2Int dir2 = afterNext - next;

        // For debugging
        Debug.Log($"[StraightCheck] Agent at ({curr.x}, {curr.y}) | Dir1={dir1}, Dir2={dir2}");

        return dir1 == dir2; // True straight direction (no turning)
        */
        /*
        if (currentIndex <= 0 || currentIndex + 1 >= path.Count)
            return false; // not enough info to determine direction
        */
        /*
        if (path.Count < 2) return false;
        Vector2Int dir1 = Vector2Int.zero;
        Vector2Int dir2 = Vector2Int.zero;
        if (currentIndex == 0 && path.Count >= 2) {
            // Not enough to look behind, just check first direction
            Vector2Int curr = new Vector2Int(path[0].gridX, path[0].gridY);
            Vector2Int next = new Vector2Int(path[1].gridX, path[1].gridY);
            dir1 = next - curr;
            dir2 = dir1; // Assume consistent for the first step
        } else if (currentIndex > 0 && currentIndex + 1 < path.Count) {
            Vector2Int prev = new Vector2Int(path[currentIndex - 1].gridX, path[currentIndex - 1].gridY);
            Vector2Int curr = new Vector2Int(path[currentIndex].gridX, path[currentIndex].gridY);
            Vector2Int next = new Vector2Int(path[currentIndex + 1].gridX, path[currentIndex + 1].gridY);
            dir1 = curr - prev;
            dir2 = next - curr;
        } else {
            return false;
        }
        */
        /*
        Vector2Int prev = new Vector2Int(path[currentIndex - 1].gridX, path[currentIndex - 1].gridY);
        Vector2Int curr = new Vector2Int(path[currentIndex + 0].gridX, path[currentIndex + 0].gridY);
        Vector2Int next = new Vector2Int(path[currentIndex + 1].gridX, path[currentIndex + 1].gridY);
        Vector2Int dir1 = curr - prev;
        Vector2Int dir2 = next - curr;
        */
        /*
        Debug.Log($"[StraightCheck] Agent at {path[currentIndex].gridX},{path[currentIndex].gridY} | Dir1={dir1}, Dir2={dir2}");
        return dir1 == dir2; // same direction = going straight
        */
    }

    /**
    * Returns true if a node neighboar is an intersection
    */
    public bool IsIntersection(Node node) {
        var neighbours = grid.GetNeighbours(node);
        int walkableCount = neighbours.Count(n => n.walkable);
        return walkableCount > 2;
    }


    /*** ------ debugging gizmos ------ ***/
    public Color[] agentColors;
    void OnDrawGizmos() {
        if (activePaths == null) return;
        Dictionary<Node, List<string>> nodeToAgents = new();

        // First pass: Collect who owns each node;
        foreach (var kvp in activePaths) {
            string agentName = kvp.Key;
            List<Node> path = kvp.Value;
            foreach (Node node in path) {
                if(!nodeToAgents.ContainsKey(node)) nodeToAgents[node] = new();
                nodeToAgents[node].Add(agentName);
            }
        }

        // Second pass: draw
        foreach(var kvp in activePaths) {
            string agentName = kvp.Key;
            List<Node> path = kvp.Value;

            int colorIndex = 0;
            if (agentColors.Length > 0) {
                // Option 1: If agent name ends in _1, _2, etc., extract index
                if (agentName.StartsWith("AUGV_")) {
                    if (int.TryParse(agentName.Substring(5), out int parsedIndex)) {
                        colorIndex = parsedIndex % agentColors.Length;
                    }
                } else {
                    // Option 2: Fallback simple sum-of-chars hash
                    int hash = 0;
                    foreach (char c in agentName) hash += c;
                    colorIndex = hash % agentColors.Length;
                }
                Gizmos.color = agentColors[colorIndex];
            } else {
                // Default fallback color
                Gizmos.color = Color.white;
            }
            foreach (Node node in path) {
                Vector3 pos = node.worldPosition + new Vector3(0,.15f,0);
                Gizmos.DrawCube(pos, new Vector3(1, .1f, 1));
            }
        }

        // Draw overlapping nodes;
        /*
        foreach (var kvp in nodeToAgents) {
            if (kvp.Value.Count > 1) {
                Gizmos.color = Color.magenta;
                Vector3 pos = kvp.Key.worldPosition + Vector3.up * .15f;
                Gizmos.DrawSphere(pos, 0.15f);
            }
        }
        */
    }
}

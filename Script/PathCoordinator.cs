/**
* PathCoordinator.cs
* Central Coordination System for all AUGV Agents.
* Handles: path planning, occupancy checking, dynamic re-routing, and collision prediction.
* Global manager for all of the agents
*/

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// =======================================================================================================
// Path Coordinator: The Central Brain of the Multi-Agent System
// =======================================================================================================

public class PathCoordinator : MonoBehaviour {
    public static PathCoordinator Instance {get; private set;}

    private GridManager grid;
    private Dictionary<string, AUGVAgent> agents = new Dictionary<string, AUGVAgent>();

    // this is the core solution for multi-agent, it maps timestep, to a dictionary of node reservations at that time.
    // Structure: Dict<node,string> -> timestep  -> (node -> agentId);
    private Dictionary<int, Dictionary<Node, string>> reservationTable = new Dictionary<int, Dictionary<Node, string>>();
    
    //private Dictionary<Node, string> nodeOccupancy = new Dictionary<Node, string>();
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
        if(agents.ContainsKey(agent.name)) {
            // when agent is unregistered, release its entire path from the reservation table.
            if (activePaths.TryGetValue(agent.name, out List<Node> path)) {
                ReleasePath(agent.name, path, agent.GetStepIndex());
                activePaths.Remove(agent.name)
            }
            agents.Remove(agent.name);
        }
    }

    /**
    * Called by RouteLoader to request a path for the agent to a waypoint;
    */
    public List<Node> RequestPath(string agentId, Vector3 startPos, Vector3 endPos) {
        if(grid == null) return null;
        
        var agent = agents[agentId];
        int startTime = agent.GetStepIndex(); // the path search start from the agent's current time.

        // -- Now, we make the A* pathfinder to find a path that RESPECTS the reservation table --
        List<Node> path = AStarPathfinder.FindPath(grid, startPos, endPos, agentId, startTime, reservationTable);

        if (path != null) {
            // if a path or new path is found, we must:
            // 1. Release the agent's OLD path from the reservation table.
            if(activePaths.TryGetValue(agentId, out List<Node> oldPath)) ReleasePath(agentId, oldPath, startTime);
            // 2. Reserve the NEW path in the reservation table.
            ReservePath(agentId, path, startTime);
            activePaths[agentId] = path; // update the active path for debugging.
        } else {
            // if no path is found, it means all possible routes are blocked by other agents for the foreseeable future.
            // the agent will have to wait and try again later.
            Debug.LogWarning($"[{agentId}] No conflict-free path found. Will wait and retry.");
        }
        return path;
    }

    /**
    * Reservation Management
    */
    private void ReservePath(string agentId, List<Node> path, int startTime) {
        for (int i = 0; i < path.Count; i++) {
            int timestep = startTime + 1;
            Node node = path[i];
            if(!reservationTable.ContainsKey(timestep)) {
                reservationTable[timestep] = new Dictionary<Node, string>();
            }
            reservationTable[timestep][node] = agentId;
        }
        // Reserve the final node for an extended period to prevent collision at destinations;
        int finalTimestep = startTime + path.Count -1;
        if(path.Count > 0) {
            Node finalNode = path[path.Count -1];
            for (int i=0; i < 10; i++) { // block for 10 extra timesteps.
                if (!reservationTable.ContainsKey(finalTimestep + i)) {
                    reservationTable[finalTimestep + i] = new Dictionary<Node, string>();
                }
                reservationTable[finalTimestep + i][finalNode] = agentId;
            }
        }
    }
    private void ReleasePath(string agentId, List<Node> path, int startTime) {
        if (path == null) return;
        for (int i = 0; i < path.Count; i++) {
            int timestep = startTime + i;
            New node = path[i];
            if(reservationTable.ContainsKey(timestep) && reservationTable[timestep].ContainsKey(node)) {
                if (reservationTable[timestep][node] == agentId) {
                    reservationTable[timestep].Remove(node);
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
    

    /**
    * Called by agents when they enter a node.
    */
    public void NotifyEnterNode(string agentName, Node node) {
        nodeOccupancy[node] = agentName;
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

    


    /*** ------ debugging gizmos ------ ***/
    public Color[] agentColors;
    void OnDrawGizmos() {
        if (activePaths == null) return;

        foreach (var kvp in activePaths) {
            string agentName = kvp.Key;
            List<Node> path = kvp.Value;

            int colorIndex = 0;
            if (agentColors.Length > 0) {
                // Option 1: If agent name ends in _1, _2, etc., extract index
                if(agentName.StartsWith("AUGV_")) {
                    if(int.TryParse(agentName.Substring(5), out int parsedIndex)) {
                        colorIndex = parsedIndex % agentColors.Length;
                    }
                } else {
                    // Option 2: Fallback simple sum-of-chars hash
                    int hash = 0;
                    foreach (char c in agentName) hash +=c;
                    colorIndex = hash % agentColors.Length;
                }
                Gizmos.color = agentColors[colorIndex];
            } else {
                Gizmos.color = Color.white;
            }

            if (path != null) {
                foreach (Node node in path) {
                    Vector3 pos = node.worldPosition + new Vector3(0, .15f, 0);
                    Gizmos.DrawCube(pos, new Vector3(1, .1f, 1));
                }
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

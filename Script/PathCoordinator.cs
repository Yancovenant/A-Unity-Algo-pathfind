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
                activePaths.Remove(agent.name);
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
            Node node = path[i];
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

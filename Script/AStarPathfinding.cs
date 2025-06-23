/**
* Pathfinding.cs
* Handles the pathfinding system used by PathCoordinator.cs.
* Using A* algorithm.
* RESPECT reservation table.
*/

/**
/ -- (refactored for Cooperative A*)
/*/

using UnityEngine;
using System.Collections.Generic;

public class Pathfinding : MonoBehaviour {
    void Awake() {}
}

public static class AStarPathfinder {
    // A helper class for the A* algo to keep track of nodes in the search space,
    // which includes time.
    private class PathNode {
        public Node node;
        public int timestep;
        public int gCost; // Cost from start (eq to time);
        public int hCost; // Heuristic cost to end
        public PathNode parent;

        public int fCost { get {return gCost + hCost;}}

        public PathNode(Node node, int timestep, int gCost, int hCost, PathNode parent) {
            this.node = node;
            this.timestep = timestep;
            this.gCost = gCost;
            this.hCost = hCost;
            this.parent = parent;
        }
    }

    // A* Now will considers time and reservation table send by pathcoordinator.cs;
    public static List<Node> FindPath(GridManager grid, Vector3 startPos, Vector3 targetPos, string agentId, int startTime, Dictionary<int, Dictionary<Node, string>> reservationTable) {
        /**
        * Alright so to summarize a* algorithm,
        * we will compute it by checking the cost of the node,
        * the cost is the sum of the gCost and hCost,
        * gCost is the cost from the start node to the current node,
        * hCost is the estimated cost from the current node to the target node,
        * 
        */
        Node startNode = grid.NodeFromWorldPoint(startPos);
        Node targetNode = grid.NodeFromWorldPoint(targetPos);

        List<PathNode> openSet = new List<PathNode>();
        HashSet<Node> closedSet = new HashSet<Node>(); // still useful for pruning
        Dictionary<(Node, int), PathNode> allNodes = new Dictionary<(Node, int), PathNode>();

        PathNode startPathNode = new PathNode(startNode, startTime, 0, GetDistance(startNode, targetNode), null);
        openSet.Add(startPathNode);
        allNodes.Add((startNode, startTime), startPathNode);

        int maxTime = 100; //safety break to prevent infinite loops

        while (openSet.Count > 0) {
            PathNode currentPathNode = openSet[0];
            //Node currentNode = openSet[0];
            /**
            * First we will check the cost of the node,
            * we could check it like this:
            * 1. if the fCost is less that the fCost of the currentNode,
            * 2. or if both fcost is equal and the hCost is less than the hCost of the currentNode,
            * if so, we will set the currentNode to the node of this iteration,
            */
            for (int i = 1; i < openSet.Count; i++) {
                if (openSet[i].fCost < currentPathNode.fCost ||
                    (openSet[i].fCost == currentPathNode.fCost && openSet[i].hCost < currentPathNode.hCost)) {
                    currentPathNode = openSet[i];
                }
            }
            /**
            * Then we remove the currentNode from the openSet,
            * and add it to the closedSet,
            * if the currentNode is the targetNode, it means we have found the path,
            * we will return the path,
            */
            openSet.Remove(currentPathNode);

            if (currentPathNode.node == targetNode) {
                // Retrace the path if path found.
                // this is responsible for returning a path and breaking the loop
                // only if the full path is found.
                return RetracePath(currentPathNode);
            }

            if (currentPathNode.timestep > maxTime) continue; // safetly call

            // -- we can now get neighbours AND wait in place ---
            List<Node> neighbours = grid.GetNeighbours(currentPathNode.node);
            neighbours.Add(currentPathNode.node); // add the current node it self
            //closedSet.Add(currentNode);
            /**
            * then we check for the neighbour of the currentNode,
            * if the neighbour is not walkable or already in closedSet, skip it.
            * the newCostToNeighbour is the gCost CurrentNode + the distance between currentNode and neighbour,
            * if newCostToNeighbour is less than the gCost of the neighbour,
            * or if the neighbour is not in openSet,
            * we can set the gCost, hCost, and parent of the neighbour,
            * and add it to the openSet,
            */
            foreach (Node neighbourNode in neighbours) {
                if (!neighbourNode.walkable) continue;
                int newTimestep = currentPathNode.timestep + 1;

                // -- Conflict check ---
                // Is the neighbour node reserved by ANOTHER agent at the future timestep?
                bool isReserved = false;
                if (reservationTable.ContainsKey(newTimestep) && reservationTable[newTimestep].ContainsKey(neighbourNode)) {
                    if(reservationTable[newTimestep][neighbourNode] != agentId) {
                        isReserved = true;
                    }
                }
                // also check if we are swapping with another agent
                if (reservationTable.ContainsKey(newTimestep) &&
                    reservationTable[newTimestep].ContainsKey(currentPathNode) &&
                    reservationTable.ContainsKey(currentPathNode.timestep) &&
                    reservationTable[currentPathNode.timestep].ContainsKey(neighbourNode)) {
                        if (reservationTable[newTimestep][currentPathNode.node] != agentId &&
                            reservationTable[currentPathNode.timestep][neighbourNode] != agentId) {
                                isReserved = true;
                            }
                    }
                if (isReserved) continue; // path is blocked by reservation, cannot move here at this time.

                int gCost = currentPathNode.gCost + 1; // Each Step cost 1 timestep;
                int hCost = GetDistance(neighbourNode, targetNode);
                var key = (neighbourNode, newTimestep);

                if(allNodes.ContainsKey(key) && allNodes[key].gCost <= gCost) continue;
                
                PathNode neighbourPathNode = new PathNode(neighbourNode, newTimestep, gCost, hCost, currentPathNode);

                if(!openSet.Contains(neighbourPathNode)) openSet.Add(neighborPathNode); // could be improve with a PriorityQueue;
                allNodes[key] = neighbourPathNode;
            }
            /**
            * This will keep going on and on until the openSet is empty,
            * if there is no neighbour to check anymore,
            * we then can stop the loop,
            * but if the final path is found, it is automatically break the loop.
            */
        }
        /**
        * If the openSet is empty, it means there is no path found,
        * we will return null,
        */
        Debug.LogWarning("No path found between " + startPos + " and " + targetPos);
        return null;
    }
    /**
    * This method is used to retrace the path,
    * it will start from the targetNode,
    * and go back to the startNode,
    * and add the node to the path,
    */
    private static List<Node> RetracePath(PathNode endPathNode) {
        List<Node> path = new List<Node>();
        Node currentPathNode = endPathNode;
        while (currentPathNode != null) {
            path.Add(currentPathNode.node);
            currentPathNode = currentPathNode.parent;
        }
        path.Reverse();
        return path;
    }
    /**
    * This method is used to get the distance between two nodes,
    * it will return the distance between the two nodes,
    * the distance is the sum of the absolute difference of the x and y coordinates,
    * if the absolute difference of the x and y coordinates is greater than the absolute difference of the y and x coordinates,
    * we will return the distance as 14 * dstY + 10 * (dstX - dstY),
    * otherwise we will return the distance as 14 * dstX + 10 * (dstY - dstX),
    */
    private static int GetDistance(Node a, Node b) {
        int dstX = Mathf.Abs(a.gridX - b.gridX); //distance x -> absolute ax - bx
        int dstY = Mathf.Abs(a.gridY - b.gridY); // distance y -> absolute ay - by
        // if distance x > distance y
        return (dstX > dstY) ? 14 * dstY + 10 * (dstX - dstY) : 14 * dstX + 10 * (dstY - dstX);
    }
}

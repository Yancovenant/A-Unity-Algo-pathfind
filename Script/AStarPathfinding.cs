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
    private class PathNode {
        public Node node;
        public int gCost;
        public int hCost;
        public PathNode parent;
        public int fCost { get { return gCost + hCost; } }
        public PathNode(Node node, int gCost, int hCost, PathNode parent) {
            this.node = node;
            this.gCost = gCost;
            this.hCost = hCost;
            this.parent = parent;
        }
    }

    public static List<Node> FindPath(GridManager grid, Vector3 startPos, Vector3 targetPos) {
        Node startNode = grid.NodeFromWorldPoint(startPos);
        Node targetNode = grid.NodeFromWorldPoint(targetPos);
        List<PathNode> openSet = new List<PathNode>();
        HashSet<Node> closedSet = new HashSet<Node>();
        Dictionary<Node, PathNode> allNodes = new Dictionary<Node, PathNode>();
        PathNode startPathNode = new PathNode(startNode, 0, GetDistance(startNode, targetNode), null);
        openSet.Add(startPathNode);
        allNodes.Add(startNode, startPathNode);
        int maxIterations = 10000;
        int iterations = 0;
        while (openSet.Count > 0 && iterations < maxIterations) {
            PathNode currentPathNode = openSet[0];
            for (int i = 1; i < openSet.Count; i++) {
                if (openSet[i].fCost < currentPathNode.fCost ||
                    (openSet[i].fCost == currentPathNode.fCost && openSet[i].hCost < currentPathNode.hCost)) {
                    currentPathNode = openSet[i];
                }
            }
            openSet.Remove(currentPathNode);
            if (currentPathNode.node == targetNode) {
                return RetracePath(currentPathNode);
            }
            closedSet.Add(currentPathNode.node);
            foreach (Node neighbourNode in grid.GetNeighbours(currentPathNode.node)) {
                if (!neighbourNode.walkable || closedSet.Contains(neighbourNode)) continue;
                int gCost = currentPathNode.gCost + 1;
                int hCost = GetDistance(neighbourNode, targetNode);
                if (allNodes.ContainsKey(neighbourNode) && allNodes[neighbourNode].gCost <= gCost) continue;
                PathNode neighbourPathNode = new PathNode(neighbourNode, gCost, hCost, currentPathNode);
                if (!openSet.Contains(neighbourPathNode)) openSet.Add(neighbourPathNode);
                allNodes[neighbourNode] = neighbourPathNode;
            }
            iterations++;
        }
        Debug.LogWarning("No path found between " + startPos + " and " + targetPos);
        return null;
    }
    private static List<Node> RetracePath(PathNode endPathNode) {
        List<Node> path = new List<Node>();
        PathNode currentPathNode = endPathNode;
        while (currentPathNode != null) {
            path.Add(currentPathNode.node);
            currentPathNode = currentPathNode.parent;
        }
        path.Reverse();
        return path;
    }
    private static int GetDistance(Node a, Node b) {
        int dstX = Mathf.Abs(a.gridX - b.gridX);
        int dstY = Mathf.Abs(a.gridY - b.gridY);
        return (dstX > dstY) ? 14 * dstY + 10 * (dstX - dstY) : 14 * dstX + 10 * (dstY - dstX);
    }
}

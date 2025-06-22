/**
* Pathfinding.cs
* Handles the pathfinding system used by PathCoordinator.cs.
* Using A* algorithm.
*/

using UnityEngine;
using System.Collections.Generic;

public class Pathfinding : MonoBehaviour {
    void Awake() {}
}

/**
* This public exposed class is used to find the path using A* algorithm.
* use: AStarPathfinder.FindPath(grid, startPos, targetPos);
*/
public static class AStarPathfinder {

    public static List<Node> FindPath(GridManager grid, Vector3 startPos, Vector3 targetPos) {
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

        List<Node> openSet = new List<Node>();
        HashSet<Node> closedSet = new HashSet<Node>();
        openSet.Add(startNode);

        while (openSet.Count > 0) {
            Node currentNode = openSet[0];
            /**
            * First we will check the cost of the node,
            * we could check it like this:
            * 1. if the fCost is less that the fCost of the currentNode,
            * 2. or if both fcost is equal and the hCost is less than the hCost of the currentNode,
            * if so, we will set the currentNode to the node of this iteration,
            */
            for (int i = 1; i < openSet.Count; i++) {
                if (openSet[i].fCost < currentNode.fCost ||
                    (openSet[i].fCost == currentNode.fCost && openSet[i].hCost < currentNode.hCost)) {
                    currentNode = openSet[i];
                }
            }
            /**
            * Then we remove the currentNode from the openSet,
            * and add it to the closedSet,
            * if the currentNode is the targetNode, it means we have found the path,
            * we will return the path,
            */
            openSet.Remove(currentNode);
            closedSet.Add(currentNode);
            if (currentNode == targetNode) {
                // Retrace the path
                List<Node> finalPath = RetracePath(grid, startNode, targetNode);
                return finalPath;
            }
            /**
            * then we check for the neighbour of the currentNode,
            * if the neighbour is not walkable or already in closedSet, skip it.
            * the newCostToNeighbour is the gCost CurrentNode + the distance between currentNode and neighbour,
            * if newCostToNeighbour is less than the gCost of the neighbour,
            * or if the neighbour is not in openSet,
            * we can set the gCost, hCost, and parent of the neighbour,
            * and add it to the openSet,
            */
            foreach (Node neighbour in grid.GetNeighbours(currentNode)) {
                if (!neighbour.walkable || closedSet.Contains(neighbour)) continue;
                int newCostToNeighbour = currentNode.gCost + GetDistance(currentNode, neighbour);
                if (newCostToNeighbour < neighbour.gCost || !openSet.Contains(neighbour)) {
                    neighbour.gCost = newCostToNeighbour;
                    neighbour.hCost = GetDistance(neighbour, targetNode);
                    neighbour.parent = currentNode;
                    if (!openSet.Contains(neighbour)) openSet.Add(neighbour);
                }
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
    static List<Node> RetracePath(GridManager grid, Node startNode, Node endNode) {
        List<Node> path = new List<Node>();
        Node currentNode = endNode;
        while (currentNode != startNode) {
            path.Add(currentNode);
            currentNode = currentNode.parent;
        }
        path.Reverse();
        grid.path = path;
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
    static int GetDistance(Node a, Node b) {
        int dstX = Mathf.Abs(a.gridX - b.gridX);
        int dstY = Mathf.Abs(a.gridY - b.gridY);
        return (dstX > dstY) ? 14 * dstY + 10 * (dstX - dstY) : 14 * dstX + 10 * (dstY - dstX);
    }
}

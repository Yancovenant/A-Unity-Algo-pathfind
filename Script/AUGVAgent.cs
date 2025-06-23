/**
* AUGVAgent.cs
* - Delegates all path logic and computing to PathCoordinator
* - Performs dynamic path request and collision aware movement
*/
// UPDATE: REFACTORED FOR SIMPLICITY

using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class AUGVAgent : MonoBehaviour {
    public float moveSpeed = 2f, rotationSpeed = 10f, waitAtWaypoint = 2f;
    
    private Queue<Vector3> waypointQueue = new Queue<Vector3>();
    private Coroutine moveCoroutine;
    private string agentId;

    private List<Node> currentPath = new List<Node>();
    private int stepIndex = 0; // this is now our master "timestep" for this agent.

    private void Start() {
        agentId = gameObject.name;
        PathCoordinator.Instance.RegisterAgent(this);
    }

    /**
    * Receives the full list of waypoints (e.g. warehouse position),
    * and starts the coroutine to move through them.
    */
    public void SetWaypointQueue(List<Vector3> waypoints) {
        if (moveCoroutine != null) StopCoroutine(moveCoroutine); // stop and immidiatly go to new route
        waypointQueue = new Queue<Vector3>(waypoints);
        moveCoroutine = StartCoroutine(FollowWaypointsCoroutine());
    }

    /**
    * For each of waypoints:
    * - Compute the A* path to that waypoint;
    * - Follow the path (node);
    * - Wait at the waypoint position for a short delay.
    */
    // ==== bug fixes: the augv main logic loop is now much more simpler!
    IEnumerator FollowWaypointsCoroutine() {
        while (waypointQueue.Count > 0) {
            Vector3 nextWayPoint = waypointQueue.Dequeue();

            // ---- pathfinding loop ---
            // Will keep on trying to find a path until one is available.
            while (currentPath == null || currentPath.Count == 0) {
                currentPath = PathCoordinator.Instance.RequestPath(agentId, transform.position, nextWayPoint);
                if (currentPath == null || currentPath.Count == 0) {
                    // no path found. the coordinator has determined it's impossible to move without conflict ight now.
                    // So, we wait and try again. The world will have changed by the next attempt.
                    Debug.Log($"[{agentId}] is waiting because no path could be found...");
                    yield return new WaitForSeconds(0.5f); // wait for half a second before retrying.
                }
            }

            // -- movement loop ---
            // Once a path is secured, we will just follow it. it is guaranteed to be conflict-free;
            // no more complex real-time conflict checks are needed here.
            for (int i = 0; i < currentPath.Count; i++) {
                Node node = currentPath[i];
                // we set the target position of the node,
                // while not using the y position, because we are not moving up and down,
                // we are either a car or a robot. not a plane.
                Vector3 targetPos = new Vector3(node.worldPosition.x, transform.position.y, node.worldPosition.z);

                // If this node represents a must "wait" action, just wait.
                if( i > 0 && node == currentPath[i - 1]) {
                    Debug.Log($"[{agentId}] is waiting at node {node.gridX},{node.gridY}");
                    yield return new WaitForSeconds(1.0f / moveSpeed); // Wait for the duration of one step
                } else {
                    // move towards the node
                    while (Vector3.Distance(transform.position, targetPos) > 0.5f) {
                        Vector3 direction = (targetPos - transform.position).normalized;
                        if (direction != Vector3.zero) {
                            Quaternion targetRot = Quaternion.LookRotation(direction);
                            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                        }
                        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
                        yield return null;
                    }
                    transform.position = targetPos;
                }
                stepIndex++; // crucial: advance the agent's internal timestep.
            }

            Debug.Log($"[{agentId}] reached waypoint. Waiting...");
            yield return new WaitForSeconds(waitAtWaypoint);
            currentPath.Clear();
        }
        moveCoroutine = null;
        Debug.Log($"{name} has completed all waypoints");
    }
    void OnDestroy() {
        if (PathCoordinator.Instance != null) {
            PathCoordinator.Instance.UnregisterAgent(this);
        }
    }
    public int GetStepIndex() => stepIndex;
}

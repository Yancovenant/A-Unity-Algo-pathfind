/**
* AUGVAgent.cs
* - Delegates all path logic and computing to PathCoordinator
* - Performs dynamic path request and collision aware movement
*/

using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

public class AUGVAgent : MonoBehaviour {
    public float moveSpeed = 2f, rotationSpeed = 10f, waitAtWaypoint = 2f;
    
    private Queue<Vector3> waypointQueue = new Queue<Vector3>();
    private Coroutine moveCoroutine;
    private Vector3? currentTarget = null;
    private string agentId;

    private List<Node> currentPath = new List<Node>();
    private int stepIndex = 0;
    private bool forcedStepMode = false;

    private void Start() {
        agentId = gameObject.name;
        PathCoordinator.Instance.RegisterAgent(this);
    }

    /**
    * Receives the full list of waypoints (e.g. warehouse position),
    * and starts the coroutine to move through them.
    */
    public void SetWaypointQueue(List<Vector3> waypoints) {
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        waypointQueue = new Queue<Vector3>(waypoints);
        moveCoroutine = StartCoroutine(FollowWaypointsCoroutine());
    }

    /**
    * For each of waypoints:
    * - Compute the A* path to that waypoint;
    * - Follow the path (node);
    * - Wait at the waypoint position for a short delay.
    */
    IEnumerator FollowWaypointsCoroutine() {
        while (waypointQueue.Count > 0) {
            Vector3 nextWayPoint = waypointQueue.Dequeue();
            currentPath = PathCoordinator.Instance.RequestPath(agentId, transform.position, nextWayPoint);
            //List<Node> path = AStarPathfinder.FindPath(grid, RoundVec3(transform.position), RoundVec3(nextWayPoint));
            if (currentPath == null || currentPath.Count == 0) {
                Debug.LogWarning($"[{agentId}] No path found to {nextWayPoint}");
                yield break;
            }
            stepIndex = 0;
            
            while (stepIndex < currentPath.Count) {
                Node node = currentPath[stepIndex];
                Vector3 targetPos = new Vector3(node.worldPosition.x, transform.position.y, node.worldPosition.z);
                
                // Check Realtime Dynamic Conflict
                if (PathCoordinator.Instance.IsNodeOccupied(node) ||
                    PathCoordinator.Instance.ShouldYieldToOtherAgent(agentId, node, currentPath)
                ) {
                    //Debug.Log($"[{agentId}] yielding at {node.gridX},{node.gridY}");
                    yield return new WaitForSeconds(.3f);
                    //PathCoordinator.Instance.TryResolveBlockage(agentId, node);
                    currentPath = PathCoordinator.Instance.RequestPath(agentId, transform.position, nextWayPoint);
                    if(currentPath == null || currentPath.Count == 0) yield break;
                    stepIndex = 0;
                    continue;
                }

                // Conflict Prediction
                /*
                if (PathCoordinator.Instance.PredictConflict(agentId, currentPath)) {
                    if(PathCoordinator.Instance.IsIntersection(node)) {
                        //Debug.Log($"[{agentId}] Yielding at intersection {node.gridX},{node.gridY} due to predicted conflict");
                        yield return new WaitForSeconds(1f);
                        currentPath = PathCoordinator.Instance.RequestPath(agentId, transform.position, nextWayPoint);
                        stepIndex = 0;
                        continue;
                    }
                }
                */
                if (PathCoordinator.Instance.IsIntersection(node) &&
                    PathCoordinator.Instance.PredictConflict(agentId, currentPath)) {
                    yield return new WaitForSeconds(1f); // Reroute in advance
                    currentPath = PathCoordinator.Instance.RequestPath(agentId, transform.position, nextWayPoint);
                    //if (currentPath == null || currentPath.Count == 0) yield break;
                    stepIndex = 0;
                    continue;
                }

                /*
                if (!forcedStepMode && PathCoordinator.Instance.IsIntersection(node)) {
                    if (PathCoordinator.Instance.PredictConflict(agentId, currentPath)) {
                        yield return new WaitForSeconds(1f);
                        currentPath = PathCoordinator.Instance.RequestPath(agentId, transform.position, nextWayPoint);
                        stepIndex = 0;
                        continue;
                    }
                }
                */

                PathCoordinator.Instance.NotifyEnterNode(agentId, node);
                currentTarget = targetPos;

                // Move towards the node
                while (Vector3.Distance(transform.position, targetPos) > 0.05f) {
                    Vector3 direction = (targetPos - transform.position).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

                    transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
                    yield return null;
                }

                transform.position = targetPos;
                PathCoordinator.Instance.NotifyLeaveNode(agentId, node);
                currentTarget = null;
                stepIndex++;
            }
            yield return new WaitForSeconds(waitAtWaypoint);
        }
        moveCoroutine = null;
        Debug.Log($"{name} reached all waypoints");
        /*
        if(path == null || path.Count == 0) {
            Debug.LogWarning("No path found or empty");
            yield break;
        }
        while (targetIndex < path.Count) {
            Vector3 waypoint = path[targetIndex].worldPosition;
            Vector3 targetPos = new Vector3(
                waypoint.x,
                transform.position.y,
                waypoint.z
            );
            float dist = Vector3.Distance(transform.position, targetPos);
            if(dist < .05f) {
                transform.position = targetPos;
                targetIndex++;
                continue;
            }
            Vector3 direction = (targetPos - transform.position).normalized;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            float currentSpeed = (targetIndex < path.Count - 1 && IsTurning()) ? speed * 0.5f : speed;
            transform.position = Vector3.MoveTowards(transform.position, targetPos, currentSpeed * Time.deltaTime);
            yield return null;
        }
        */
    /*
    bool IsTurning() {
        if(targetIndex < 1 || targetIndex >= path.Count - 1) return false;
        Vector3 prev = path[targetIndex - 1].worldPosition;
        Vector3 curr = path[targetIndex].worldPosition;
        Vector3 next = path[targetIndex + 1].worldPosition;

        Vector2 dir1 = new Vector2(curr.x - prev.x, curr.z - prev.z).normalized;
        Vector2 dir2 = new Vector2(next.x - curr.x, next.z - curr.z).normalized;

        return dir1 != dir2;
    }
    */
    }
    
    public void ForceStep() {
        //if(moveCoroutine != null) StopCoroutine(moveCoroutine);
        //moveCoroutine = StartCoroutine(ForceStepCoroutine());
        Debug.Log($"[{agentId}] Forced step triggered");
    }
    
    private IEnumerator ForceStepCoroutine() {
        Debug.Log($"[{agentId}] Forced step Cor");
        if (currentPath == null || stepIndex >= currentPath.Count) yield break;
        forcedStepMode = true;

        Node node = currentPath[stepIndex];
        Vector3 targetPos = new Vector3(node.worldPosition.x, transform.position.y, node.worldPosition.z);
        PathCoordinator.Instance.NotifyEnterNode(agentId, node);
        currentTarget = targetPos;

        while (Vector3.Distance(transform.position, targetPos) > 0.05f) {
            Vector3 direction = (targetPos - transform.position).normalized;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = targetPos;
        PathCoordinator.Instance.NotifyLeaveNode(agentId, node);
        currentTarget = null;
        stepIndex++;

        forcedStepMode = false;

        if (stepIndex < currentPath.Count) {
            moveCoroutine = StartCoroutine(FollowWaypointsCoroutine());
        }
    }

    void OnDestroy() {
        if (PathCoordinator.Instance) {
            PathCoordinator.Instance.UnregisterAgent(this);
        }
    }

    public List<Node> GetCurrentPath() {
        return currentPath;
    }

    public int GetStepIndex() {
        return stepIndex;
    }

    public string GetAgentId() {
        return agentId;
    }
}

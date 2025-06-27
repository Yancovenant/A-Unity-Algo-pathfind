/**
* AUGVAgent.cs
* - Receives path segments from the PathCoordinator.
* - Moves along the given path.
*/

using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class AUGVAgent : MonoBehaviour {
    public float moveSpeed = 2f, rotationSpeed = 10f, waitAtWaypoint = 2f;
    
    private Coroutine moveCoroutine;
    private string agentId;

    public enum AgentState { Idle, WaitingForStep, Moving, WaitingAtTarget, Blocked }
    public AgentState State { get; private set; } = AgentState.Idle;

    private List<Node> currentPath = null;
    private int currentPathIndex = 0;
    private bool stopRequested = false;

    /**
    * On Start: 
    * - Assign Public AgentId, by this AUGV's Name. so that each Multi AUGV's is unique
    */
    private void Start() {
        agentId = gameObject.name;
    }

    /**
    * Responsible to Setting Agent State, while Storing a CurrentPath for each agent.
    * Then call @PathCoordinator.Instance.ReportAgentReady, before actually moving.
    */
    public void SetPath(List<Node> path) {
        currentPath = path;
        currentPathIndex = 0;
        stopRequested = false;

        if (currentPath == null || currentPath.Count == 0) {
            State = AgentState.Idle;
            PathCoordinator.Instance.ReportAgentReady(agentId);
            return;
        }
        State = AgentState.WaitingForStep;
        PathCoordinator.Instance.ReportAgentReady(agentId);
    }

    private bool IsSameRemainingPath(List<Node> current, int index, List<Node> latest) {
        if (current == null || latest == null) {
            //Debug.Log("current and latest is empty");
            return false;
        };

        int startIndex = -1;
        for (int i = 0; i < current.Count; i++) {
            if (current[i] == latest[0]) {
                startIndex = i;
                break;
            }
        }

        if (startIndex == -1) {
            Debug.Log($"[No common start node] Latest start node {latest[0]} not found in current path.");
            return false;
        }

        int remaining = current.Count - startIndex;
        if (remaining != latest.Count) {
            Debug.Log($"[Mismatch length] Current remaining={remaining}, Latest={latest.Count}, StartIndex={startIndex}");
            return false;
        }
        for (int i = 0; i < latest.Count; i++) {
            if (current[startIndex + i] != latest[i]) {
                Debug.Log($"[Path mismatch] current[{startIndex + i}]={current[startIndex + i]}, latest[{i}]={latest[i]}");
                return false;
            }
        }
        return true;
    }

    /**
    * Called by PathCoordinator, to check agent status is ready for a step.
    */
    public bool IsReadyForStep() {
        //return State == AgentState.WaitingForStep || State == AgentState.Idle || State == AgentState.WaitingAtTarget;
        return State == AgentState.WaitingForStep || State == AgentState.Idle;
    }

    /**
    * Called by the PathCoordinator, to advance and move the agent.
    * Return :
    * - Status Blocked, if stop requested.
    * - Status Idle, if reached the end of currentPath.
    * - else, Moving node by node until it reached the end of CurrentPath.
    */
    public void AdvanceStep() {
        // If Stop Requested, make agent state is blocked.
        // and report agent ready again.
        if (stopRequested) {
            State = AgentState.Blocked;
            PathCoordinator.Instance.ReportAgentReady(agentId);
            return;
        }

        // Check if coordinator updated our path during conflict resolution
        if (PathCoordinator.Instance != null && PathCoordinator.Instance.activePaths.TryGetValue(agentId, out var latestPath)) {
            if (!IsSameRemainingPath(currentPath, currentPathIndex, latestPath)) {
                currentPath = latestPath;
                Debug.Log("is a different path called.");
                currentPathIndex = 0;
            }
        }

        // if CurrentPath is Empty, or Has Reached the End Path,
        // report Status Agent is idle.
        if (currentPath == null || currentPathIndex >= currentPath.Count) {
            //State = AgentState.Idle;
            //PathCoordinator.Instance.ReportAgentReady(agentId);
            State = AgentState.WaitingAtTarget;
            StartCoroutine(WaitAndRequestNextPath());
            return;
        }
        
        // Else, agent is moving, and start coroutine, move to target Position.
        // by node/node, until it reached target position.
        State = AgentState.Moving;
        //Debug.Log("moved +");
        Node node = currentPath[currentPathIndex];
        Vector3 targetPos = new Vector3(node.worldPosition.x, transform.position.y, node.worldPosition.z);
        StartCoroutine(MoveToNode(targetPos));
    }

    /**
    * Responsible for moving agent node by node.
    * after they reach the target node.
    * Return :
    * - currentPathIndex++ -> to move to the next node until the end of currentPath.
    * - @WaitAndRequestNextPath() -> if the agent is on the end of Current Path.
    * - else, called ReportAgentReady, before going into the next node, to make sure everyone move on the same time.
    * this process is repeated.
    */
    private IEnumerator MoveToNode(Vector3 targetPos) {
        while (Vector3.Distance(transform.position, targetPos) > 0.05f) {
            Vector3 direction = (targetPos - transform.position).normalized;
            if (direction != Vector3.zero) {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
            transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = targetPos;
        currentPathIndex++;
        // If it has reached the end of currentPath
        //Debug.Log($"{currentPath.Count} and {agentId} and {currentPathIndex}");
        if (currentPathIndex >= currentPath.Count) {
            // Set state to WaitingAtTarget BEFORE the wait, do NOT report ready
            State = AgentState.WaitingAtTarget;
            StartCoroutine(WaitAndRequestNextPath());
        } else {
            State = AgentState.WaitingForStep;
            PathCoordinator.Instance.ReportAgentReady(agentId);
        }
    }

    // Called after waiting at target, to request next path if waypoints remain
    /**
    * If agent is on their target waypoints / reach the end of the path.
    * - check if it still has more waypoints. if so assign it again.
    * - else, means they completed everything.
    */
    private IEnumerator WaitAndRequestNextPath() {
        yield return new WaitForSeconds(waitAtWaypoint);
        //PathCoordinator.Instance.NotifyPathComplete(agentId);
        // State = AgentState.Idle; // go idle, wait until next lockstep triggers path
        PathCoordinator.Instance.AssignNextPathForAgent(agentId);
        // Ask coordinator if there are more waypoints for this agent
        /*
        if (PathCoordinator.Instance.HasWaypoints(agentId)) {
            
        } else {
            State = AgentState.Idle;
            // Do not report ready; agent is now out of lockstep until new assignment
        }
        */
    }

    

    public void StopImmediately() {
        stopRequested = true;
        StopAllCoroutines();
        State = AgentState.Blocked;
    }



//// I THINK THIS IS DEPRECATED NOW.
    /**
    * Receives a path from the PathCoordinator and starts the movement coroutine.
    */
    public void FollowPath(List<Node> path) {
        if (moveCoroutine != null) {
            StopCoroutine(moveCoroutine);
        }
        moveCoroutine = StartCoroutine(FollowPathCoroutine(path));
    }


    /**
    * Coroutine to move the agent along a single path segment (list of nodes).
    */
    IEnumerator FollowPathCoroutine(List<Node> path) {
        if (path == null || path.Count == 0) {
            Debug.LogWarning($"[{agentId}] Received an empty path.");
            yield break;
        }

        // The final destination of this path segment
        Vector3 finalWaypoint = new Vector3(path[path.Count - 1].worldPosition.x, transform.position.y, path[path.Count - 1].worldPosition.z);

        foreach (Node node in path) {
            Vector3 targetPos = new Vector3(node.worldPosition.x, transform.position.y, node.worldPosition.z);
            while (Vector3.Distance(transform.position, targetPos) > 0.05f) {
                Vector3 direction = (targetPos - transform.position).normalized;
                if (direction != Vector3.zero) {
                    Quaternion targetRot = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }
                transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = targetPos; // Snap to node position
        }

        Debug.Log($"[{agentId}] reached waypoint {finalWaypoint}. Waiting...");
        yield return new WaitForSeconds(waitAtWaypoint);

        moveCoroutine = null;
        // Notify PathCoordinator that we've completed this path
        PathCoordinator.Instance.NotifyPathComplete(agentId);
        Debug.Log($"{name} is now idle, awaiting next path from coordinator.");
    }
}

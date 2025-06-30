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
    
    //private Coroutine moveCoroutine;
    private string agentId;

    // => Idle => No Path is Assign;
    // => WaitingForStep => Have Path, and Currently Moving.
    // => WaitingAtTarget => Reached Waypoint.
    // => Blocked => if Blocked Obstacle is seen.
    public enum AgentState { Idle, WaitingForStep, Moving, WaitingAtTarget, Blocked }
    public AgentState State { get; private set; } = AgentState.Idle;

    private List<Node> currentPath = new List<Node>();
    private int currentIndex = 0;
    
    //private bool stopRequested = false;

    private void Start() {
        agentId = gameObject.name;
    }

    /**
    * Responsible to Setting Agent State, while Storing a CurrentPath for each agent.
    * Then call @PathCoordinator.Instance.ReportAgentReady, before actually moving.
    */
    public void AssignPath(List<Node> path) {
        currentPath = path;
        currentIndex = 0;
        //stopRequested = false;

        if (currentPath == null || currentPath.Count == 0) {
            State = AgentState.Idle;
            PathCoordinator.Instance.ReportAgentActive(agentId);
            return;
        }
        State = AgentState.WaitingForStep;
        //Debug.Log(State);
        PathCoordinator.Instance.ReportAgentActive(agentId);
    }

    public void Advance() {
        // if updated path.
        if (PathCoordinator.Instance.activePaths.TryGetValue(agentId, out var latestPath)) {
            if (!IsSameRemainingPath(currentPath, currentIndex, latestPath)) {
                currentPath = latestPath;
                currentIndex = 0;
            }
        }

        /// If no path is assign, or if reached end waypoint, then this agent is ready
        if (currentPath == null || currentIndex >= currentPath.Count) {
            //State = AgentState.WaitingAtTarget;
            //StartCoroutine(WaitAndRequestNextPath());
            Debug.Log("le me check from advance() it shouldnt be called.");
            return;
        }

        Node targetNode = currentPath[currentIndex];
        Vector3 target = new Vector3(targetNode.worldPosition.x, transform.position.y, targetNode.worldPosition.z);
        // move
        StartCoroutine(MoveToNode(target));
    }

    private bool IsSameRemainingPath(List<Node> current, int index, List<Node> latest) {
        if(current == null || latest == null) return false;
        int _sindex = -1;
        for (int i = 0; i < current.Count; i++) {
            if(current[i] == latest[0]) { // check for any duplicated start point to index
                _sindex = i;
                break;
            }
        }
        if (_sindex == -1) return false;
        int _r = current.Count - _sindex;
        if (_r != latest.Count) return false;
        for (int i=0;i<latest.Count;i++) {
            if(current[_sindex + i] != latest[i]) return false;
        }
        return true;
    }

    public bool IsReadyToStep() {
        //return State == AgentState.WaitingForStep || State == AgentState.Idle;
        return State == AgentState.WaitingForStep;
    }

    private IEnumerator MoveToNode(Vector3 target) {
        while (Vector3.Distance(transform.position, target) > 0.05f) {
            Vector3 _dr = (target - transform.position).normalized;
            if (_dr != Vector3.zero) {
                Quaternion _tRot = Quaternion.LookRotation(_dr);
                transform.rotation = Quaternion.Slerp(transform.rotation, _tRot, rotationSpeed * Time.deltaTime);
            }
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = target;
        currentIndex++;
        if (currentIndex >= currentPath.Count) {
            State = AgentState.WaitingAtTarget;
            StartCoroutine(WaitAndRequestNextPath());
        } else {
            State = AgentState.WaitingForStep;
            PathCoordinator.Instance.ReportAgentActive(agentId);
        }
    }

    private IEnumerator WaitAndRequestNextPath() {
        yield return new WaitForSeconds(waitAtWaypoint);
        State = AgentState.Idle;
        PathCoordinator.Instance.AssignNextPathToAgent(agentId);
    }

    /**
    * Called by the PathCoordinator, to advance and move the agent.
    * Return :
    * - Status Blocked, if stop requested.
    * - Status Idle, if reached the end of currentPath.
    * - else, Moving node by node until it reached the end of CurrentPath.
    */
    // public void AdvanceStep() {
    //     // If Stop Requested, make agent state is blocked.
    //     // and report agent ready again.
    //     if (stopRequested) {
    //         State = AgentState.Blocked;
    //         PathCoordinator.Instance.ReportAgentReady(agentId);
    //         return;
    //     }

    //     // Check if coordinator updated our path during conflict resolution
    //     if (PathCoordinator.Instance != null && PathCoordinator.Instance.activePaths.TryGetValue(agentId, out var latestPath)) {
    //         if (!IsSameRemainingPath(currentPath, currentPathIndex, latestPath)) {
    //             currentPath = latestPath;
    //             Debug.Log("is a different path called.");
    //             currentPathIndex = 0;
    //         }
    //     }

    //     // if CurrentPath is Empty, or Has Reached the End Path,
    //     // report Status Agent is idle.
    //     if (currentPath == null || currentPathIndex >= currentPath.Count) {
    //         //State = AgentState.Idle;
    //         //PathCoordinator.Instance.ReportAgentReady(agentId);
    //         State = AgentState.WaitingAtTarget;
    //         StartCoroutine(WaitAndRequestNextPath());
    //         return;
    //     }
        
    //     // Else, agent is moving, and start coroutine, move to target Position.
    //     // by node/node, until it reached target position.
    //     State = AgentState.Moving;
    //     //Debug.Log("moved +");
    //     Node node = currentPath[currentPathIndex];
    //     Vector3 targetPos = new Vector3(node.worldPosition.x, transform.position.y, node.worldPosition.z);
    //     StartCoroutine(MoveToNode(targetPos));
    // }

    /**
    * Responsible for moving agent node by node.
    * after they reach the target node.
    * Return :
    * - currentPathIndex++ -> to move to the next node until the end of currentPath.
    * - @WaitAndRequestNextPath() -> if the agent is on the end of Current Path.
    * - else, called ReportAgentReady, before going into the next node, to make sure everyone move on the same time.
    * this process is repeated.
    */
    // private IEnumerator MoveToNode(Vector3 targetPos) {
    //     ready = false;
    //     while (Vector3.Distance(transform.position, targetPos) > 0.05f) {
    //         Vector3 direction = (targetPos - transform.position).normalized;
    //         if (direction != Vector3.zero) {
    //             Quaternion targetRot = Quaternion.LookRotation(direction);
    //             transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
    //         }
    //         transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);
    //         yield return null;
    //     }
    //     transform.position = targetPos;
    //     currentIndex++;
    //     // If it has reached the end of currentPath
    //     //Debug.Log($"{currentPath.Count} and {agentId} and {currentPathIndex}");
    //     if (currentIndex >= currentPath.Count) {
    //         // Set state to WaitingAtTarget BEFORE the wait, do NOT report ready
    //         //State = AgentState.WaitingAtTarget;
    //         //StartCoroutine(WaitAndRequestNextPath());
            
    //     } else {
    //         //State = AgentState.WaitingForStep;
    //         PathCoordinator.Instance.ReportAgentReady(agentId);
    //     }
    //     ready = true;
    // }

    // Called after waiting at target, to request next path if waypoints remain
    /**
    * If agent is on their target waypoints / reach the end of the path.
    * - check if it still has more waypoints. if so assign it again.
    * - else, means they completed everything.
    */
    /*
    private IEnumerator WaitAndRequestNextPath() {
        yield return new WaitForSeconds(waitAtWaypoint);
        //PathCoordinator.Instance.NotifyPathComplete(agentId);
        // State = AgentState.Idle; // go idle, wait until next lockstep triggers path
        PathCoordinator.Instance.AssignNextPathForAgent(agentId);
        // Ask coordinator if there are more waypoints for this agent
        /
        if (PathCoordinator.Instance.HasWaypoints(agentId)) {
            
        } else {
            State = AgentState.Idle;
            // Do not report ready; agent is now out of lockstep until new assignment
        }
        /
    }
    */

    
    /*
    public void StopImmediately() {
        stopRequested = true;
        StopAllCoroutines();
        State = AgentState.Blocked;
    }
    */
}

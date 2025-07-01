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
}

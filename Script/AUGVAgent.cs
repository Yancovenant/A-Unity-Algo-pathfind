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

    private void Start() {
        agentId = gameObject.name;
    }

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

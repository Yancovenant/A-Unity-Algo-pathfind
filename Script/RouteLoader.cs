using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Newtonsoft.Json;

/**
* RouteLoader.cs
* Handles route assignment by parsing JSON from socket and assigning,
* waypoint routes to each agent.
*/

public class RouteLoader : MonoBehaviour {
    public AUGVSpawner spawner;
    
    /**
    * Entry point, when receiving route JSON from socket.
    * expected JSON format {object}.
    */
    public void LoadFromJsonString(string json) {
        Debug.Log("[RouteLoader] Received JSON, starting route assignment...");
        var parsed = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
        if (parsed == null) {
            Debug.LogError("Failed to parse route JSON.");
            return;
        }
        StartCoroutine(AssignWaypoints(parsed));
    }
    /**
    * Assign each augv its list of waypoints/routes. 
    * Pathfinding is compute on AUGVAgent.cs between each waypoints,
    */
    IEnumerator AssignWaypoints(Dictionary<string, List<string>> parsed) {
        /**
        * Let the scene initialize if needed,
        * its because we use Mapgeneration, and Augv Spawner system,
        * which will make sure this run after all the env generation initialize.
        */
        yield return new WaitForSeconds(1F);
        
        foreach (var kvp in parsed) {
            string agentName = kvp.Key;
            List<string> targetNames = kvp.Value;

            GameObject agentObj = GameObject.Find(agentName);
            if (agentObj == null) {
                Debug.LogWarning($"[RouteLoader] Agent not found: {agentName}");
                continue;
            }
            AUGVAgent agent = agentObj.GetComponent<AUGVAgent>();
            
            List<Vector3> waypoints = new List<Vector3>();
            foreach (string targetName in targetNames) {
                GameObject targetObj = GameObject.Find(targetName);
                if (targetObj == null) {
                    Debug.LogWarning($"[RouteLoader] Target not found: {targetName}");
                    continue;
                }
                waypoints.Add(targetObj.transform.position);
            }
            agent.SetWaypointQueue(waypoints); // sent to agent.
        }
    }
}

/**
* CameraManager.cs
* Using unity camera, we do simple basic, camera cycling through,
* by clicking 'C' key to cycle.
*/
using UnityEngine;
using System.Collections.Generic;

public class CameraManager : MonoBehaviour {
    public List<Camera> augvCameras = new List<Camera>();
    void Start() {
        // onstart we get all the camera's inside agents;
        AUGVAgent[] agents = FindObjectsByType<AUGVAgent>(FindObjectsSortMode.None);
        foreach (var agent in agents) {
            Camera cam = agent.GetComponentInChildren<Camera>();
            if (cam != null) {
                augvCameras.Add(cam);
                cam.enabled = false;
            }
        }
        if (augvCameras.Count > 0) augvCameras[0].enabled = true;
    }
    void Update() {
        // press 'c' to cycle through each camera;
        if (Input.GetKeyDown(KeyCode.C)) {
            for (int i = 0; i < augvCameras.Count; i++) {
                if (augvCameras[i].enabled) {
                    augvCameras[i].enabled = false;
                    augvCameras[(i + 1) % augvCameras.Count].enabled = true;
                    break;
                }
            }
        }
    }
}

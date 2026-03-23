using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Compass_script : MonoBehaviour
{
    
    public Vector3 NorthDirection;
    public Transform Player;
    public RectTransform NorthArrow;

    public float debugHeading; // This shows up as a box in the Inspector

    void Start()
    {
        // Turn on the device location service and compass
        Input.location.Start();
        Input.compass.enabled = true;
    }
    
    // Update is called once per frame
    void Update()
    {
        ChangeNorthDirection();
    }

    public void ChangeNorthDirection()
    {
        // Use the debugHeading while in the Editor, use real Compass on device
        float currentHeading = Application.isEditor ? debugHeading : Input.compass.trueHeading;

        NorthDirection.z = -currentHeading; 
        NorthArrow.localEulerAngles = NorthDirection;
    }
}

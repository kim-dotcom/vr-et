// --------------------------------------------------------------------------------------------------------------------
// Heatmap Visualizer, version 2020-01-06
// This script collects data a virtual environment user can generate.
//
// For the current range of logging possibilities and settings, see the UI in Unity inspector
//     Movement logging: Internal. Attach the script to the desired (FPS)Controller. Logs position and rotation. 
//     Collision logging: External. For user controller bumping into Trigger colliders.
//     Controller logging: Internal. Use of physical interface. Currently keyboard only, GetKeyDown() on Update().
//     Eye tracking: External. SMI format. Deprecated.
//     Eye tracking 2: External. Pupil Labs format (old). Deprecated.
//     Eventlog: External. Free format, accepts any string at logEventInfo().
//     Moving Objects log: Internal. Logs a list of movingObjects<GameObject>.
//       
//  Movement and moving objects are logged countinuously per interval, on a coroutine (max per fps).
//      Set BufferSize for write buffer size (every Nth entry).
//      Set MovementLogInterval for logging periodicity (0 = per fps). Otherwise, delay in seconds.
//
//  Data format: prefedined, per CSV standard and en-Us locale.
//      Changeable per separatorDecimal and separatorItem. Consider format dependency of other apps.
//  Data naming/location: set per dataPrefix and saveLocation. Make sure the dataLocation exists & is writeable.
//  Data structure: see GenerateFileNames() for headers. Self-explanatory.
//
// Basic data structure example (path):
//      userId -- generated per timestamp to identify the user in batch/multiple file processing
//      logId  -- iterator on the current log file
//      timestamp -- time (seconds), Unix epoch format
//      hour | minute | second | milliseconds
//      xpos | ypos | zpos -- location in global Unity coordinates (1 Unity unit = 1 meter)
//      uMousePos | vMousePos | wMousePos -- camera position per mouse. Only u/v relevant (LCD task); only v (VR task)
//      uGazePos | vGazePos | wGazePos    -- VR HMD camera. Only relevant when wearing HMD. Otherwise junk data
//
// Usage: Attach this script to an intended FPSController (dragging & dropping within the Hierarchy browser will do).
//      Other dependent object have to be linked to it, too (e.g. movingObjects<>)
// PathScript methods are public. If other scripts are linked to the GameObject with PathScript, they can log.
//      E.g.: Logger.GetComponent<PathScript>().logEventData(this.name + " triggered " + subObject.name);
// --------------------------------------------------------------------------------------------------------------------

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class HeatmapVisualizer : MonoBehaviour
{
	//link to the file name -- has to be located in the Assets/Resources folder
	public string fileToLoad;
	//the datastructure to load the CSV file to
	private List<Dictionary<string, object>> data;
	[Space(10)]

	//format variables
	public String userGazeX = "EtPositionX";
	public String userGazeY = "EtPositionY";
	public String userGazeZ = "EtPositionZ";

	//optimizations for loading big chunks of data
	//specify a from-to range on the dataset
	public bool cullByRange;
	public int cullFrom;
	public int cullTo;
	//cull by an in-scene collider (must be a box!)
	//TODO: multiple boxes
	public bool cullByContainer;
	public GameObject cullContainer;
	[Space(10)]

	//algorithm precision
	public float maxPointDistance = 0.05f;
	public int minClusterSize = 5;
	[Space(10)]

	//coloring/visualization
	public Color defaultColorLow; //white
	public Color defaultColorHigh; //red
	public Color failedDistanceColor; //gray
	public float pointSize = 0.075f;
	public bool drawTrail;
	public bool drawCloseTrailOnly;
	public Color trailColor; //white
	public float trailMaxDistance = 1f; 

	//load the data
	void Awake()
    {
        data = CSVReader.Read(fileToLoad);
        data = CullData(data);
		VisualizeTrail(data);
        VisualizeHeatmapData(data);
    }

	//draw the ET trail, if enabled
	void VisualizeTrail(List<Dictionary<string, object>> thisData)
	{
		if (drawTrail)
		{
			LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
			lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
			lineRenderer.material.color = trailColor;
			lineRenderer.startWidth = 0.02f;
			lineRenderer.endWidth = 0.02f;
			//generate a Vector3[] array to pass to the renderer
			Vector3[] lineVectors = new Vector3[thisData.Count];
			float trailDistance;
			Vector3 vectorToAdd;
			Vector3 vectorPrevious = new Vector3();
			for (int i = 0; i < thisData.Count; i++)
			{
				vectorToAdd = new Vector3(float.Parse(data[i][userGazeX].ToString()),
										  float.Parse(data[i][userGazeY].ToString()),
										  float.Parse(data[i][userGazeZ].ToString()));				
				//if "spider web" ET trails are not wanted, exclude them...
				//TODO: instantiate into multiple LineRenderers so that there is no long skipping line
				if (drawCloseTrailOnly && i > 0)
                {
					trailDistance = Vector3.Distance(vectorToAdd, vectorPrevious);
					if (trailDistance <= trailMaxDistance)
                    {
						lineVectors[i] = vectorToAdd;
					}
				}
				else
                {
					lineVectors[i] = vectorToAdd;
				}
				vectorPrevious = vectorToAdd;
			}
			//draw it...
			lineRenderer.positionCount = thisData.Count;
			lineRenderer.SetPositions(lineVectors);
		}
	}

	void VisualizeHeatmapData(List<Dictionary<string, object>> thisData)
	{
        //generate all viable ET objects
        List<GameObject> raycasterListAll = new List<GameObject>();
        for (int i = 0; i < thisData.Count; i++)
        {
            float xPos = float.Parse(data[i][userGazeX].ToString());
			float yPos = float.Parse(data[i][userGazeY].ToString());
            float zPos = float.Parse(data[i][userGazeZ].ToString());
            GameObject newSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			//TODO: attach a data component on each such instantiated eye-tracking coordinate
			//      so that spatial data processing can commence without array search loops
            newSphere.GetComponent<Collider>().enabled = false;
			//newSphere.tag = "EyeTracking"; //gotta be created from the editor first 
			newSphere.transform.localScale = new Vector3(pointSize, pointSize, pointSize);
            newSphere.transform.position = new Vector3(xPos, yPos, zPos);
            newSphere.name = "dwell_" + i;
            raycasterListAll.Add(newSphere);
        }

		//iterate on them
		float maxClusterSize = 0f;
		foreach(GameObject thisObject in raycasterListAll)
		{
			float minimumDistance = 1000f;
			float currentClusterSize = 0f;
			//this ET object compared against all other raycasted objects, except itself
			foreach (GameObject anotherObject in raycasterListAll)
			{
				if(thisObject != anotherObject)
				{
					//object distance to other objects
					float distance = Vector3.Distance(thisObject.transform.position,
					anotherObject.transform.position);
					if (distance < minimumDistance) { minimumDistance = distance; }
					if (distance < maxPointDistance) { currentClusterSize++; }
				}
			}

			//determine max absolute cluster size in the ET objects
			//so that cluster visualization density relatives are based on this
			if (currentClusterSize > maxClusterSize) { maxClusterSize = currentClusterSize; }
			if ((minimumDistance > maxPointDistance) || (currentClusterSize < minClusterSize))
			{
				thisObject.GetComponent<Renderer>().material.color = failedDistanceColor;
			}
			else
			{
				thisObject.GetComponent<Renderer>().material.color = Color.Lerp(defaultColorLow, defaultColorHigh,
																				currentClusterSize / maxClusterSize);
			}
		}
    }
    
    //cull the unnecesssary data to reduce rendering and computational load
    //as set up in the inspector
    //TODO: refactor visualizers
    List<Dictionary<string, object>> CullData(List<Dictionary<string, object>> passedList)
    {
		if (!cullByRange && !cullByContainer) { return passedList; } //nothing removed

		List<Dictionary<string, object>> tempList = new List<Dictionary<string, object>>();
		//accept interval subsection of the data, per data entry id
		if (cullByRange)
        {
            if ((data.Count > cullFrom) && (data.Count >= cullTo) && (cullFrom < cullTo))
            {
				for (var i = 0; i < passedList.Count; i++)
                {
                    if (i >= cullFrom && i <= cullTo) { tempList.Add(passedList[i]); }     
                }
				Debug.Log("ET data culled by range from " + data.Count + " to " + tempList.Count);
			}
        }
        //accept what is included inside a gameobject
        if (cullByContainer)
        {
			Bounds bounds = cullContainer.GetComponent<Collider>().bounds;
			float cullFromX = bounds.min.x;
			float cullFromY = bounds.min.y;
			float cullFromZ = bounds.min.z;
			float cullToX = bounds.max.x;
			float cullToY = bounds.max.y;
			float cullToZ = bounds.max.z;
			if (tempList.Count == 0) { tempList = passedList; }
			
			for (var i = 0; i < tempList.Count; i++)
            {
				float xPos = float.Parse(data[i][userGazeX].ToString());
			    float yPos = float.Parse(data[i][userGazeY].ToString());
                float zPos = float.Parse(data[i][userGazeZ].ToString());
				if (!((xPos >= cullFromX && xPos <= cullToX) &&
					  (yPos >= cullFromY && xPos <= cullToY) &&
					  (zPos >= cullFromZ && xPos <= cullToZ)))
				{
					//tempList.Add(passedList[i]);
					tempList.RemoveAt(i);
				}
            }
            passedList = tempList;
			Debug.Log("ET data culled by collider from " + data.Count + " to " + tempList.Count);
        }       
        
        return tempList; //something removed...
    }
}

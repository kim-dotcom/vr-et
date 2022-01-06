// --------------------------------------------------------------------------------------------------------------------
// Path Visualizer script, version 2021-01-06
// This script processes (and debugs/visualizes) currently captured eye-tracking data, to send them to a logger.
//
// The script is derived from SRanipal API (based on SRanipal_EyeFocusSample_v2).
// Functionality: To obtain the data from the Focus function which enables to find the focal point of eyes in VR.
// 
// Use: pair it with logger (PathScript).
// The external logger already user camera position and rotation by default, as well as timestamp.
// The eye-tracking data (gaze coordinates, raycast.hit object) as passed on to the logger through this script. 
//
// Script setup and options:
//   Max distance: maximum eye-tracking distance, in meters
//   Dual raycaster settings: if enabled; which layers to ignore
//   Gaze point visualization
//   Calibration settings (per SRanipal functionality)
//   Logger reference (PathScript logger)
//   Logging settings (gaze position, gazed object name, etc.)
// --------------------------------------------------------------------------------------------------------------------

public class PathVisualizer : MonoBehaviour
{
    //link to the file name -- has to be located in the Assets/Resources folder
    public string fileToLoad;
    //the datastructure to load the CSV file to
    private List<Dictionary<string, object>> data;
    [Space(10)]
    
    //optimizations for loading big chunks of data
    //specify a from-to range on the dataset
    public bool cullByRange;
    public int cullFrom;
    public bool cullTo;
    //cull by an in-scene collider (must be a box!)
    //TODO: multiple boxes
    public bool cullByContainer;
    public GameObject cullContainer;
    [Space(10)]

    //visualization parameters (can be set up from Unity inspector)
    public Color pointColor;
    public float pointSize = 0.15f;
    public Color lineColor;
    //to visualize a continuous line
    private Vector3 previousCubePosition;

    void Awake()
    {
        data = CSVReader.Read(fileToLoad);
        data = CullData(data);
  
        for (var i = 0; i < data.Count; i++)
        {
            //display the point
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cube.name = ”cube” + data[i][”id”];
            cube.GetComponent<Renderer>().material.color = pointColor;
            cube.GetComponent<Renderer>().transform.localScale =
            new Vector3(pointSize, pointSize, pointSize);
            cube.GetComponent<Collider>().enabled = false; //no collisions
            float xPos = float.Parse(data[i][”xpos”].ToString());
            float yPos = float.Parse(data[i][”ypos”].ToString());
            float zPos = float.Parse(data[i][”zpos”].ToString());
            cube.transform.position = new Vector3(xPos, yPos, zPos); //placement
 
            //connect the current point to the previous one
            LineRenderer lineRenderer;
            if ((i > 0) && (i != (data.Count-1)))
            {
                lineRenderer = cube.AddComponent<LineRenderer>();
                lineRenderer.SetVertexCount(2);
                lineRenderer.material.color = lineColor;
                lineRenderer.SetPosition(0, cube.transform.position);
                lineRenderer.SetPosition(1, previousCubePosition);
                lineRenderer.SetWidth(pointSize/2, pointSize/2);
            }
            previousCubePosition = cube.transform.position;
        }
    }
    
    //cull the unnecesssary data to reduce rendering and computational load
    //as set up in the inspector
    //TODO: refactor visualizers
    List<Dictionary<string, object>> CullData(List<Dictionary<string, object>> passedList)
    {
		List<Dictionary<string, object>> tempList = new List<Dictionary<string, object>>;
		//accept interval subsection of the data, per data entry id
        if (cullByRange)
        {
            if ((data.Count > cullFrom) && (data.Count > cullTo) && (cullFrom < cullTo))
            {
                for (var i = 0; i < passedList.Count; i++)
                {
                    if (i >= cullFrom && i <= cullTo) { tempList.Add(passedList[i]); }     
                }
                passedList = tempList;
                tempList = new List<Dictionary<string, object>>;
            }
        }
        //accept what is included inside a gameobject
        if (cullByContainer)
        {
			Bounds bounds = cullContainer.GetComponent<Collider>().bounds;
			cullFromX = bounds.min.x;
			cullFromY = bounds.min.y;
			cullFromZ = bounds.min.z;
			cullToX = bounds.max.x;
			cullToY = bounds.max.y;
			cullToZ = bounds.max.z;
			
			for (var i = 0; i < passedList.Count; i++)
            {
				float xPos = float.Parse(data[i][”xpos”].ToString());
			    float yPos = float.Parse(data[i][”ypos”].ToString());
                float zPos = float.Parse(data[i][”zpos”].ToString());
                if ((xPos >= cullFromX && xPos =< cullToX) &&
                    (yPos >= cullFromY && xPos =< cullToY) &&
                    (zPos >= cullFromZ && xPos =< cullToZ))
                {
                   tempList.Add(passedList[i]);
                }     
            }
            passedList = tempList;
        }
        
        if (!cullByRange && !cullByContainer) { return passedList; } //nothing removed
        else { return tempList; } //something removed...
    }
}

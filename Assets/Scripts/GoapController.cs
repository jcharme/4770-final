using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;
using System.Linq;

[System.Serializable]
public struct RoomMapping
{
    public string roomID;
    public List<GameObject> floorObjects;
}

public class GoapController : KaijuController
{
    [Header("Room Detection")]
    public LayerMask floorLayer; // Assign this to your "Floor" layer
    public List<RoomMapping> roomMappings = new List<RoomMapping>();
    private Dictionary<Collider, string> colliderToRoomID = new Dictionary<Collider, string>();
    
    
    protected override void OnEnabled()
    {
        InitializeActions();
        InitializeRoomDetection();
        StartCoroutine(RoomDetectionLoop());
        StartWandering();
    }
    
    private void OnValidate()
    {
        if (roomIDs == null || roomIDs.Length == 0) return;

        // 1. If the list count is totally wrong, just rebuild it once
        if (roomMappings.Count != roomIDs.Length)
        {
            // We don't Clear() here to avoid wiping data if we just added 1 room
            while (roomMappings.Count < roomIDs.Length)
                roomMappings.Add(new RoomMapping { roomID = "", floorObjects = new List<GameObject>() });
            while (roomMappings.Count > roomIDs.Length)
                roomMappings.RemoveAt(roomMappings.Count - 1);
        }

        // 2. Match IDs and Force Unique Lists
        for (int i = 0; i < roomIDs.Length; i++)
        {
            // Use a temporary struct (because you can't modify list structs directly)
            RoomMapping mapping = roomMappings[i];

            // Fix the ID to match our hardcoded master list
            mapping.roomID = roomIDs[i];

            // CRITICAL FIX: Ensure this room has its OWN list instance.
            // This prevents the "changing one changes all" bug.
            if (mapping.floorObjects == null)
            {
                mapping.floorObjects = new List<GameObject>();
            }

            // Put the fixed struct back into the list
            roomMappings[i] = mapping;
        }
    }
    
    protected string[] roomIDs =
    {
        "Entrance", "Hall-East", "Dining", "Kitchen", "Corridor", "Library", "Hall-West", 
        "Bathroom-North", "Bedroom-Large", "Bathroom-South", "Bedroom-Small-North", "Bedroom-Small-South"
    };
    
    private void InitializeRoomDetection()
    {
        colliderToRoomID.Clear();
        foreach (var mapping in roomMappings)
        {
            // Safety check: ensure the list exists
            if (mapping.floorObjects == null) continue;

            foreach (GameObject floor in mapping.floorObjects)
            {
                if (floor != null)
                {
                    Collider col = floor.GetComponent<Collider>();
                    if (col != null)
                    {
                        colliderToRoomID[col] = mapping.roomID;
                    }
                }
            }
        }
    }

    private IEnumerator RoomDetectionLoop()
    {
        while (true)
        {
            // Raycast down from slightly above the feet
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.0f, floorLayer))
            {
                // Look up what room this collider belongs to
                if (colliderToRoomID.TryGetValue(hit.collider, out string detectedRoom))
                {
                    // Only update the worldState if we actually crossed a threshold
                    if (!worldState["CurrentRoom"].Equals(detectedRoom))
                    {
                        worldState["CurrentRoom"] = detectedRoom;
                        Debug.Log($"<color=green>GOAP:</color> Room detected: {detectedRoom}");
                    }
                }
            }
            
            // LOGGING - TEMP
            // Start the string with a header
            string report = "<color=yellow>--- GHOST STATE ---</color>\n";

            // Loop through and add every key-value pair
            foreach (var kvp in worldState)
            {
                report += $"<b>{kvp.Key}:</b> {kvp.Value}\n";
            }

            Debug.Log(report);
            
            yield return new WaitForSeconds(0.2f); // 5 times a second is plenty
        }
    }
    
    
    protected Coroutine wanderCoroutine;
    private KaijuEverythingVisionSensor visionSensor;
    public Transform targetVictim; 
    private bool isChasing = false;

    public Dictionary<string, object> worldState = new Dictionary<string, object>()
    {
        { "CurrentRoom", "Entrance" }, // Predicate approach
        { "IsHidden", false },
        { "ResidentScared", false },
        { "ResidentInSight", false },  
        { "NearVictim", false },
    };
    
    private List<GoapAction> availableActions = new List<GoapAction>();
    private Queue<GoapAction> currentPlan = new Queue<GoapAction>();


    private bool initialized = false;
    private void InitializeActions()
    {
        if (initialized) return;
        initialized = true;

        visionSensor = Agent.GetSensor<KaijuEverythingVisionSensor>();
        
        // Setup movement actions
        foreach (string roomId in roomIDs)
        {
            availableActions.Add(new GoapAction(
                name: "MoveTo" + roomId,
                cost: 1f,
                preReqs: new Dictionary<string, object>(), 
                effects: new Dictionary<string, object> { { "CurrentRoom", roomId } }, 
                actionLogic: (context) => Action_MoveTo(context, roomId)
            ));
        }
        
        // Setup specific behavior actions
        availableActions.Add(new GoapAction("HideInShadows", 1f, new Dictionary<string, object> {{"CurrentRoom", "Kitchen"}}, new Dictionary<string, object> {{"IsHidden", true}}, Action_Hide));
        availableActions.Add(new GoapAction("ChaseVictim", 1f, new Dictionary<string, object>(), new Dictionary<string, object> {{"NearVictim", true}}, Action_Chase));
        availableActions.Add(new GoapAction("ScareVictim", 1f, new Dictionary<string, object> { { "NearVictim", true } }, new Dictionary<string, object> { { "ResidentScared", true } }, Action_Scare));
    }

    public void RequestPlan(Dictionary<string, object> goalState)
    {
        Debug.Log("<color=cyan>GOAP:</color> Requesting plan from GoapEngine...");
        var plan = GoapEngine.Plan(worldState, goalState, availableActions);
        
        if (plan != null)
        {
            currentPlan = plan;
            isChasing = true;
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            Agent.Stop();
            StopAllCoroutines();
            
            StartCoroutine(RoomDetectionLoop());
            StartCoroutine(ExecutePlan());
        }
        else 
        {
            Debug.LogError("<color=red>GOAP:</color> Plan Failed!");
        }
    }

    private IEnumerator ExecutePlan()
    {
        while (currentPlan.Count > 0)
        {
            GoapAction currentAction = currentPlan.Dequeue();
            Debug.Log($"<color=cyan>GOAP:</color> Executing {currentAction.name}");
            yield return StartCoroutine(currentAction.logicCoroutine(this));
            
            // Apply effects to world state after completion
            foreach(var effect in currentAction.effects) worldState[effect.Key] = effect.Value;
        }
        
        isChasing = false;
        worldState["NearVictim"] = false; 
        worldState["ResidentScared"] = false; 
        StartWandering();
    }

    // --- Action Coroutines ---

    private IEnumerator Action_MoveTo(GoapController context, string roomName)
    {
        GameObject target = GameObject.Find("POI_" + roomName);
        if (target != null)
        {
            context.Agent.PathFollow(target.transform.position, clear: true);
            float timeout = 15f;
            while (Vector3.Distance(context.Agent.transform.position, target.transform.position) > 1.5f && timeout > 0) 
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            context.Agent.Stop();
        }
    }

    private IEnumerator Action_Hide(GoapController context) { yield return new WaitForSeconds(1.5f); }
    
    private IEnumerator Action_Chase(GoapController context)
    {
        if (context.targetVictim == null) yield break;
        context.Agent.PathFollow(context.targetVictim, clear: true);
        float timeout = 20f;
        while (context.targetVictim != null && Vector3.Distance(context.transform.position, context.targetVictim.position) > 2.0f && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        context.Agent.Stop();
    }

    private IEnumerator Action_Scare(GoapController context)
    {
        if (context.targetVictim == null) yield break;
        ResidentController victim = context.targetVictim.GetComponent<ResidentController>();
        if (victim != null) victim.TriggerScared(context.transform);
        yield return new WaitForSeconds(2.0f);
    }

    // --- Wandering & Sensing ---

    protected void StartWandering()
    {
        string randomRoom = roomIDs[Random.Range(0, roomIDs.Length)];
        GameObject target = GameObject.Find("POI_" + randomRoom);
        if (target != null) Agent.PathFollow(target.transform.position, clear: true);
        else Agent.Wander(clear: true);
        Agent.ObstacleAvoidance(clear: false);
    }

    protected override void OnMovementStopped(KaijuMovement movement)
    {
        if (!isChasing)
        {
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            wanderCoroutine = StartCoroutine(WaitThenWander());
        }
    }

    protected IEnumerator WaitThenWander() { yield return new WaitForSeconds(Random.Range(5f, 10f)); StartWandering(); }
    protected override void OnSense(KaijuSensor sensor) { if (sensor == visionSensor) worldState["ResidentInSight"] = visionSensor.HasObserved; }
}
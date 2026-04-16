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
    public LayerMask floorLayer;
    public List<RoomMapping> roomMappings = new List<RoomMapping>();
    private Dictionary<Collider, string> colliderToRoomID = new Dictionary<Collider, string>();
    
    /// <summary>
    /// List of rooms in the level. Each ID corresponds exactly to a POI_*, of which there is one per room.
    /// </summary>
    [Header("Navigation Data")]
    protected readonly string[] roomIDs =
    {
        "Entrance", "Hall-East", "Dining", "Kitchen", "Corridor", "Library", "Hall-West", 
        "Bathroom-North", "Bedroom-Large", "Bathroom-South", "Bedroom-Small-North", "Bedroom-Small-South"
    };
    
    /// <summary>
    /// A dictionary which maps rooms to their adjacent rooms. Can use to see if rooms are connected.
    /// </summary>
    protected readonly Dictionary<string, string[]> roomAdjacency = new()
    {
        { "Entrance", new[] { "Hall-East" } },
        { "Hall-East", new[] { "Dining", "Entrance", "Bedroom-Large" } },
        { "Dining", new[] { "Kitchen", "Hall-East" } },
        { "Kitchen", new[] { "Dining", "Corridor" } },
        { "Corridor", new[] { "Kitchen", "Library", "Bedroom-Small-North", "Bedroom-Small-South" } },
        { "Library", new[] { "Corridor", "Hall-West" } },
        { "Hall-West", new[] { "Library", "Bathroom-North" } },
        { "Bathroom-North", new[] { "Hall-West" } },
        { "Bedroom-Large", new[] { "Hall-East", "Bathroom-South" } },
        { "Bathroom-South", new[] { "Bedroom-Large" } },
        { "Bedroom-Small-North", new[] { "Corridor" } },
        { "Bedroom-Small-South", new[] { "Corridor" } }
    };

    /// <summary>
    /// List of state objects.
    /// This class only handles room movement, add more in derived classes as needed.
    /// </summary>
    [Header("AI State")]
    protected Dictionary<string, object> worldState = new ()
    {
        { "CurrentRoom", "Entrance" },
    };

    protected Queue<GoapAction> currentPlan = new Queue<GoapAction>();
    protected Coroutine wanderCoroutine;
    
    /// <summary>
    /// Called when the script instance is being loaded.
    /// Initializes actions and room detection.
    /// </summary>
    protected override void OnEnabled()
    {
        InitializeActions();
        InitializeRoomDetection();
    }

    /// <summary>
    /// List of actions available to the Agent.
    /// </summary>
    protected readonly List<GoapAction> availableActions = new();

    /// <summary>
    /// Initializes all GOAP actions.
    /// GoapController only handles movement. Override to add more actions.
    /// </summary>
    protected virtual void InitializeActions()
    {
        if (availableActions.Any()) return;
        
        // Creates an action 'Door_X_to_Y' (and 'Door_Y_to_X') for every pair of adjacent rooms.
        foreach (var room in roomAdjacency)
        {
            string startRoom = room.Key;
            foreach (string endRoom in room.Value)
            {
                availableActions.Add(new GoapAction(
                    name: $"Door_{startRoom}_to_{endRoom}",
                    cost: 1f,
                    preReqs: new Dictionary<string, object> { { "CurrentRoom", startRoom } }, 
                    effects: new Dictionary<string, object> { { "CurrentRoom", endRoom } }, 
                    actionLogic: (context) => Action_MoveTo(context, endRoom)
                ));
            }
        }
    }

    /// <summary>
    /// Initializes the room detection system by mapping colliders to room IDs.
    /// </summary>
    private void InitializeRoomDetection()
    {
        colliderToRoomID.Clear();
        foreach (var mapping in roomMappings)
        {
            if (mapping.floorObjects == null) continue;
            foreach (GameObject floor in mapping.floorObjects)
            {
                if (floor != null)
                {
                    Collider col = floor.GetComponent<Collider>();
                    if (col != null) colliderToRoomID[col] = mapping.roomID;
                }
            }
        }

        StartCoroutine(RoomDetectionLoop());
    }

    /// <summary>
    /// Coroutine that periodically checks the agent's current room based on floor detection.
    /// </summary>
    /// <returns>An IEnumerator for the coroutine.</returns>
    private IEnumerator RoomDetectionLoop()
    {
        while (true)
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.0f, floorLayer))
            {
                if (colliderToRoomID.TryGetValue(hit.collider, out string detectedRoom))
                {
                    if (!worldState["CurrentRoom"].Equals(detectedRoom))
                    {
                        worldState["CurrentRoom"] = detectedRoom;
                        Debug.Log($"<color=green>GOAP State Update:</color> Entered {detectedRoom}");
                    }
                }
            }
            yield return new WaitForSeconds(0.2f);
        }
    }
    
    /// <summary>
    /// Unity callback for validating inspector data.
    /// Ensures roomMappings matches the size and content of roomIDs.
    /// </summary>
    protected override void OnValidate()
    {
        if (roomIDs == null || roomIDs.Length == 0) return;
        if (roomMappings.Count != roomIDs.Length)
        {
            while (roomMappings.Count < roomIDs.Length) roomMappings.Add(new RoomMapping { roomID = "", floorObjects = new List<GameObject>() });
            while (roomMappings.Count > roomIDs.Length) roomMappings.RemoveAt(roomMappings.Count - 1);
        }
        for (int i = 0; i < roomIDs.Length; i++)
        {
            RoomMapping mapping = roomMappings[i];
            mapping.roomID = roomIDs[i];
            if (mapping.floorObjects == null) mapping.floorObjects = new List<GameObject>();
            roomMappings[i] = mapping;
        }
    }
    
    /// <summary>
    /// Starts the GOAP planning process for a specific set of goal conditions.
    /// If a plan is found, it stops current activities and begins execution.
    /// </summary>
    /// <param name="goalState">A dictionary of desired state keys and their target values.</param>
    public void RequestPlan(Dictionary<string, object> goalState)
    {
        var plan = GoapEngine.Plan(worldState, goalState, availableActions);
        if (plan != null && plan.Count > 0)
        {
            currentPlan = plan;
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            Agent.Stop();
            StopAllCoroutines();
            StartCoroutine(RoomDetectionLoop());
            StartCoroutine(ExecutePlan());
        }
        else 
        {
            Debug.LogError("GOAP Failed! No path found to goal.");
        }
    }

    /// <summary>
    /// Iterates through the current plan queue, executing each action coroutine 
    /// sequentially and applying their effects to the world state upon completion.
    /// </summary>
    protected IEnumerator ExecutePlan()
    {
        while (currentPlan.Count > 0)
        {
            GoapAction currentAction = currentPlan.Dequeue();
            yield return StartCoroutine(currentAction.logicCoroutine(this));
            foreach(var effect in currentAction.effects) worldState[effect.Key] = effect.Value;
        }
        OnPlanComplete();
    }

    /// <summary>
    /// Wrapper to request a plan to the specified room.
    /// </summary>
    /// <param name="roomID">The ID of the room we want to move to.</param>
    public void MoveToRoom(string roomID)
    {
        if (!roomIDs.Contains(roomID)) return;
        RequestPlan(new Dictionary<string, object> { { "CurrentRoom", roomID } });
    }

    /// <summary>
    /// Called when a GOAP plan is completed or aborted.
    /// Override in derived classes to handle post-plan logic.
    /// </summary>
    protected virtual void OnPlanComplete() { }
    
    /// <summary>
    /// Physically moves the agent to a target room.
    /// Uses a "Reality Lock" pattern: it waits until the room detection raycast 
    /// confirms the agent has physically entered the target room before finishing.
    /// </summary>
    /// <param name="context">The controller executing the action.</param>
    /// <param name="targetRoom">The ID of the room to move to.</param>
    protected IEnumerator Action_MoveTo(GoapController context, string targetRoom)
    {
        GameObject poi = GameObject.Find("POI_" + targetRoom);
        if (poi == null) yield break;

        context.Agent.PathFollow(poi.transform.position, clear: true);

        float timeout = 15f;
        while (timeout > 0)
        {
            if (context.worldState["CurrentRoom"].Equals(targetRoom)) yield break;
            timeout -= Time.deltaTime;
            yield return null;
        }
        Debug.LogError($"Target failed to reach {targetRoom}");
    }


    /// <summary>
    /// Called when a sensor updates its observations.
    /// Override in derived classes to handle sensor data.
    /// </summary>
    /// <param name="sensor">The sensor that updated.</param>
    protected override void OnSense(KaijuSensor sensor) { }
    
    
}

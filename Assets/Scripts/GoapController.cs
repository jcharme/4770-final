using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;
using System.Linq;

/// <summary>
/// Maps a room ID to a set of floor objects for environment detection.
/// </summary>
[System.Serializable]
public struct RoomMapping
{
    public string roomID;
    public List<GameObject> floorObjects;
}

/// <summary>
/// Base controller for agents using Goal-Oriented Action Planning (GOAP).
/// Handles environment-aware navigation and action execution.
/// </summary>
public class GoapController : KaijuController
{
    [Header("Room Detection")]
    public LayerMask floorLayer;
    public List<RoomMapping> roomMappings = new List<RoomMapping>();

    [Header("Navigation Data")]
    /// <summary>
    /// Valid room IDs in the environment.
    /// </summary>
    protected readonly string[] roomIDs =
    {
        "Entrance", "Hall-East", "Dining", "Kitchen", "Corridor", "Library", "Hall-West",
        "Bathroom-North", "Bedroom-Large", "Bathroom-South", "Bedroom-Small-North", "Bedroom-Small-South"
    };

    /// <summary>
    /// Defines which rooms are physically adjacent to each other.
    /// </summary>
    protected readonly Dictionary<string, string[]> roomAdjacency = new Dictionary<string, string[]>()
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

    [Header("AI State")]
    protected Dictionary<string, object> worldState = new Dictionary<string, object>()
    {
        { "CurrentRoom", "Entrance" },
    };

    protected Queue<GoapAction> currentPlan = new Queue<GoapAction>();
    protected Coroutine wanderCoroutine;
    protected readonly List<GoapAction> availableActions = new List<GoapAction>();

    private Dictionary<Collider, string> colliderToRoomID = new Dictionary<Collider, string>();

    /// <summary>
    /// Initializes the controller, actions, and room detection.
    /// </summary>
    protected override void OnEnabled()
    {
        InitializeActions();
        InitializeRoomDetection();
    }

    /// <summary>
    /// Populates the availableActions list. Override to add specialized agent actions.
    /// </summary>
    protected virtual void InitializeActions()
    {
        if (availableActions.Any())
        {
            return;
        }

        // Generate movement actions based on room adjacency
        foreach (var room in roomAdjacency)
        {
            string startRoom = room.Key;
            foreach (string endRoom in room.Value)
            {
                availableActions.Add(new GoapAction(
                    name: $"Move_{startRoom}_to_{endRoom}",
                    cost: 1f,
                    preReqs: new Dictionary<string, object> { { "CurrentRoom", startRoom } },
                    effects: new Dictionary<string, object>
                    {
                        { "CurrentRoom", endRoom },
                        { "In" + endRoom, true }
                    },
                    actionLogic: (context) => Action_MoveTo(context, endRoom)
                ));
            }
        }
    }

    /// <summary>
    /// Maps floor colliders to room IDs for spatial awareness.
    /// </summary>
    private void InitializeRoomDetection()
    {
        colliderToRoomID.Clear();
        foreach (var mapping in roomMappings)
        {
            if (mapping.floorObjects == null)
            {
                continue;
            }

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

        StartCoroutine(RoomDetectionLoop());
    }

    /// <summary>
    /// Periodically updates the current room state based on the agent's position.
    /// </summary>
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
                    }
                }
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    /// <summary>
    /// Validates inspector data to ensure room mappings stay synchronized with room IDs.
    /// </summary>
    protected override void OnValidate()
    {
        if (roomIDs == null || roomIDs.Length == 0)
        {
            return;
        }

        if (roomMappings.Count != roomIDs.Length)
        {
            while (roomMappings.Count < roomIDs.Length)
            {
                roomMappings.Add(new RoomMapping { roomID = "", floorObjects = new List<GameObject>() });
            }
            while (roomMappings.Count > roomIDs.Length)
            {
                roomMappings.RemoveAt(roomMappings.Count - 1);
            }
        }

        for (int i = 0; i < roomIDs.Length; i++)
        {
            RoomMapping mapping = roomMappings[i];
            mapping.roomID = roomIDs[i];
            if (mapping.floorObjects == null)
            {
                mapping.floorObjects = new List<GameObject>();
            }
            roomMappings[i] = mapping;
        }
    }

    /// <summary>
    /// Requests a new GOAP plan to reach the specified goal state.
    /// </summary>
    /// <param name="goalState">The desired state conditions.</param>
    public virtual void RequestPlan(Dictionary<string, object> goalState)
    {
        string startRoom = worldState.ContainsKey("CurrentRoom") ? worldState["CurrentRoom"].ToString() : "Unknown";
        Debug.Log($"<color=cyan>GOAP:</color> Calculating plan from <b>{startRoom}</b>...");

        var plan = GoapEngine.Plan(worldState, goalState, availableActions);
        if (plan != null && plan.Count > 0)
        {
            currentPlan = plan;

            // Pretty-print the sequence of actions for visualization
            string planLog = "<color=green>Plan Found:</color> " + string.Join(" -> ", plan.Select(a => a.name));
            Debug.Log(planLog);

            // Stop current activities to prioritize the new plan
            if (wanderCoroutine != null)
            {
                StopCoroutine(wanderCoroutine);
            }

            Agent.Stop();
            StopAllCoroutines();

            // Restart core maintenance loops
            StartCoroutine(RoomDetectionLoop());
            StartCoroutine(ExecutePlan());
        }
        else
        {
            Debug.LogWarning($"<color=orange>GOAP:</color> Failed to find a valid plan for the requested goal.");
        }
    }

    /// <summary>
    /// Executes the current plan step-by-step.
    /// </summary>
    protected IEnumerator ExecutePlan()
    {
        while (currentPlan.Count > 0)
        {
            GoapAction currentAction = currentPlan.Dequeue();
            yield return StartCoroutine(currentAction.logicCoroutine(this));

            // Apply action effects to the world state upon successful completion
            foreach (var effect in currentAction.effects)
            {
                worldState[effect.Key] = effect.Value;
            }
        }

        OnPlanComplete();
    }

    /// <summary>
    /// High-level command to move the agent to a specific room.
    /// </summary>
    /// <param name="roomID">The target room ID.</param>
    public void MoveToRoom(string roomID)
    {
        if (!roomIDs.Contains(roomID))
        {
            Debug.LogError($"<color=red>GOAP:</color> {roomID} is not a valid room name.");
            return;
        }
        RequestPlan(new Dictionary<string, object> { { "CurrentRoom", roomID } });
    }

    /// <summary>
    /// Executed when a plan is finished. Override for post-plan behaviors.
    /// </summary>
    protected virtual void OnPlanComplete()
    {
    }

    /// <summary>
    /// Movement logic for transitioning between rooms using Point of Interest (POI) markers.
    /// </summary>
    protected IEnumerator Action_MoveTo(GoapController context, string targetRoom)
    {
        GameObject poi = GameObject.Find("POI_" + targetRoom);
        if (poi == null)
        {
            Debug.LogError($"<color=red>GOAP:</color> POI for {targetRoom} not found in scene.");
            yield break;
        }

        context.Agent.PathFollow(poi.transform.position, clear: true);

        // Wait until the room detection confirms we have entered the target room or timeout
        float timeout = 15f;
        while (timeout > 0)
        {
            if (context.worldState["CurrentRoom"].Equals(targetRoom))
            {
                yield break;
            }
            timeout -= Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning($"<color=orange>GOAP:</color> Movement to {targetRoom} timed out.");
    }

    /// <summary>
    /// Handles sensor updates. Override to integrate environmental perception into the world state.
    /// </summary>
    protected override void OnSense(KaijuSensor sensor)
    {
    }
}

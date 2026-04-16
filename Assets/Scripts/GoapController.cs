using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;

public class GoapController : KaijuController
{
    protected string[] roomIDs =
    {
        "Entrance", "Hall-East", "Dining", "Kitchen", "Corridor", "Library", "Hall-West", 
        "Bathroom-North", "Bedroom-Large", "Bathroom-South", "Bedroom-Small-North", "Bedroom-Small-South"
    };
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

    protected override void OnEnabled()
    {
        InitializeActions();
        StartWandering();
    }

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
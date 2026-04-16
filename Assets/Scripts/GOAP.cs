using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;

public class GOAP : BaseWanderController
{
    private KaijuEverythingVisionSensor visionSensor;
    
    public Transform targetVictim; 
    private bool isChasing = false;
    
    // World State
    public Dictionary<string, bool> worldState = new Dictionary<string, bool>()
    {
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
        if (visionSensor != null) visionSensor.automatic = true;
        
        foreach (string roomId in roomIDs)
        {
            string stateKey = "In" + roomId;
            if (!worldState.ContainsKey(stateKey)) worldState.Add(stateKey, false);

            string actionName = "MoveTo" + roomId;
            var effects = new Dictionary<string, bool>();
            foreach (string otherId in roomIDs) effects.Add("In" + otherId, (otherId == roomId));

            availableActions.Add(new GoapAction(
                name: actionName,
                cost: 1f,
                preReqs: new Dictionary<string, bool>(), 
                effects: effects,
                actionLogic: (context) => Action_MoveTo(context, roomId)
            ));
        }
        
        availableActions.Add(new GoapAction("HideInShadows", 1f, new Dictionary<string, bool>() {{"InKitchen", true}}, new Dictionary<string, bool>() {{"IsHidden", true}}, Action_Hide));
        availableActions.Add(new GoapAction("ChaseVictim", 1f, new Dictionary<string, bool>(), new Dictionary<string, bool>() {{"NearVictim", true}}, Action_Chase));
        availableActions.Add(new GoapAction("ScareVictim", 1f, new Dictionary<string, bool>() { { "NearVictim", true } }, new Dictionary<string, bool>() { { "ResidentScared", true } }, Action_Scare));
    }
    
    protected override void OnMovementStopped(KaijuMovement movement)
    {
        if (!isChasing)
        {
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            wanderCoroutine = StartCoroutine(WaitThenWander());
        }
    }

    protected override void StartWandering()
    {
        isChasing = false;
        base.StartWandering();
    }

    protected override void OnSense(KaijuSensor sensor)
    {
        if (sensor == visionSensor) worldState["ResidentInSight"] = visionSensor.HasObserved;
    }
    
    public void RequestPlan(Dictionary<string, bool> goalState)
    {
        currentPlan.Clear();
        List<GoapNode> openList = new List<GoapNode>();
        List<GoapNode> closedList = new List<GoapNode>();
        
        GoapNode startNode = new GoapNode(null, 0, worldState, null);
        startNode.heuristicCost = CalculateHeuristic(worldState, goalState);
        openList.Add(startNode);
        
        GoapNode cheapestNode = null;
        while (openList.Count > 0)
        {
            openList.Sort((a, b) => a.TotalCost.CompareTo(b.TotalCost));
            GoapNode currentNode = openList[0];
            openList.Remove(currentNode);
            closedList.Add(currentNode);
            
            if (IsGoalMet(currentNode.state, goalState)) { cheapestNode = currentNode; break; }
            
            foreach (GoapAction action in availableActions)
            {
                if (ArePreconditionsMet(action.preconditions, currentNode.state))
                {
                    Dictionary<string, bool> newState = new Dictionary<string, bool>(currentNode.state);
                    foreach (var effect in action.effects) newState[effect.Key] = effect.Value;
                    
                    float gCost = currentNode.runningCost + action.cost;
                    GoapNode neighbourNode = new GoapNode(currentNode, gCost, newState, action);
                    neighbourNode.heuristicCost = CalculateHeuristic(newState, goalState);
                    
                    if (closedList.Exists(n => StatesMatch(n.state, newState) && n.runningCost <= gCost)) continue;
                    GoapNode existingOpen = openList.FirstOrDefault(n => StatesMatch(n.state, newState));
                    if (existingOpen != null) { if (existingOpen.runningCost > gCost) { openList.Remove(existingOpen); openList.Add(neighbourNode); } }
                    else openList.Add(neighbourNode);
                }
            }
        }
        
        if (cheapestNode != null)
        {
            List<GoapAction> actionPath = new List<GoapAction>();
            GoapNode node = cheapestNode;
            while (node.action != null) { actionPath.Add(node.action); node = node.parent; }
            actionPath.Reverse();
            foreach (var action in actionPath) currentPlan.Enqueue(action);

            isChasing = true;
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            Agent.Stop();
            StopAllCoroutines();
            StartCoroutine(ExecutePlan());
        }
    }
    
    private bool ArePreconditionsMet(Dictionary<string, bool> pre, Dictionary<string, bool> st) { foreach (var p in pre) if (!st.ContainsKey(p.Key) || st[p.Key] != p.Value) return false; return true; }
    private bool IsGoalMet(Dictionary<string, bool> st, Dictionary<string, bool> goal) { foreach (var g in goal) if (!st.ContainsKey(g.Key) || st[g.Key] != g.Value) return false; return true; }
    private float CalculateHeuristic(Dictionary<string, bool> st, Dictionary<string, bool> goal) { float m = 0; foreach (var g in goal) if (!st.ContainsKey(g.Key) || st[g.Key] != g.Value) m++; return m; }
    private bool StatesMatch(Dictionary<string, bool> a, Dictionary<string, bool> b) { if (a.Count != b.Count) return false; foreach (var s in a) if (!b.ContainsKey(s.Key) || b[s.Key] != s.Value) return false; return true; }
    
    private IEnumerator ExecutePlan()
    {
        while (currentPlan.Count > 0)
        {
            GoapAction currentAction = currentPlan.Dequeue();
            yield return StartCoroutine(currentAction.logicCoroutine(this));
            foreach(var effect in currentAction.effects) worldState[effect.Key] = effect.Value;
        }
        worldState["NearVictim"] = false; 
        StartWandering();
    }

    private IEnumerator Action_MoveTo(GOAP context, string roomName)
    {
        GameObject target = GameObject.Find("POI_" + roomName);
        if (target != null)
        {
            context.Agent.PathFollow(target.transform.position, clear: true);
            context.Agent.ObstacleAvoidance(clear: false);
        
            float timeout = 15f; // Max 15 seconds to reach a room
            while (Vector3.Distance(context.Agent.transform.position, target.transform.position) > 1.5f && timeout > 0) 
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
        
            context.Agent.Stop();
        }
    }

    private IEnumerator Action_Hide(GOAP context) { yield return new WaitForSeconds(1.5f); }
    
    private IEnumerator Action_Chase(GOAP context)
    {
        if (context.targetVictim == null) yield break;
        context.Agent.PathFollow(context.targetVictim, clear: true);
        context.Agent.ObstacleAvoidance(clear: false);
        float timeout = 20f;
        while (context.targetVictim != null && 
               Vector3.Distance(context.transform.position, context.targetVictim.position) > 2.0f && 
               timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        context.Agent.Stop();
    }

    private IEnumerator Action_Scare(GOAP context)
    {
        if (context.targetVictim == null) yield break;
        ResidentController victim = context.targetVictim.GetComponent<ResidentController>();
        if (victim != null) victim.TriggerScared(context.transform);
        yield return new WaitForSeconds(2.0f);
    }
}

public class GoapAction
{
    public string name;
    public float cost;
    public Dictionary<string, bool> preconditions;
    public Dictionary<string, bool> effects;
    public System.Func<GOAP, IEnumerator> logicCoroutine;

    public GoapAction(string name, float cost, Dictionary<string, bool> preReqs, Dictionary<string, bool> effects, System.Func<GOAP, IEnumerator> actionLogic)
    {
        this.name = name; this.cost = cost; this.preconditions = preReqs; this.effects = effects; this.logicCoroutine = actionLogic;
    }
}

public class GoapNode
{
    public GoapNode parent; 
    public float runningCost; 
    public float heuristicCost; 
    public Dictionary<string, bool> state; 
    public GoapAction action; 
    public float TotalCost => runningCost + heuristicCost;
    public GoapNode(GoapNode parent, float runningCost, Dictionary<string, bool> state, GoapAction action)
    {
        this.parent = parent; this.runningCost = runningCost; this.state = new Dictionary<string, bool>(state); this.action = action;
    }
}

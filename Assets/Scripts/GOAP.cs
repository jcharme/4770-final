using UnityEngine;
// using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;

public class GOAP : KaijuController
{
    private bool isMoving = false;

    private KaijuEverythingVisionSensor visionSensor;
    
    // World State
    public Dictionary<string, bool> worldState = new Dictionary<string, bool>()
    {
        { "InKitchen", false },
        { "InLibrary", false },
        { "InHallway", false },
        { "IsHidden", false },
        { "PlayerScared", false },
        { "PlayerInSight", false },  
    };
    
    // Available Actions
    private List<GoapAction> availableActions = new List<GoapAction>();
    
    // Current Plan
    private Queue<GoapAction> currentPlan = new Queue<GoapAction>();
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    protected override void OnEnabled()
    {
        InitializeActions();
    }

    private void Start()
    {
        InitializeActions();
    }

    private void OnEnable()
    {
        InitializeActions();
    }

    private bool initialized = false;
    private void InitializeActions()
    {
        if (initialized) return;
        initialized = true;

        visionSensor = Agent.GetSensor<KaijuEverythingVisionSensor>();
        if (visionSensor != null)
        {
            visionSensor.automatic = true;
        }
        
        // Define Actions
        availableActions.Add(new GoapAction("MoveToKitchen",
            cost: 1f,
            preReqs: new Dictionary<string, bool>(), 
            effects:  new Dictionary<string, bool>() {{"InKitchen", true}, {"InLibrary", false}, {"InHallway", false}},
            actionLogic: (context) => Action_MoveTo(context, "Kitchen")
            ));

        availableActions.Add(new GoapAction("MoveToLibrary",
            cost: 1f,
            preReqs: new Dictionary<string, bool>(), 
            effects:  new Dictionary<string, bool>() {{"InLibrary", true}, {"InKitchen", false}, {"InHallway", false}},
            actionLogic: (context) => Action_MoveTo(context, "Library")
            ));

        availableActions.Add(new GoapAction("MoveToHallway",
            cost: 1f,
            preReqs: new Dictionary<string, bool>(), 
            effects:  new Dictionary<string, bool>() {{"InHallway", true}, {"InKitchen", false}, {"InLibrary", false}},
            actionLogic: (context) => Action_MoveTo(context, "Hallway")
            ));
        
        availableActions.Add(new GoapAction("HideInShadows",
            cost: 1f,
            preReqs: new Dictionary<string, bool>() {{"InKitchen", true}}, //must be in kitchen
            effects:  new Dictionary<string, bool>() {{"IsHidden", true}},
            actionLogic: Action_Hide
        ));
        
        availableActions.Add(new GoapAction("JumpScare", 
            cost: 1f,
            preReqs: new Dictionary<string, bool>() { { "InKitchen", true }, { "IsHidden", true } }, // Must be hidden in the kitchen
            effects: new Dictionary<string, bool>() { { "PlayerScared", true } },
            actionLogic: Action_JumpScare
        ));
    }
    
    // OnSense callback to react to the environment automatically
    protected override void OnSense(KaijuSensor sensor)
    {
        // If our vision sensor sees something
        if (sensor == visionSensor && visionSensor.HasObserved)
        {
            // You can loop through observed objects to see if it's the player, then update state
            worldState["PlayerInSight"] = true;
        }
        else if (sensor == visionSensor && !visionSensor.HasObserved)
        {
            worldState["PlayerInSight"] = false;
        }
    }
    
    protected override void OnMovementStopped(KaijuMovement movement)
    {
        isMoving = false;
    }
    
    // Planner
    public void RequestPlan(Dictionary<string, bool> goalState)
    {
        Debug.Log("GOAP (A*): Calculating plan");
        currentPlan.Clear();
        
        // Open list: paths currently exploring
        List<GoapNode> openList = new List<GoapNode>();
        // Closed list: paths already checked
        List<GoapNode> closedList = new List<GoapNode>();
        
        // Start with current world state
        GoapNode startNode = new GoapNode(null, 0, worldState, null);
        startNode.heuristicCost = CalculateHeuristic(worldState, goalState);
        openList.Add(startNode);
        
        GoapNode cheapestNode = null;

        while (openList.Count > 0)
        {
            // Get node with lowest total (F) cost
            openList.Sort((a, b) => a.TotalCost.CompareTo(b.TotalCost));
            GoapNode currentNode = openList[0];
            
            openList.Remove(currentNode);
            closedList.Add(currentNode);
            
            // Check if node meets our goal
            if (IsGoalMet(currentNode.state, goalState))
            {
                cheapestNode = currentNode;
                break;
            }
            
            // if not at goal, try all actions from this state
            foreach (GoapAction action in availableActions)
            {
                // Can we perform this action?
                if (ArePreconditionsMet(action.preconditions, currentNode.state))
                {
                    // create new state based on action's effects
                    Dictionary<string, bool> newState = new Dictionary<string, bool>(currentNode.state);
                    foreach (var effect in action.effects) newState[effect.Key] = effect.Value;
                    
                    // Create a new node for this state
                    float gCost = currentNode.runningCost + action.cost;
                    GoapNode neighbourNode = new GoapNode(currentNode, gCost, newState, action);
                    neighbourNode.heuristicCost = CalculateHeuristic(newState, goalState);
                    
                    // if we already checked that state and found cheaper way, skip it
                    if (closedList.Exists(n => StatesMatch(n.state, newState) && n.runningCost <= gCost)) continue;
                    
                    // if it's already in open list with cheaper way, skip it
                    GoapNode existingOpen = openList.FirstOrDefault(n => StatesMatch(n.state, newState));
                    if (existingOpen != null)
                    {
                        if (existingOpen.runningCost > gCost)
                        {
                            openList.Remove(existingOpen);
                            openList.Add(neighbourNode);
                        }
                    }
                    else
                    {
                        // add to open list to be explored
                        openList.Add(neighbourNode);
                    }
                }
            }
        }
        
        // Trace best path backward to build queue
        if (cheapestNode != null)
        {
            List<GoapAction> actionPath = new List<GoapAction>();
            GoapNode node = cheapestNode;

            while (node.action != null)
            {
                actionPath.Add(node.action);
                node = node.parent;
            }
            
            actionPath.Reverse();
            foreach (var action in actionPath) currentPlan.Enqueue(action);
            
            Debug.Log($"GOAP (A*): Optimal plan found with {currentPlan.Count} steps");
            StopAllCoroutines();
            StartCoroutine(ExecutePlan());
        }

        else
        {
            Debug.LogError("GOAP (A*): Plan failed. No valid sequence could reach the goal.");
        }
    }
    
    // Checks if all preconditions are true in the current state
    private bool ArePreconditionsMet(Dictionary<string, bool> preconditions, Dictionary<string, bool> state)
    {
        foreach (var prereq in preconditions)
        {
            if (!state.ContainsKey(prereq.Key) || state[prereq.Key] != prereq.Value) return false;
        }
        return true;
    }
    
    // Checks if current state satisfies all goal conditions
    private bool IsGoalMet(Dictionary<string, bool> state, Dictionary<string, bool> goalState)
    {
        foreach (var condition in goalState)
        {
            if (!state.ContainsKey(condition.Key) || state[condition.Key] != condition.Value) return false;
        }
        return true;
    }
    
    // H cost (Hamming Distance) : how many goals are we still missing?
    private float CalculateHeuristic(Dictionary<string, bool> state, Dictionary<string, bool> goalState)
    {
        float missingGoals = 0;
        foreach (var condition in goalState)
        {
            if (!state.ContainsKey(condition.Key) ||  state[condition.Key] != condition.Value) missingGoals++;
        }

        return missingGoals;
    }
    
    // Check if two states are identical
    private bool StatesMatch(Dictionary<string, bool> stateA, Dictionary<string, bool> stateB)
    {
        if (stateA.Count !=  stateB.Count) return false;
        foreach (var s in stateA)
        {
           if (!stateB.ContainsKey(s.Key) || stateB[s.Key] != s.Value) return false; 
        }

        return true;
    }
    
    // Execution
    private IEnumerator ExecutePlan()
    {
        while (currentPlan.Count > 0)
        {
            GoapAction currentAction = currentPlan.Dequeue();
            Debug.Log($"GOAP (A*) Executing: {currentAction.name}");
            yield return StartCoroutine(currentAction.logicCoroutine(this));
            
            // Apply the effects to the world state once complete
            foreach(var effect in currentAction.effects) worldState[effect.Key] =  effect.Value;
        }
        Debug.Log("GOAP (A*): Finished");
    }

    // Action Coroutines
    private IEnumerator Action_MoveTo(GOAP context, string roomName)
    {
        GameObject target = GameObject.Find("POI_" + roomName);
        if (target != null)
        {
            context.Agent.PathFollow(target.transform.position);
            
            while (Vector3.Distance(context.Agent.transform.position, target.transform.position) > 1.5f) 
            {
                yield return null; 
            }
            
            context.Agent.Stop();
        }
        else
        {
            Debug.LogError($"GOAP: Action_MoveTo failed because POI_{roomName} was not found.");
        }
    }

    private IEnumerator Action_Hide(GOAP context)
    {
        Debug.Log("*Ghost turns invisible*");
        yield return new WaitForSeconds(1.5f);
    }

    private IEnumerator Action_JumpScare(GOAP context)
    {
        Debug.Log("BOO!");
        yield return new WaitForSeconds(2.0f);
    }
}

// Action Data Structure
public class GoapAction
{
    public string name;
    public float cost;
    public Dictionary<string, bool> preconditions;
    public Dictionary<string, bool> effects;
    public System.Func<GOAP, IEnumerator> logicCoroutine;

    public GoapAction(string name, float cost, Dictionary<string, bool> preReqs, Dictionary<string, bool> effects,
        System.Func<GOAP, IEnumerator> actionLogic)
    {
        this.name = name;
        this.cost = cost;
        this.preconditions = preReqs;
        this.effects = effects;
        this.logicCoroutine = actionLogic;
    }
}

// A* Node Class
public class GoapNode
{
    public GoapNode parent; // The node that came before this one
    public float runningCost; // G-Cost: Cost of all actions taken so far
    public float heuristicCost; // H-Cost: Estimated cost to reach the goal
    public Dictionary<string, bool> state; // The  world state at this step
    public GoapAction action; // The action that created this state

    // F-Cost: Total score for this node (Lower is better)
    public float TotalCost => runningCost + heuristicCost;

    public GoapNode(GoapNode parent, float runningCost, Dictionary<string, bool> state, GoapAction action)
    {
        this.parent = parent;
        this.runningCost = runningCost;
        this.state = new Dictionary<string, bool>(state); // Copy the state
        this.action = action;
    }
}

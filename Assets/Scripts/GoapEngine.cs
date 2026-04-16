using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GoapAction
{
    public string name;
    public float cost;
    public Dictionary<string, object> preconditions;
    public Dictionary<string, object> effects;
    public System.Func<GoapController, IEnumerator> logicCoroutine;

    /// <summary>
    /// Initializes a new instance of the GoapAction class.
    /// </summary>
    /// <param name="name">The name of the action.</param>
    /// <param name="cost">The cost of performing this action.</param>
    /// <param name="preReqs">The preconditions that must be met to perform this action.</param>
    /// <param name="effects">The effects that this action has on the world state.</param>
    /// <param name="actionLogic">The logic coroutine to execute when performing this action.</param>
    public GoapAction(string name, float cost, Dictionary<string, object> preReqs, Dictionary<string, object> effects, System.Func<GoapController, IEnumerator> actionLogic)
    {
        this.name = name; this.cost = cost; this.preconditions = preReqs; this.effects = effects; this.logicCoroutine = actionLogic;
    }
}

public class GoapNode
{
    public GoapNode parent; 
    public float runningCost; 
    public float heuristicCost; 
    public Dictionary<string, object> state; 
    public GoapAction action; 
    public float TotalCost => runningCost + heuristicCost;

    /// <summary>
    /// Initializes a new instance of the GoapNode class.
    /// </summary>
    /// <param name="parent">The parent node in the plan.</param>
    /// <param name="runningCost">The cumulative cost from the start node to this node.</param>
    /// <param name="state">The world state at this node.</param>
    /// <param name="action">The action taken to reach this node.</param>
    public GoapNode(GoapNode parent, float runningCost, Dictionary<string, object> state, GoapAction action)
    {
        this.parent = parent; 
        this.runningCost = runningCost; 
        this.action = action;
        // Deep copy ensures we don't accidentally modify the live world state during planning
        this.state = new Dictionary<string, object>(state); 
    }
}

// ==========================================
// THE GOAP ENGINE (Static Planner)
// ==========================================

public static class GoapEngine
{
    /// <summary>
    /// Generates a plan (sequence of actions) to transition from the start state to the goal state.
    /// </summary>
    /// <param name="start">The initial world state.</param>
    /// <param name="goal">The desired goal state.</param>
    /// <param name="actions">The list of available actions.</param>
    /// <returns>A queue of actions representing the plan, or null if no plan is found.</returns>
    public static Queue<GoapAction> Plan(Dictionary<string, object> start, Dictionary<string, object> goal, List<GoapAction> actions)
    {
        List<GoapNode> openList = new List<GoapNode>();
        List<GoapNode> closedList = new List<GoapNode>();
        
        GoapNode startNode = new GoapNode(null, 0, start, null);
        startNode.heuristicCost = CalculateHeuristic(start, goal);
        openList.Add(startNode);
        
        int iterations = 0;
        while (openList.Count > 0 && iterations < 1000)
        {
            iterations++;
            // Sort by TotalCost (G + H)
            openList = openList.OrderBy(n => n.TotalCost).ToList();
            GoapNode currentNode = openList[0];
            openList.RemoveAt(0);
            closedList.Add(currentNode);
            
            if (IsGoalMet(currentNode.state, goal)) return ReconstructPath(currentNode);
            
            foreach (GoapAction action in actions)
            {
                if (ArePreconditionsMet(action.preconditions, currentNode.state))
                {
                    Dictionary<string, object> newState = new Dictionary<string, object>(currentNode.state);
                    foreach (var effect in action.effects) newState[effect.Key] = effect.Value;
                    
                    float gCost = currentNode.runningCost + action.cost;
                    
                    // Skip if we've already found a better way to get to this state
                    if (closedList.Exists(n => StatesMatch(n.state, newState) && n.runningCost <= gCost)) continue;
                    
                    GoapNode node = new GoapNode(currentNode, gCost, newState, action);
                    node.heuristicCost = CalculateHeuristic(newState, goal);
                    openList.Add(node);
                }
            }
        }
        return null; // No path found
    }

    /// <summary>
    /// Reconstructs the path from the goal node back to the start node.
    /// </summary>
    /// <param name="node">The goal node.</param>
    /// <returns>A queue of actions in the correct order (start to goal).</returns>
    private static Queue<GoapAction> ReconstructPath(GoapNode node)
    {
        Stack<GoapAction> path = new Stack<GoapAction>();
        while (node.action != null) { path.Push(node.action); node = node.parent; }
        return new Queue<GoapAction>(path);
    }

    /// <summary>
    /// Checks if all preconditions of an action are met by the current state.
    /// </summary>
    /// <param name="pre">The action's preconditions.</param>
    /// <param name="st">The current world state.</param>
    /// <returns>True if all preconditions are met, false otherwise.</returns>
    private static bool ArePreconditionsMet(Dictionary<string, object> pre, Dictionary<string, object> st) => pre.All(p => st.ContainsKey(p.Key) && st[p.Key].Equals(p.Value));

    /// <summary>
    /// Checks if the current state satisfies the goal requirements.
    /// </summary>
    /// <param name="st">The current world state.</param>
    /// <param name="goal">The goal state requirements.</param>
    /// <returns>True if the goal is met, false otherwise.</returns>
    private static bool IsGoalMet(Dictionary<string, object> st, Dictionary<string, object> goal) => goal.All(g => st.ContainsKey(g.Key) && st[g.Key].Equals(g.Value));

    /// <summary>
    /// Calculates the heuristic cost (number of unmet goal conditions) for the given state.
    /// </summary>
    /// <param name="st">The current world state.</param>
    /// <param name="goal">The goal state requirements.</param>
    /// <returns>The heuristic cost.</returns>
    private static float CalculateHeuristic(Dictionary<string, object> st, Dictionary<string, object> goal) => goal.Count(g => !st.ContainsKey(g.Key) || !st[g.Key].Equals(g.Value));

    /// <summary>
    /// Compares two world states for equality.
    /// </summary>
    /// <param name="a">The first state.</param>
    /// <param name="b">The second state.</param>
    /// <returns>True if the states match exactly, false otherwise.</returns>
    private static bool StatesMatch(Dictionary<string, object> a, Dictionary<string, object> b) => a.Count == b.Count && a.All(kv => b.ContainsKey(kv.Key) && b[kv.Key].Equals(kv.Value));
}
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

    private static Queue<GoapAction> ReconstructPath(GoapNode node)
    {
        Stack<GoapAction> path = new Stack<GoapAction>();
        while (node.action != null) { path.Push(node.action); node = node.parent; }
        return new Queue<GoapAction>(path);
    }

    // Predicate comparison using .Equals() to handle strings, ints, and bools correctly
    private static bool ArePreconditionsMet(Dictionary<string, object> pre, Dictionary<string, object> st) => pre.All(p => st.ContainsKey(p.Key) && st[p.Key].Equals(p.Value));
    private static bool IsGoalMet(Dictionary<string, object> st, Dictionary<string, object> goal) => goal.All(g => st.ContainsKey(g.Key) && st[g.Key].Equals(g.Value));
    private static float CalculateHeuristic(Dictionary<string, object> st, Dictionary<string, object> goal) => goal.Count(g => !st.ContainsKey(g.Key) || !st[g.Key].Equals(g.Value));
    private static bool StatesMatch(Dictionary<string, object> a, Dictionary<string, object> b) => a.Count == b.Count && a.All(kv => b.ContainsKey(kv.Key) && b[kv.Key].Equals(kv.Value));
}
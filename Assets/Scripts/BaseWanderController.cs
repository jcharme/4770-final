using UnityEngine;
using System.Collections;
using KaijuSolutions.Agents;
using KaijuSolutions.Agents.Movement;

public abstract class BaseWanderController : KaijuController
{
    protected string[] roomIDs = {
        "Entrance", "Hall-East", "Dining", "Kitchen", "Corridor", 
        "Library", "Hall-West", "Bathroom-North", "Bedroom-Large", 
        "Bathroom-South", "Bedroom-Small-North", "Bedroom-Small-South"
    };

    protected Coroutine wanderCoroutine;

    protected virtual void StartWandering()
    {
        string randomRoom = roomIDs[Random.Range(0, roomIDs.Length)];
        GameObject target = GameObject.Find("POI_" + randomRoom);
        
        if (target != null)
        {
            Agent.PathFollow(target.transform.position);
        }
        else
        {
            Agent.ObstacleAvoidance();
            Agent.Wander(clear: true);
        }
    }

    protected virtual IEnumerator WaitThenWander()
    {
        yield return new WaitForSeconds(Random.Range(5f, 10f));
        StartWandering();
    }
}

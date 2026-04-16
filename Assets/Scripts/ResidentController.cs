using System.Collections;
using KaijuSolutions.Agents;
using UnityEngine;
using KaijuSolutions.Agents.Actuators;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;

public class ResidentController : BaseWanderController
{
    private bool isScared = false;
    
    protected override void OnEnabled()
    {
        StartWandering();
    }
    
    protected override void OnMovementStopped(KaijuMovement movement)
    {
        if (!isScared)
        {
            // Stay in the room for a bit (5 to 10 seconds)
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            wanderCoroutine = StartCoroutine(WaitThenWander());
        }
        else
        {
            // Reached safety, wait to calm down
            CancelInvoke(nameof(StopBeingScared));
            Invoke(nameof(StopBeingScared), 8f);
        }
    }

    protected override void StartWandering()
    {
        isScared = false;
        base.StartWandering();
    }

    private void StopBeingScared()
    {
        StartWandering();
    }
    
    public void TriggerScared(Transform ghostTransform)
    {
        isScared = true;
        
        if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
        CancelInvoke(nameof(StopBeingScared));
        
        // Stop whatever we were doing immediately
        Agent.Stop();

        // Pick a room that is far away from the ghost
        string bestRoom = roomIDs[0];
        float maxDistance = 0;

        foreach (string roomId in roomIDs)
        {
            GameObject room = GameObject.Find("POI_" + roomId);
            if (room != null)
            {
                float dist = Vector3.Distance(room.transform.position, ghostTransform.position);
                if (dist > maxDistance)
                {
                    maxDistance = dist;
                    bestRoom = roomId;
                }
            }
        }

        GameObject target = GameObject.Find("POI_" + bestRoom);
        if (target != null)
        {
            Agent.PathFollow(target.transform.position);
        }
        else
        {
            Agent.ObstacleAvoidance();
            Agent.Flee(ghostTransform, clear: true);
        }
    }
}

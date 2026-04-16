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
    
    private float GetWalkableDistance(Vector3 start, Vector3 target)
    {
        UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
        
        // Calculate the path on the NavMesh
        if (UnityEngine.AI.NavMesh.CalculatePath(start, target, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            float distance = 0.0f;
            // Add up the distance between each corner of the calculated path
            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                distance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }
            return distance;
        }
        
        // If the path is blocked/unreachable, return a massive number so it's not chosen
        return float.MaxValue; 
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
                float distGhostToRoom = GetWalkableDistance(ghostTransform.position, room.transform.position);
                float distResidentToRoom = GetWalkableDistance(transform.position, room.transform.position);
                
                if (distGhostToRoom == float.MaxValue || distResidentToRoom == float.MaxValue) continue;

                float roomScore = distGhostToRoom - (distResidentToRoom * 0.5f); 

                if (roomScore > maxDistance) // using maxDistance variable to hold the best score
                {
                    maxDistance = roomScore;
                    bestRoom = roomId;
                }
            }
        }

        GameObject target = GameObject.Find("POI_" + bestRoom);
        if (target != null)
        {
            Agent.PathFollow(target.transform.position, clear: true);
            
        }
        else
        {
            Agent.Flee(ghostTransform, clear: true);
            Agent.ObstacleAvoidance(clear: false);
        }
    }
}

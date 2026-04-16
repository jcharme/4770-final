using UnityEngine;
using System.Collections;

public class ResidentController : GoapController
{
    private bool isScared = false;
    
    /// <summary>
    /// Called when the script instance is being loaded.
    /// Initializes actions and starts the adjacent wandering behavior.
    /// </summary>
    protected override void OnEnabled()
    {
        base.OnEnabled();
        StartAdjacentWander();
    }

    /// <summary>
    /// Initiates the wandering behavior between adjacent rooms.
    /// </summary>
    private void StartAdjacentWander()
    {
        isScared = false;
        if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
        wanderCoroutine = StartCoroutine(AdjacentWanderLoop());
    }

    /// <summary>
    /// Coroutine that periodically chooses an adjacent room to wander to.
    /// </summary>
    /// <returns>An IEnumerator for the coroutine.</returns>
    private IEnumerator AdjacentWanderLoop()
    {
        while (!isScared)
        {
            yield return new WaitForSeconds(Random.Range(5f, 10f));
            if (isScared) continue;

            string currentRoom = worldState["CurrentRoom"].ToString();
            if (roomAdjacency.ContainsKey(currentRoom))
            {
                string[] neighbors = roomAdjacency[currentRoom];
                string nextRoom = neighbors[Random.Range(0, neighbors.Length)];
                Debug.Log($"Resident wandering from {currentRoom} to neighbor {nextRoom}");
                MoveToRoom(nextRoom);
            }
        }
    }

    /// <summary>
    /// Called when a GOAP plan is completed or aborted.
    /// Resumes wandering if not currently scared.
    /// </summary>
    protected override void OnPlanComplete()
    {
        if (!isScared) StartAdjacentWander();
    }

    /// <summary>
    /// Triggers the scared state of the resident, causing them to flee from the ghost.
    /// </summary>
    /// <param name="ghostTransform">The transform of the ghost that caused the scare.</param>
    public void TriggerScared(Transform ghostTransform)
    {
        isScared = true;
        if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
        Agent.Stop();

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
                if (roomScore > maxDistance)
                {
                    maxDistance = roomScore;
                    bestRoom = roomId;
                }
            }
        }

        GameObject target = GameObject.Find("POI_" + bestRoom);
        if (target != null)
        {
            MoveToRoom(bestRoom);
        }
        else
        {
            Agent.Flee(ghostTransform, clear: true);
        }

        Invoke(nameof(StopBeingScared), 8f);
    }

    /// <summary>
    /// Resets the scared state and resumes normal wandering.
    /// </summary>
    private void StopBeingScared()
    {
        StartAdjacentWander();
    }

    /// <summary>
    /// Calculates the walking distance between two points on the NavMesh.
    /// </summary>
    /// <param name="start">The starting position.</param>
    /// <param name="target">The target position.</param>
    /// <returns>The calculated distance, or float.MaxValue if no path exists.</returns>
    private float GetWalkableDistance(Vector3 start, Vector3 target)
    {
        UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
        if (UnityEngine.AI.NavMesh.CalculatePath(start, target, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            float distance = 0.0f;
            for (int i = 0; i < path.corners.Length - 1; i++) distance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            return distance;
        }
        return float.MaxValue; 
    }
}

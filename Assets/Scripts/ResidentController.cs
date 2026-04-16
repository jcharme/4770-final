using UnityEngine;
using System.Collections;

/// <summary>
/// Specialized GOAP agent representing a resident who wanders between rooms and can be scared by ghosts.
/// </summary>
public class ResidentController : GoapController
{
    private bool isScared = false;

    /// <summary>
    /// Initializes the resident and starts their wandering behavior.
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
        if (wanderCoroutine != null)
        {
            StopCoroutine(wanderCoroutine);
        }
        wanderCoroutine = StartCoroutine(AdjacentWanderLoop());
    }

    /// <summary>
    /// Coroutine that periodically chooses an adjacent room to wander to.
    /// </summary>
    private IEnumerator AdjacentWanderLoop()
    {
        while (!isScared)
        {
            yield return new WaitForSeconds(Random.Range(5f, 10f));

            if (isScared)
            {
                continue;
            }

            string currentRoom = worldState["CurrentRoom"].ToString();
            if (roomAdjacency.ContainsKey(currentRoom))
            {
                string[] neighbors = roomAdjacency[currentRoom];
                string nextRoom = neighbors[Random.Range(0, neighbors.Length)];
                MoveToRoom(nextRoom);
            }
        }
    }

    /// <summary>
    /// Resumes wandering after a plan (such as fleeing) is completed.
    /// </summary>
    protected override void OnPlanComplete()
    {
        if (!isScared)
        {
            StartAdjacentWander();
        }
    }

    /// <summary>
    /// Triggers the scared state, causing the resident to flee to a safe room.
    /// </summary>
    /// <param name="ghostTransform">The transform of the ghost causing the scare.</param>
    public void TriggerScared(Transform ghostTransform)
    {
        isScared = true;

        if (wanderCoroutine != null)
        {
            StopCoroutine(wanderCoroutine);
        }

        Agent.Stop();

        string bestRoom = roomIDs[0];
        float maxDistance = 0;

        // Evaluate all rooms to find the safest one (furthest from the ghost, relatively close to the resident)
        foreach (string roomId in roomIDs)
        {
            GameObject room = GameObject.Find("POI_" + roomId);
            if (room != null)
            {
                float distGhostToRoom = GetWalkableDistance(ghostTransform.position, room.transform.position);
                float distResidentToRoom = GetWalkableDistance(transform.position, room.transform.position);

                if (distGhostToRoom == float.MaxValue || distResidentToRoom == float.MaxValue)
                {
                    continue;
                }

                // Heuristic for "safety": far from ghost, but reachable for the resident
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

        // Recover from being scared after a delay
        Invoke(nameof(StopBeingScared), 8f);
    }

    /// <summary>
    /// Resets the scared state and resumes normal behavior.
    /// </summary>
    private void StopBeingScared()
    {
        StartAdjacentWander();
    }

    /// <summary>
    /// Calculates the actual walking distance between two points using the NavMesh.
    /// </summary>
    /// <param name="start">Starting position.</param>
    /// <param name="target">Target position.</param>
    /// <returns>The path distance, or float.MaxValue if no path is found.</returns>
    private float GetWalkableDistance(Vector3 start, Vector3 target)
    {
        UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
        if (UnityEngine.AI.NavMesh.CalculatePath(start, target, UnityEngine.AI.NavMesh.AllAreas, path))
        {
            float distance = 0.0f;
            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                distance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }
            return distance;
        }
        return float.MaxValue;
    }
}

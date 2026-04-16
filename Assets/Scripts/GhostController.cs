using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using KaijuSolutions.Agents.Sensors;

/// <summary>
/// Specialized GOAP agent representing a ghost that hunts and scares residents.
/// </summary>
public class GhostController : GoapController
{
    [Header("Ghost State")]
    public Transform targetVictim;

    protected bool isChasing = false;
    private bool isHunting = false;
    protected KaijuEverythingVisionSensor visionSensor;

    /// <summary>
    /// Initializes ghost-specific state and sensors.
    /// </summary>
    protected override void OnEnabled()
    {
        visionSensor = Agent.GetSensor<KaijuEverythingVisionSensor>();

        worldState["ResidentScared"] = false;
        worldState["ResidentInSight"] = false;
        worldState["NearVictim"] = false;

        base.OnEnabled();
    }

    /// <summary>
    /// Adds ghost-specific actions to the GOAP action pool.
    /// </summary>
    protected override void InitializeActions()
    {
        base.InitializeActions();

        availableActions.Add(new GoapAction(
            name: "ChaseVictim",
            cost: 1f,
            preReqs: new Dictionary<string, object> { { "ResidentInSight", true } },
            effects: new Dictionary<string, object> { { "NearVictim", true } },
            actionLogic: Action_Chase
        ));

        availableActions.Add(new GoapAction(
            name: "ScareVictim",
            cost: 1f,
            preReqs: new Dictionary<string, object> { { "NearVictim", true } },
            effects: new Dictionary<string, object> { { "ResidentScared", true } },
            actionLogic: Action_Scare
        ));
    }

    /// <summary>
    /// Initiates a hunt for a specific victim.
    /// </summary>
    /// <param name="victim">The victim's transform.</param>
    public void BeginHunt(Transform victim)
    {
        isHunting = true;
        targetVictim = victim;

        // Manually update world state to reflect known information
        worldState["ResidentInSight"] = true;
        RequestPlan(new Dictionary<string, object> { { "ResidentScared", true } });
    }

    /// <summary>
    /// Overrides plan request to set chasing state.
    /// </summary>
    /// <param name="goalState">The desired goal state.</param>
    public override void RequestPlan(Dictionary<string, object> goalState)
    {
        isChasing = true;
        base.RequestPlan(goalState);
    }

    /// <summary>
    /// Resets ghost state after a plan (hunt) is completed.
    /// </summary>
    protected override void OnPlanComplete()
    {
        isChasing = false;
        isHunting = false;
        worldState["NearVictim"] = false;
        worldState["ResidentScared"] = false;
        worldState["ResidentInSight"] = false;
        Agent.Stop();
    }

    /// <summary>
    /// Callback for when movement is stopped.
    /// </summary>
    protected override void OnMovementStopped(KaijuSolutions.Agents.Movement.KaijuMovement movement)
    {
    }

    /// <summary>
    /// Updates the world state based on vision sensor observations.
    /// </summary>
    /// <param name="sensor">The updated sensor.</param>
    protected override void OnSense(KaijuSensor sensor)
    {
        if (sensor != visionSensor)
        {
            return;
        }

        worldState["ResidentInSight"] = visionSensor.HasObserved;
    }

    /// <summary>
    /// Action logic for chasing the target victim.
    /// </summary>
    private IEnumerator Action_Chase(GoapController context)
    {
        if (targetVictim == null)
        {
            yield break;
        }

        float timeout = 20f;
        while (targetVictim != null && timeout > 0)
        {
            // If close enough to the victim, the chase is successful
            if (Vector3.Distance(transform.position, targetVictim.position) <= 2.0f)
            {
                break;
            }

            Agent.PathFollow(targetVictim.position, clear: true);
            timeout -= 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        Agent.Stop();

        if (targetVictim == null || Vector3.Distance(transform.position, targetVictim.position) > 2.0f)
        {
            Debug.Log("<color=red>GHOST:</color> Target lost. Aborting chase.");
            worldState["NearVictim"] = false;
        }
    }

    /// <summary>
    /// Action logic for scaring the target victim.
    /// </summary>
    private IEnumerator Action_Scare(GoapController context)
    {
        if (targetVictim == null)
        {
            yield break;
        }

        ResidentController victim = targetVictim.GetComponent<ResidentController>();
        if (victim != null)
        {
            Debug.Log($"<color=red>GHOST:</color> Scaring {targetVictim.name}!");
            victim.TriggerScared(transform);
        }

        yield return new WaitForSeconds(2.0f);
    }
}
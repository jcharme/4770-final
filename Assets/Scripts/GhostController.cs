using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using KaijuSolutions.Agents.Sensors;

public class GhostController : GoapController
{
    [Header("Ghost State")]
    public Transform targetVictim; 
    protected bool isChasing = false;
    protected KaijuEverythingVisionSensor visionSensor;

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Initializes world state flags and starts the wandering behavior.
    /// </summary>
    protected override void OnEnabled()
    {
        visionSensor = Agent.GetSensor<KaijuEverythingVisionSensor>();

        // Add ghost-specific state flags
        worldState["IsHidden"] = false;
        worldState["ResidentScared"] = false;
        worldState["ResidentInSight"] = false;
        worldState["NearVictim"] = false;

        base.OnEnabled();
        StartWandering();
    }

    /// <summary>
    /// Initializes the list of available actions for the ghost.
    /// </summary>
    protected override void InitializeActions()
    {
        base.InitializeActions();

        availableActions.Add(new GoapAction("HideInShadows", 1f, new Dictionary<string, object> {{"CurrentRoom", "Kitchen"}}, new Dictionary<string, object> {{"IsHidden", true}}, Action_Hide));
        availableActions.Add(new GoapAction("ChaseVictim", 1f, new Dictionary<string, object>(), new Dictionary<string, object> {{"NearVictim", true}}, Action_Chase));
        availableActions.Add(new GoapAction("ScareVictim", 1f, new Dictionary<string, object> { { "NearVictim", true } }, new Dictionary<string, object> { { "ResidentScared", true } }, Action_Scare));
    }

    /// <summary>
    /// Called when a GOAP plan is completed or aborted.
    /// </summary>
    protected override void OnPlanComplete()
    {
        isChasing = false;
        worldState["NearVictim"] = false; 
        worldState["ResidentScared"] = false; 
        StartWandering();
    }

    /// <summary>
    /// Initiates the wandering behavior.
    /// </summary>
    private void StartWandering()
    {
        if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
        wanderCoroutine = StartCoroutine(WaitThenWander());
    }

    /// <summary>
    /// Coroutine that waits for a random interval before picking a new room to wander to.
    /// </summary>
    /// <returns>An IEnumerator for the coroutine.</returns>
    private IEnumerator WaitThenWander()
    {
        yield return new WaitForSeconds(Random.Range(5f, 10f));
        string randomRoom = roomIDs[Random.Range(0, roomIDs.Length)];
        GameObject target = GameObject.Find("POI_" + randomRoom);
        if (target != null) Agent.PathFollow(target.transform.position, clear: true);
        else Agent.Wander(clear: true);
    }

    /// <summary>
    /// Called when the agent stops moving.
    /// Resumes wandering if the ghost is not currently chasing a victim.
    /// </summary>
    /// <param name="movement">The movement component that stopped.</param>
    protected override void OnMovementStopped(KaijuSolutions.Agents.Movement.KaijuMovement movement)
    {
        if (!isChasing)
        {
            if (wanderCoroutine != null) StopCoroutine(wanderCoroutine);
            wanderCoroutine = StartCoroutine(WaitThenWander());
        }
    }

    // --- Ghost Specific Action Coroutines ---

    /// <summary>
    /// Coroutine for the "HideInShadows" action.
    /// </summary>
    /// <param name="context">The GOAP controller context.</param>
    /// <returns>An IEnumerator for the coroutine.</returns>
    private IEnumerator Action_Hide(GoapController context) { yield return new WaitForSeconds(1.5f); }
    
    /// <summary>
    /// Coroutine for the "ChaseVictim" action.
    /// </summary>
    /// <param name="context">The GOAP controller context.</param>
    /// <returns>An IEnumerator for the coroutine.</returns>
    private IEnumerator Action_Chase(GoapController context)
    {
        if (targetVictim == null) yield break;
        isChasing = true;
        Agent.PathFollow(targetVictim, clear: true);
        float timeout = 20f;
        while (targetVictim != null && Vector3.Distance(transform.position, targetVictim.position) > 2.0f && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }
        Agent.Stop();
    }

    /// <summary>
    /// Coroutine for the "ScareVictim" action.
    /// </summary>
    /// <param name="context">The GOAP controller context.</param>
    /// <returns>An IEnumerator for the coroutine.</returns>
    private IEnumerator Action_Scare(GoapController context)
    {
        if (targetVictim == null) yield break;
        ResidentController victim = targetVictim.GetComponent<ResidentController>();
        if (victim != null) victim.TriggerScared(transform);
        yield return new WaitForSeconds(2.0f);
    }

    /// <summary>
    /// Called when a sensor updates its observations.
    /// </summary>
    /// <param name="sensor">The sensor that updated.</param>
    protected override void OnSense(KaijuSensor sensor) 
    { 
        if (sensor == visionSensor) worldState["ResidentInSight"] = visionSensor.HasObserved; 
    }
}

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using KaijuSolutions.Agents.Sensors;

public class GhostController : GoapController
{
    [Header("Ghost State")]
    public Transform targetVictim;
    protected bool isChasing = false;
    private bool isHunting = false;
    protected KaijuEverythingVisionSensor visionSensor;

    protected override void OnEnabled()
    {
        visionSensor = Agent.GetSensor<KaijuEverythingVisionSensor>();

        worldState["ResidentScared"] = false;
        worldState["ResidentInSight"] = false;
        worldState["NearVictim"] = false;

        base.OnEnabled();
    }

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

    public void BeginHunt(Transform victim)
    {
        isHunting = true;
        targetVictim = victim;
        worldState["ResidentInSight"] = true; // tell the planner the target is known
        RequestPlan(new Dictionary<string, object> { { "ResidentScared", true } });
    }

    public override void RequestPlan(Dictionary<string, object> goalState)
    {
        isChasing = true;
        base.RequestPlan(goalState);
    }

    protected override void OnPlanComplete()
    {
        isChasing = false;
        isHunting = false;
        worldState["NearVictim"] = false;
        worldState["ResidentScared"] = false;
        worldState["ResidentInSight"] = false;
        Agent.Stop();
    }

    protected override void OnMovementStopped(KaijuSolutions.Agents.Movement.KaijuMovement movement) { }

    protected override void OnSense(KaijuSensor sensor)
    {
        if (sensor != visionSensor) return;
        worldState["ResidentInSight"] = visionSensor.HasObserved;
    }

    private IEnumerator Action_Chase(GoapController context)
    {
        if (targetVictim == null) yield break;

        float timeout = 20f;
        while (targetVictim != null && timeout > 0)
        {
            if (Vector3.Distance(transform.position, targetVictim.position) <= 2.0f) break;
            Agent.PathFollow(targetVictim.position, clear: true);
            timeout -= 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        Agent.Stop();

        if (targetVictim == null || Vector3.Distance(transform.position, targetVictim.position) > 2.0f)
        {
            Debug.Log("<color=red>GHOST:</color> Lost the target. Giving up chase.");
            worldState["NearVictim"] = false;
        }
    }

    private IEnumerator Action_Scare(GoapController context)
    {
        if (targetVictim == null) yield break;

        ResidentController victim = targetVictim.GetComponent<ResidentController>();
        if (victim != null)
        {
            Debug.Log($"<color=red>GHOST:</color> BOO! Scaring {targetVictim.name}!");
            victim.TriggerScared(transform);
        }

        yield return new WaitForSeconds(2.0f);
    }
}
using KaijuSolutions.Agents;
using UnityEngine;
using KaijuSolutions.Agents.Actuators;
using KaijuSolutions.Agents.Movement;
using KaijuSolutions.Agents.Sensors;

public class ResidentController : KaijuController
{
    protected override void OnEnabled()
    {
        Agent.ObstacleAvoidance();
        Agent.Wander(clear: false);
    }
}

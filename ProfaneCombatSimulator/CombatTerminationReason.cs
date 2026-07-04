namespace CombatSimulator.Combat;

// Explains whether combat ended through death or a defensive simulation limit.
public enum CombatTerminationReason
{
    Death,
    TimeLimit,
    EventLimit
}

namespace CombatSimulator.Analysis;

// Couples a generated loadout with the Health weight calculated for that build.
public sealed class WeightedLoadout
{
    public required Loadout Loadout { get; init; }
    public required double HealthWeight { get; init; }
}

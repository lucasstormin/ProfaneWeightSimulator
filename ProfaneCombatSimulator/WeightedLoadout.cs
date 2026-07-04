namespace CombatSimulator.Analysis;

// Couples a generated loadout with one contextual attribute weight.
public sealed class WeightedLoadout
{
    public required Loadout Loadout { get; init; }
    public required double Weight { get; init; }
}

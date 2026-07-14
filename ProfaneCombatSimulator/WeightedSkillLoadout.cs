namespace CombatSimulator.Analysis;

// Couples a generated caster skill loadout with one contextual caster attribute weight.
public sealed class WeightedSkillLoadout
{
    public required SkillLoadout Loadout { get; init; }
    public required double Weight { get; init; }
}

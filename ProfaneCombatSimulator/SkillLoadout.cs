using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Couples a gear loadout with the randomly selected skill bar used by caster analysis.
public sealed class SkillLoadout
{
    public required Loadout Gear { get; init; }
    public required IReadOnlyList<SkillDefinition> Skills { get; init; }
    public CharacterStats Stats => Gear.Stats;
    public string SkillDescription => string.Join(", ", Skills.Select(skill => skill.Name));
}

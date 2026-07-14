namespace CombatSimulator.Models;

// Represents one damage skill imported from the authoritative skill spreadsheet tab.
public sealed class SkillDefinition
{
    public required string Name { get; init; }
    public required double Damage { get; init; }
    public required double AttackScaling { get; init; }
    public required double MagicScaling { get; init; }
    public required double ManaCost { get; init; }
    public required double Cooldown { get; init; }
    public bool IsMagicalOnly => AttackScaling == 0 && MagicScaling > 0;
    public bool IsPhysicalOnly => AttackScaling > 0 && MagicScaling == 0;
    public bool IsMixed => AttackScaling > 0 && MagicScaling > 0;
}

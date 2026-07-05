using CombatSimulator.Combat;

namespace CombatSimulator.Analysis;

// Links one notable fight to both complete builds and its Health Regen context.
public sealed class FightDiagnosticEntry
{
    public required Loadout PlayerA { get; init; }
    public required Loadout PlayerB { get; init; }
    public required TimedCombatResult Fight { get; init; }
    public double HealthWeight { get; init; }
    public double HealthRegenWeight { get; init; }
}

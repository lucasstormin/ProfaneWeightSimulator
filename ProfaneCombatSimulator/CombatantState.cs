using CombatSimulator.Analysis;
using CombatSimulator.Models;

namespace CombatSimulator.Combat;

// Tracks one combatant's mutable Health, combo position, and next contact time.
public sealed class CombatantState
{
    private readonly double speedFactor;

    // Starts Attack 1 Anticipation at time zero with full Health.
    public CombatantState(Loadout loadout)
    {
        Stats = loadout.Stats;
        Profile = loadout.AttackProfile;
        CurrentHealth = Stats.MaxHealth;
        speedFactor = 1 + Stats[AttributeId.AttackSpeed];
        if (speedFactor <= 0)
            throw new InvalidOperationException("Attack Speed must keep the attack-duration divisor positive.");

        CurrentAttackStart = 0;
        ComboIndex = 0;
        NextContactTime = Scale(CurrentStep.Anticipation);
    }

    public CharacterStats Stats { get; }
    public WeaponAttackProfile Profile { get; }
    public double CurrentHealth { get; private set; }
    public double CurrentAttackStart { get; private set; }
    public double NextContactTime { get; private set; }
    public int ComboIndex { get; private set; }
    public int HitsLanded { get; private set; }
    public bool IsAlive => CurrentHealth > 0;
    public AttackStep CurrentStep => Profile.Steps[ComboIndex];

    // Applies one contact's damage while preventing negative remaining Health.
    public void ReceiveDamage(double damage)
    {
        CurrentHealth = Math.Max(0, CurrentHealth - damage);
    }

    // Records a landed hit and schedules the next attack's contact in combo order.
    public void AdvanceAfterContact()
    {
        HitsLanded++;
        CurrentAttackStart += Scale(CurrentStep.TotalDuration);
        ComboIndex = (ComboIndex + 1) % Profile.Steps.Count;
        NextContactTime = CurrentAttackStart + Scale(CurrentStep.Anticipation);
    }

    // Applies the character's unrounded Attack Speed bonus to one phase duration.
    private double Scale(double duration) => duration / speedFactor;
}

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
    public double TotalHealing { get; private set; }
    public double AdditionalRegenHealingOpportunity { get; private set; }
    public double AdditionalLifeStealHealingOpportunity { get; private set; }
    public bool IsAlive => CurrentHealth > 0;
    public AttackStep CurrentStep => Profile.Steps[ComboIndex];

    // Applies one contact's full damage while preserving overkill for pre-death Life Steal.
    public void ReceiveDamage(double damage)
    {
        CurrentHealth -= damage;
    }

    // Applies truncated final-damage Life Steal and tracks the useful gain from one extra percent.
    public void ApplyLifeSteal(double dealtDamage)
    {
        double lifeSteal = Stats[AttributeId.LifeSteal];
        if (dealtDamage <= 0 || lifeSteal <= 0 && lifeSteal + 0.01 <= 0 ||
            CurrentHealth >= Stats.MaxHealth)
        {
            return;
        }

        int integerDamage = (int)dealtDamage;
        double baseHealing = lifeSteal <= 0
            ? 0
            : (int)(integerDamage * (float)lifeSteal);
        double increasedHealing = (int)(integerDamage * (float)Math.Max(0, lifeSteal + 0.01));
        baseHealing = Math.Min(baseHealing, Stats.MaxHealth - CurrentHealth);
        increasedHealing = Math.Min(increasedHealing, Stats.MaxHealth - CurrentHealth);
        if (CurrentHealth + baseHealing > 0)
        {
            AdditionalLifeStealHealingOpportunity +=
                Math.Max(0, increasedHealing - baseHealing);
        }
        CurrentHealth += baseHealing;
        TotalHealing += baseHealing;
    }

    // Applies one ceiling-rounded regeneration tick without exceeding maximum Health.
    public void Regenerate()
    {
        double regeneration = Stats[AttributeId.HealthRegen];
        if (!IsAlive || CurrentHealth >= Stats.MaxHealth)
            return;

        double missingHealth = Stats.MaxHealth - CurrentHealth;
        double baseHealing = regeneration <= 0
            ? 0
            : Math.Min(Math.Ceiling(regeneration), missingHealth);
        double increasedRegeneration = regeneration + 1;
        double increasedHealing = increasedRegeneration <= 0
            ? 0
            : Math.Min(Math.Ceiling(increasedRegeneration), missingHealth);
        AdditionalRegenHealingOpportunity += Math.Max(0, increasedHealing - baseHealing);

        double healing = baseHealing;
        CurrentHealth += healing;
        TotalHealing += healing;
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

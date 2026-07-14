using CombatSimulator.Models;

namespace CombatSimulator.Analysis;

// Simulates deterministic magical skill output over a fixed combat window.
public static class SkillOutputSimulator
{
    public const double DefaultCastTime = 0.5;

    // Calculates total post-resist skill damage from one caster loadout and skill bar.
    public static double Simulate(
        CharacterStats stats,
        IReadOnlyList<SkillDefinition> skills,
        CombatConfig config,
        double combatWindowSeconds,
        double targetMagicResist = 0)
    {
        if (combatWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(combatWindowSeconds));
        if (skills.Count == 0)
            return 0;

        double time = 0;
        double nextCastTime = 0;
        double nextManaTick = 1;
        double mana = stats[AttributeId.MaxMana];
        double totalDamage = 0;
        double[] readyTimes = new double[skills.Count];

        while (time < combatWindowSeconds)
        {
            ApplyManaTicks(stats, combatWindowSeconds, ref time, ref nextManaTick, ref mana);
            if (time >= combatWindowSeconds)
                break;

            if (time >= nextCastTime &&
                TrySelectSkill(stats, skills, config, readyTimes, time, mana, targetMagicResist, out int skillIndex))
            {
                SkillDefinition skill = skills[skillIndex];
                double damage = CalculateDamage(stats, skill, config, targetMagicResist);
                double manaCost = CalculateManaCost(stats, skill);
                mana -= manaCost;
                totalDamage += damage;
                readyTimes[skillIndex] = time + CalculateFinalCooldown(stats, skill);
                nextCastTime = time + DefaultCastTime;
                continue;
            }

            double nextEventTime = FindNextEventTime(readyTimes, nextCastTime, nextManaTick, time, combatWindowSeconds);
            if (nextEventTime <= time)
                break;
            time = nextEventTime;
        }

        return totalDamage;
    }

    // Estimates priority-based skill output for stat weights without discrete breakpoint spikes.
    public static double EstimateSmoothOutput(
        CharacterStats stats,
        IReadOnlyList<SkillDefinition> skills,
        CombatConfig config,
        double combatWindowSeconds,
        double targetMagicResist = 0,
        bool enforceMana = true)
    {
        if (combatWindowSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(combatWindowSeconds));
        if (skills.Count == 0)
            return 0;

        var opportunities = skills
            .Select(skill =>
            {
                double cooldown = Math.Max(DefaultCastTime, CalculateFinalCooldown(stats, skill));
                double damage = CalculateSmoothDamage(stats, skill, config, targetMagicResist);
                return new
                {
                    Skill = skill,
                    Damage = damage,
                    ManaCost = Math.Max(0, CalculateManaCost(stats, skill)),
                    AvailableCasts = combatWindowSeconds / cooldown,
                    Priority = damage / DefaultCastTime
                };
            })
            .Where(opportunity => opportunity.Damage > 0 && opportunity.AvailableCasts > 0)
            .OrderByDescending(opportunity => opportunity.Priority)
            .ThenByDescending(opportunity => opportunity.Damage)
            .ToArray();

        double remainingCastSlots = combatWindowSeconds / DefaultCastTime;
        double remainingMana = enforceMana
            ? stats[AttributeId.MaxMana] +
                (Math.Max(0, stats[AttributeId.ManaRegen]) * combatWindowSeconds)
            : double.PositiveInfinity;
        double totalDamage = 0;

        foreach (var opportunity in opportunities)
        {
            if (remainingCastSlots <= 0 || (enforceMana && remainingMana <= 0))
                break;

            double affordableCasts = !enforceMana || opportunity.ManaCost <= 0
                ? remainingCastSlots
                : remainingMana / opportunity.ManaCost;
            double casts = Math.Min(opportunity.AvailableCasts, Math.Min(remainingCastSlots, affordableCasts));
            if (casts <= 0)
                continue;

            totalDamage += opportunity.Damage * casts;
            remainingCastSlots -= casts;
            if (enforceMana)
                remainingMana -= opportunity.ManaCost * casts;
        }

        return totalDamage;
    }

    // Calculates one skill's rounded magical hit after Magic Resist reduction.
    public static double CalculateDamage(
        CharacterStats stats,
        SkillDefinition skill,
        CombatConfig config,
        double targetMagicResist = 0)
    {
        double rawDamage =
            skill.Damage +
            (stats[AttributeId.AttackPower] * skill.AttackScaling) +
            (stats[AttributeId.MagicPower] * skill.MagicScaling);
        double multiplier = CalculateMagicDamageMultiplier(targetMagicResist, config.MagicalArmorConstant);
        return Math.Round(rawDamage * multiplier);
    }

    // Calculates one skill's unrounded magical damage for smooth weight estimates.
    public static double CalculateSmoothDamage(
        CharacterStats stats,
        SkillDefinition skill,
        CombatConfig config,
        double targetMagicResist = 0)
    {
        double rawDamage =
            skill.Damage +
            (stats[AttributeId.AttackPower] * skill.AttackScaling) +
            (stats[AttributeId.MagicPower] * skill.MagicScaling);
        double multiplier = CalculateMagicDamageMultiplier(targetMagicResist, config.MagicalArmorConstant);
        return rawDamage * multiplier;
    }

    // Applies the sheet-defined mana cost efficiency formula to one skill cost.
    public static double CalculateManaCost(CharacterStats stats, SkillDefinition skill)
    {
        double efficiency = stats[AttributeId.ManaEfficiency];
        return skill.ManaCost - (skill.ManaCost * efficiency);
    }

    // Applies the sheet-defined cooldown reduction formula to one skill cooldown.
    public static double CalculateFinalCooldown(CharacterStats stats, SkillDefinition skill)
    {
        double cooldownReduction = stats[AttributeId.CooldownReduction];
        return Math.Max(0, skill.Cooldown - (skill.Cooldown * cooldownReduction));
    }

    // Calculates the same diminishing-return multiplier used by Magic Resist in game.
    private static double CalculateMagicDamageMultiplier(double magicResist, double resistFactor)
    {
        float factor = (float)resistFactor;
        float resist = (float)magicResist;
        return 1f - (factor * resist / (1f + (factor * Math.Abs(resist))));
    }

    // Applies all mana ticks that occur before the current decision time.
    private static void ApplyManaTicks(
        CharacterStats stats,
        double combatWindowSeconds,
        ref double time,
        ref double nextManaTick,
        ref double mana)
    {
        while (nextManaTick <= time && nextManaTick <= combatWindowSeconds)
        {
            mana = Math.Min(stats[AttributeId.MaxMana], mana + Math.Ceiling(stats[AttributeId.ManaRegen]));
            nextManaTick++;
        }
    }

    // Picks the available, affordable skill with the highest damage per cast.
    private static bool TrySelectSkill(
        CharacterStats stats,
        IReadOnlyList<SkillDefinition> skills,
        CombatConfig config,
        double[] readyTimes,
        double time,
        double mana,
        double targetMagicResist,
        out int selectedIndex)
    {
        selectedIndex = -1;
        double selectedScore = double.NegativeInfinity;
        for (int index = 0; index < skills.Count; index++)
        {
            SkillDefinition skill = skills[index];
            if (readyTimes[index] > time || CalculateManaCost(stats, skill) > mana)
                continue;

            double score = CalculateDamage(stats, skill, config, targetMagicResist) / DefaultCastTime;
            if (score > selectedScore)
            {
                selectedScore = score;
                selectedIndex = index;
            }
        }

        return selectedIndex >= 0;
    }

    // Advances to the next cooldown, cast, or mana event without stepping frame-by-frame.
    private static double FindNextEventTime(
        double[] readyTimes,
        double nextCastTime,
        double nextManaTick,
        double time,
        double combatWindowSeconds)
    {
        double next = combatWindowSeconds;
        if (nextCastTime > time)
            next = Math.Min(next, nextCastTime);
        if (nextManaTick > time)
            next = Math.Min(next, nextManaTick);
        foreach (double readyTime in readyTimes)
        {
            if (readyTime > time)
                next = Math.Min(next, readyTime);
        }

        return next;
    }
}

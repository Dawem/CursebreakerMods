using System;
using HarmonyLib;

namespace GodMode;

[HarmonyPatch]
public static class Patches
{
    public static bool IsPlayerUnit(Unit unit)
    {
        if (unit == null) return false;
        if (unit is Player) return true;
        if (unit.unitPlayer != null) return true;
        if (Player.instance != null && unit == Player.instance) return true;
        return false;
    }

    public static bool IsPlayerUnitAttacks(UnitAttacks attacks)
    {
        if (attacks == null) return false;
        if (attacks.player != null) return true;
        if (attacks.unit != null && IsPlayerUnit(attacks.unit)) return true;
        return false;
    }

    [HarmonyPatch(typeof(FileReader), nameof(FileReader.DevBuild))]
    [HarmonyPrefix]
    public static bool FileReader_DevBuild_Prefix(ref bool __result)
    {
        if (Plugin.EnableDebugMode.Value)
        {
            __result = true;
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(GameHandler), "Awake")]
    [HarmonyPostfix]
    public static void GameHandler_Awake_Postfix()
    {
        if (Plugin.EnableDebugMode.Value)
        {
            GameHandler.devBuild = true;
        }
    }

    [HarmonyPatch(typeof(GameHandler), "Start")]
    [HarmonyPostfix]
    public static void GameHandler_Start_Postfix()
    {
        if (Plugin.EnableDebugMode.Value)
        {
            GameHandler.devBuild = true;
            if (CBDebug.instance != null)
            {
                CBDebug.instance.debug = true;
            }
        }
    }

    [HarmonyPatch(typeof(CBDebug), "Awake")]
    [HarmonyPostfix]
    public static void CBDebug_Awake_Postfix(CBDebug __instance)
    {
        if (Plugin.EnableDebugMode.Value && __instance != null)
        {
            __instance.debug = true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ManualStart))]
    [HarmonyPostfix]
    public static void Player_ManualStart_Postfix(Player __instance)
    {
        if (__instance == null) return;

        if (Plugin.GodModeEnabled.Value)
        {
            if (Plugin.Invincible.Value)
            {
                __instance.godmode = true;
                __instance.immuneToDamage = true;
            }

            if (Plugin.InfiniteHealth.Value && __instance.stats != null)
            {
                __instance.currentHealth = __instance.stats.GetStatValue(StatsHandler.StatType.health);
            }

            if (Plugin.InfiniteMana.Value && __instance.stats != null)
            {
                __instance.currentMana = __instance.stats.GetStatValue(StatsHandler.StatType.mana);
            }

            if (Plugin.InfiniteSpecialPower.Value)
            {
                __instance.currentSpecialPower = 100f;
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.TakeDamage))]
    [HarmonyPrefix]
    public static bool Player_TakeDamage_Prefix(Player __instance, DamageHandler.Damage damage)
    {
        if (Plugin.GodModeEnabled.Value && Plugin.Invincible.Value)
        {
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.TakeDamage))]
    [HarmonyPrefix]
    public static bool Unit_TakeDamage_Prefix(Unit __instance, ref DamageHandler.Damage damage)
    {
        if (!Plugin.GodModeEnabled.Value)
        {
            return true;
        }

        // Target is the player -> block damage
        if (IsPlayerUnit(__instance) && Plugin.Invincible.Value)
        {
            return false;
        }

        // Attacker is the player -> boost damage or one-hit kill
        if (damage != null && damage.attackerUnit != null && IsPlayerUnit(damage.attackerUnit))
        {
            if (Plugin.OneHitKill.Value)
            {
                damage.damage = 999999f;
                damage.modifiedDamage = 999999f;
            }
            else if (Plugin.DamageMultiplier.Value > 1.0f)
            {
                damage.damage *= Plugin.DamageMultiplier.Value;
                damage.modifiedDamage *= Plugin.DamageMultiplier.Value;
            }
        }

        return true;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Death))]
    [HarmonyPrefix]
    public static bool Player_Death_Prefix()
    {
        if (Plugin.GodModeEnabled.Value && Plugin.PreventDeath.Value)
        {
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.Death))]
    [HarmonyPrefix]
    public static bool Unit_Death_Prefix(Unit __instance)
    {
        if (Plugin.GodModeEnabled.Value && Plugin.PreventDeath.Value && IsPlayerUnit(__instance))
        {
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.SetHealth))]
    [HarmonyPrefix]
    public static void Unit_SetHealth_Prefix(Unit __instance, ref float health)
    {
        if (!Plugin.GodModeEnabled.Value || !IsPlayerUnit(__instance))
        {
            return;
        }

        if (Plugin.InfiniteHealth.Value && __instance.stats != null)
        {
            float maxHp = __instance.stats.GetStatValue(StatsHandler.StatType.health);
            if (health < maxHp)
            {
                health = maxHp;
            }
        }
        else if (Plugin.Invincible.Value && health < 1f)
        {
            health = 1f;
        }
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.AddMana))]
    [HarmonyPrefix]
    public static bool Unit_AddMana_Prefix(Unit __instance, int amount)
    {
        if (Plugin.GodModeEnabled.Value && Plugin.InfiniteMana.Value && IsPlayerUnit(__instance))
        {
            if (amount < 0)
            {
                return false;
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(Unit), nameof(Unit.AddSpecialPower))]
    [HarmonyPrefix]
    public static bool Unit_AddSpecialPower_Prefix(Unit __instance, int v)
    {
        if (Plugin.GodModeEnabled.Value && Plugin.InfiniteSpecialPower.Value && IsPlayerUnit(__instance))
        {
            if (v < 0)
            {
                return false;
            }
        }
        return true;
    }

    [HarmonyPatch(typeof(UnitAttacks), nameof(UnitAttacks.SetAbilityCooldown), typeof(int), typeof(float))]
    [HarmonyPrefix]
    public static bool SetAbilityCooldown_Int_Prefix(UnitAttacks __instance)
    {
        if (Plugin.GodModeEnabled.Value && Plugin.NoCooldowns.Value && IsPlayerUnitAttacks(__instance))
        {
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(UnitAttacks), nameof(UnitAttacks.SetAbilityCooldown), typeof(AbilityHandler.Ability))]
    [HarmonyPrefix]
    public static bool SetAbilityCooldown_Ability_Prefix(UnitAttacks __instance)
    {
        if (Plugin.GodModeEnabled.Value && Plugin.NoCooldowns.Value && IsPlayerUnitAttacks(__instance))
        {
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(UnitStats), nameof(UnitStats.GetStatValue), typeof(StatsHandler.StatType))]
    [HarmonyPostfix]
    public static void UnitStats_GetStatValue_Postfix(UnitStats __instance, StatsHandler.StatType statType, ref float __result)
    {
        if (!Plugin.GodModeEnabled.Value)
        {
            return;
        }

        if (statType == StatsHandler.StatType.movementSpeed && Plugin.MovementSpeedMultiplier.Value > 1.0f)
        {
            if (__instance.unit != null && IsPlayerUnit(__instance.unit))
            {
                __result *= Plugin.MovementSpeedMultiplier.Value;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.UI.Stations;
using MelonLoader;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>
/// Estações trabalham mais rápido (multiplicador por tipo). Todas avançam em OnTimePass em TODOS os PCs, então
/// todo mundo precisa do mod (o launcher garante) e da mesma config.
/// </summary>
public sealed class StationSpeed : Feature<StationSpeed>
{
    public override string Id => "StationSpeed";
    public override string Name => "Station Speed";
    public override string Description => "Cauldron, chemistry, forno, drying rack, mixing e packaging mais rápidos.";

    private MelonPreferences_Entry<float> _cauldron = null!, _chemistry = null!, _oven = null!, _drying = null!, _mixing = null!, _packaging = null!;
    // Receitas de chemistry são assets compartilhados que sobrevivem a recarregar o save: guardamos o original.
    private readonly Dictionary<string, int> _recipeOriginals = new();
    private readonly List<StationRecipe> _recipes = new();

    protected override void OnInit()
    {
        MelonPreferences_Entry<float> S(string id, string name) => Setting(id, 2f, name + ": velocidade x N", "1 = normal");
        _cauldron = S("Cauldron", "Cauldron");
        _chemistry = S("ChemistryStation", "Chemistry station");
        _oven = S("LabOven", "Lab oven");
        _drying = S("DryingRack", "Drying rack");
        _mixing = S("MixingStation", "Mixing station");
        _packaging = S("PackagingStation", "Packaging station (funcionário)");
        EnabledEntry.OnEntryValueChanged.Subscribe((_, _) => ApplyRecipes());
        _chemistry.OnEntryValueChanged.Subscribe((_, _) => ApplyRecipes());
    }

    private float Mult(MelonPreferences_Entry<float> e) => Active ? Math.Max(0.1f, e.Value) : 1f;
    private static int Scale(int minutes, float mult) => Math.Max(1, Mathf.RoundToInt(minutes / mult));

    private void ApplyRecipes()
    {
        foreach (var r in _recipes)
        {
            if (r == null) continue;
            if (!_recipeOriginals.TryGetValue(r.RecipeID, out int original)) _recipeOriginals[r.RecipeID] = original = r.CookTime_Mins;
            r.CookTime_Mins = Scale(original, Mult(_chemistry));
        }
        if (_recipes.Count > 0) Log.Debug($"chemistry: {_recipes.Count} receitas x{Mult(_chemistry)}");
    }

    // Cauldron: CookTime é lido quando o cozimento começa (a contagem regressiva é o que vai pro save).
    [HarmonyPatch(typeof(Cauldron), nameof(Cauldron.Awake))]
    private static class CauldronPatch
    {
        private static void Postfix(Cauldron __instance) => I.Guard("Cauldron.Awake", () =>
        {
            int old = __instance.CookTime;
            __instance.CookTime = Scale(old, I.Mult(I._cauldron));
            I.Log.Debug($"cauldron: {old} -> {__instance.CookTime} min");
        });
    }

    // Chemistry: duração vem de StationRecipe.CookTime_Mins; escala antes da UI montar a lista de receitas.
    [HarmonyPatch(typeof(ChemistryStationInterface), nameof(ChemistryStationInterface.Awake))]
    private static class ChemistryPatch
    {
        private static void Prefix(ChemistryStationInterface __instance) => I.Guard("ChemistryStationInterface.Awake", () =>
        {
            I._recipes.Clear();
            foreach (var r in __instance.Recipes) I._recipes.Add(r);
            I.ApplyRecipes();
        });
    }

    // Lab oven: duração vem do item (CookableModule); GetCookDuration é usado pelo timer, UI e IsComplete.
    [HarmonyPatch(typeof(OvenCookOperation), nameof(OvenCookOperation.GetCookDuration))]
    private static class OvenPatch
    {
        private static void Postfix(ref int __result)
        {
            if (!I.Active) return;
            __result = Scale(__result, I.Mult(I._oven));
        }
    }

    // Drying rack: DRY_MINS_PER_TIER é const; o progresso por minuto é multiplicado por GetDryMultiplier.
    [HarmonyPatch(typeof(DryingRack), nameof(DryingRack.GetDryMultiplier))]
    private static class DryingPatch
    {
        private static void Postfix(ref float __result)
        {
            if (!I.Active) return;
            __result *= I.Mult(I._drying);
        }
    }

    // Mixing station (e Mk2, que herda Awake): tempo por item.
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Awake))]
    private static class MixingPatch
    {
        private static void Postfix(MixingStation __instance) => I.Guard("MixingStation.Awake", () =>
        {
            int old = __instance.MixTimePerItem;
            __instance.MixTimePerItem = Scale(old, I.Mult(I._mixing));
            I.Log.Debug($"mixing station: {old} -> {__instance.MixTimePerItem} min/item");
        });
    }

    // Packaging station (e Mk2): só o trabalho do funcionário tem duração; o do jogador é minigame.
    [HarmonyPatch(typeof(PackagingStation), nameof(PackagingStation.Awake))]
    private static class PackagingPatch
    {
        private static void Postfix(PackagingStation __instance) => I.Guard("PackagingStation.Awake", () =>
        {
            __instance.PackagerEmployeeSpeedMultiplier *= I.Mult(I._packaging);
        });
    }
}

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
/// todo mundo precisa do mod (o launcher garante) e da mesma config. Velocidade final = config x melhoria comprada
/// (Upgrades); a melhoria vale mesmo com esta feature desligada.
/// </summary>
public sealed class StationSpeed : Feature<StationSpeed>
{
    public override string Id => "StationSpeed";
    public override string Name => "Station Speed";
    public override string Description => "Cauldron, chemistry, forno, drying rack, mixing e packaging mais rápidos.";

    private Pref<float> _cauldron = null!, _chemistry = null!, _oven = null!, _drying = null!, _mixing = null!, _packaging = null!;
    // Receitas de chemistry são assets compartilhados que sobrevivem a recarregar o save: guardamos o original.
    private readonly Dictionary<string, int> _recipeOriginals = new();
    private readonly List<StationRecipe> _recipes = new();
    // Mesas que só leem a velocidade ao nascer: guardamos o valor original para reaplicar quando a config/melhoria muda.
    private readonly List<(Cauldron Station, int Original)> _cauldrons = new();
    private readonly List<(MixingStation Station, int Original)> _mixers = new();
    private readonly List<(PackagingStation Station, float Original)> _packagers = new();

    protected override void OnInit()
    {
        Pref<float> S(string id, string name) => Setting(id, 2f, name + ": velocidade x N", "1 = normal");
        _cauldron = S("Cauldron", "Cauldron");
        _chemistry = S("ChemistryStation", "Chemistry station");
        _oven = S("LabOven", "Lab oven");
        _drying = S("DryingRack", "Drying rack");
        _mixing = S("MixingStation", "Mixing station");
        _packaging = S("PackagingStation", "Packaging station (funcionário)");
        Enabled.Changed += Reapply;
        foreach (var p in new[] { _cauldron, _chemistry, _oven, _drying, _mixing, _packaging }) p.Changed += Reapply;
    }

    // Os patches das mesas não usam Guard (que pula com a feature desligada): a mesa precisa entrar na lista e receber a
    // melhoria comprada mesmo com a config de velocidade desligada.
    private static void Run(string what, Action action)
    {
        if (I.Faulted) return;
        try { action(); }
        catch (Exception e) { I.Fault(what, e); }
    }

    private float Mult(Pref<float> e, Upgrades.Track t) =>
        (Enabled.Value && !Faulted ? Math.Max(0.1f, e.Value) : 1f) * Upgrades.I.Multiplier(t);

    // velocidade final de cada mesa (o livro de receitas mostra os tempos já com ela)
    public float OvenSpeed => Mult(_oven, Upgrades.Oven);
    public float CauldronSpeed => Mult(_cauldron, Upgrades.Cauldron);
    public float DryingSpeed => Mult(_drying, Upgrades.Drying);
    public float MixingSpeed => Mult(_mixing, Upgrades.Mixing);

    public override void OnSceneLoaded(string scene) { _cauldrons.Clear(); _mixers.Clear(); _packagers.Clear(); }

    /// <summary>Aplica a velocidade atual em tudo que já existe (config mudou ou melhoria comprada).</summary>
    public void Reapply()
    {
        ApplyRecipes();
        _cauldrons.RemoveAll(c => c.Station == null);
        foreach (var (station, original) in _cauldrons) station.CookTime = Scale(original, Mult(_cauldron, Upgrades.Cauldron));
        _mixers.RemoveAll(m => m.Station == null);
        foreach (var (station, original) in _mixers) station.MixTimePerItem = Scale(original, Mult(_mixing, Upgrades.Mixing));
        _packagers.RemoveAll(p => p.Station == null);
        foreach (var (station, original) in _packagers) station.PackagerEmployeeSpeedMultiplier = original * Mult(_packaging, Upgrades.Packaging);
        Log.Debug($"velocidade reaplicada: {_cauldrons.Count} caldeirões, {_mixers.Count} mixers, {_packagers.Count} embaladoras");
    }
    private static int Scale(int minutes, float mult) => Math.Max(1, Mathf.RoundToInt(minutes / mult));

    private void ApplyRecipes()
    {
        foreach (var r in _recipes)
        {
            if (r == null) continue;
            if (!_recipeOriginals.TryGetValue(r.RecipeID, out int original)) _recipeOriginals[r.RecipeID] = original = r.CookTime_Mins;
            r.CookTime_Mins = Scale(original, Mult(_chemistry, Upgrades.Chemistry));
        }
        if (_recipes.Count > 0) Log.Debug($"chemistry: {_recipes.Count} receitas x{Mult(_chemistry, Upgrades.Chemistry)}");
    }

    // Cauldron: CookTime é lido quando o cozimento começa (a contagem regressiva é o que vai pro save).
    [HarmonyPatch(typeof(Cauldron), nameof(Cauldron.Awake))]
    private static class CauldronPatch
    {
        private static void Postfix(Cauldron __instance) => Run("Cauldron.Awake", () =>
        {
            int old = __instance.CookTime;
            I._cauldrons.Add((__instance, old));
            __instance.CookTime = Scale(old, I.Mult(I._cauldron, Upgrades.Cauldron));
            I.Log.Debug($"cauldron: {old} -> {__instance.CookTime} min");
        });
    }

    // Chemistry: duração vem de StationRecipe.CookTime_Mins; escala antes da UI montar a lista de receitas.
    [HarmonyPatch(typeof(ChemistryStationInterface), nameof(ChemistryStationInterface.Awake))]
    private static class ChemistryPatch
    {
        private static void Prefix(ChemistryStationInterface __instance) => Run("ChemistryStationInterface.Awake", () =>
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
            float m = I.Mult(I._oven, Upgrades.Oven);
            if (Math.Abs(m - 1f) > 0.001f) __result = Scale(__result, m);
        }
    }

    // Drying rack: DRY_MINS_PER_TIER é const; o progresso por minuto é multiplicado por GetDryMultiplier.
    [HarmonyPatch(typeof(DryingRack), nameof(DryingRack.GetDryMultiplier))]
    private static class DryingPatch
    {
        private static void Postfix(ref float __result) => __result *= I.Mult(I._drying, Upgrades.Drying);
    }

    // Mixing station (e Mk2, que herda Awake): tempo por item.
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Awake))]
    private static class MixingPatch
    {
        private static void Postfix(MixingStation __instance) => Run("MixingStation.Awake", () =>
        {
            int old = __instance.MixTimePerItem;
            I._mixers.Add((__instance, old));
            __instance.MixTimePerItem = Scale(old, I.Mult(I._mixing, Upgrades.Mixing));
            I.Log.Debug($"mixing station: {old} -> {__instance.MixTimePerItem} min/item");
        });
    }

    // Packaging station (e Mk2): só o trabalho do funcionário tem duração; o do jogador é minigame.
    [HarmonyPatch(typeof(PackagingStation), nameof(PackagingStation.Awake))]
    private static class PackagingPatch
    {
        private static void Postfix(PackagingStation __instance) => Run("PackagingStation.Awake", () =>
        {
            float old = __instance.PackagerEmployeeSpeedMultiplier;
            I._packagers.Add((__instance, old));
            __instance.PackagerEmployeeSpeedMultiplier = old * I.Mult(I._packaging, Upgrades.Packaging);
        });
    }
}

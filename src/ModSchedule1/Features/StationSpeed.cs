using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.Tools;
using Il2CppScheduleOne.UI.Stations;
using MelonLoader;
using UnityEngine;
using NpcBehaviour = Il2CppScheduleOne.NPCs.Behaviour.Behaviour;

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
    private readonly List<Mixer> _mixers = new();
    private sealed class Mixer { public MixingStation Station = null!; public int Original; public float Carry; }
    private readonly List<(PackagingStation Station, float Original)> _packagers = new();
    private readonly Dictionary<IntPtr, float> _dryCarry = new();

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

    public override void OnSceneLoaded(string scene) { _cauldrons.Clear(); _mixers.Clear(); _packagers.Clear(); _dryCarry.Clear(); }

    /// <summary>Aplica a velocidade atual em tudo que já existe (config mudou ou melhoria comprada).</summary>
    public void Reapply()
    {
        ApplyRecipes();
        _cauldrons.RemoveAll(c => c.Station == null);
        foreach (var (station, original) in _cauldrons) station.CookTime = Scale(original, Mult(_cauldron, Upgrades.Cauldron));
        _mixers.RemoveAll(m => m.Station == null);
        foreach (var m in _mixers) m.Station.MixTimePerItem = Scale(m.Original, MixingSpeed);
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

    // Lab oven: a duração vem do item (CookableModule) e fica em cache no campo cookDuration da operação. No IL2CPP o
    // GetCookDuration foi embutido em IsComplete/IsReady (progresso e o "pronto?" do Chemist ao chegar) e no timer; só
    // IsReadyForHarvest, o líquido e a UI chamam de verdade. Mudar só o retorno fazia o Chemist ver o forno pronto, ir
    // até ele e desistir em loop até dar o tempo original. Gravamos a duração no cache, que as cópias embutidas leem.
    [HarmonyPatch(typeof(OvenCookOperation), nameof(OvenCookOperation.GetCookDuration))]
    private static class OvenPatch
    {
        private static void Postfix(OvenCookOperation __instance, ref int __result)
        {
            if (I.Faulted) return;
            try
            {
                int d = __instance.Cookable.CookTime;
                float m = I.OvenSpeed;
                if (Math.Abs(m - 1f) > 0.001f) d = Scale(d, m);
                __instance.cookDuration = __result = d;
            }
            catch (Exception e) { I.Fault("OvenCookOperation.GetCookDuration", e); }
        }
    }

    // Drying rack: DRY_MINS_PER_TIER é const; o progresso é minutos x GetDryMultiplier (temperatura). No IL2CPP o
    // GetDryMultiplier foi embutido em OnTimePass, então o patch nele só muda a taxa mostrada na mesa.
    [HarmonyPatch(typeof(DryingRack), nameof(DryingRack.GetDryMultiplier))]
    private static class DryingPatch
    {
        private static void Postfix(ref float __result) => __result *= I.Mult(I._drying, Upgrades.Drying);
    }

    // O progresso de verdade: escala os minutos em OnTimePass (OnMinPass e dormir chamam de verdade), com a sobra
    // fracionária guardada por mesa, como no mixer.
    [HarmonyPatch(typeof(DryingRack), nameof(DryingRack.OnTimePass))]
    private static class DryingTimePatch
    {
        private static void Prefix(DryingRack __instance, ref int minutes)
        {
            if (I.Faulted) return;
            try
            {
                float rate = I.DryingSpeed;
                if (Math.Abs(rate - 1f) < 0.001f) return;
                I._dryCarry.TryGetValue(__instance.Pointer, out float carry);
                float step = minutes * rate + carry;
                minutes = (int)step;
                I._dryCarry[__instance.Pointer] = step - minutes;
            }
            catch (Exception e) { I.Fault("DryingRack.OnTimePass", e); }
        }
    }

    // Mixing station (e Mk2, que herda Awake): tempo por item.
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Awake))]
    private static class MixingPatch
    {
        private static void Postfix(MixingStation __instance) => Run("MixingStation.Awake", () =>
        {
            int old = __instance.MixTimePerItem;
            I._mixers.Add(new Mixer { Station = __instance, Original = old });
            __instance.MixTimePerItem = Scale(old, I.MixingSpeed);
            I.Log.Debug($"mixing station: {old} -> {__instance.MixTimePerItem} min/item");
        });
    }

    // Mixer: o tempo é um inteiro por item (3 min no jogo), então 3 / 2 = 1,5 virava 2 e nada passava de 3x (mínimo de
    // 1 min/item). MixTimePerItem fica com o inteiro mais próximo (o relógio da mesa mostra quase o tempo real) e cada
    // minuto conta o que falta para o tempo exato, com a sobra guardada por mesa. Mk2 chama este pelo base.OnTimePass;
    // dormir também passa por aqui (onTimeSkip).
    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.OnTimePass))]
    private static class MixingTimePatch
    {
        private static void Prefix(MixingStation __instance, ref int minutes)
        {
            if (I.Faulted || __instance.CurrentMixOperation == null) return;
            try
            {
                var ptr = __instance.Pointer;
                var m = I._mixers.Find(x => x.Station.Pointer == ptr);
                if (m == null || m.Original <= 0) return;
                float rate = I.MixingSpeed * __instance.MixTimePerItem / m.Original;
                if (Math.Abs(rate - 1f) < 0.001f) return;
                float step = minutes * rate + m.Carry;
                minutes = (int)step;
                m.Carry = step - minutes;
            }
            catch (Exception e) { I.Fault("MixingStation.OnTimePass", e); }
        }
    }

    // Funcionário no mixer e no secador: antes da operação começar ele coloca os itens um a um (1 s real por item /
    // velocidade de trabalho). Com a capacidade do BetterStacks isso vira minutos e só a operação tinha a melhoria; o
    // jogador pula essa fase. Enquanto ele coloca, a velocidade da mesa entra na velocidade de trabalho dele (a animação
    // também usa). RpcLogic (o wrapper da RPC é embutido) roda uma vez por início, no host e nos clientes.
    private const string BoostLabel = "ModSchedule1.Mesa";

    [HarmonyPatch(typeof(StartMixingStationBehaviour), nameof(StartMixingStationBehaviour.RpcLogic___StartCook_2166136261))]
    private static class MixerWorkPatch
    {
        private static void Prefix(StartMixingStationBehaviour __instance) => Boost(__instance.Npc, I.MixingSpeed);
    }

    [HarmonyPatch(typeof(StartDryingRackBehaviour), nameof(StartDryingRackBehaviour.RpcLogic___BeginAction_2166136261))]
    private static class DryingWorkPatch
    {
        private static void Prefix(StartDryingRackBehaviour __instance) => Boost(__instance.Npc, I.DryingSpeed);
    }

    // Deactivate fecha todo início (o mixer termina com Deactivate_Networked, o secador com Disable); Pause cobre a interrupção.
    [HarmonyPatch]
    private static class WorkEndPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StartMixingStationBehaviour), nameof(StartMixingStationBehaviour.Deactivate));
            yield return AccessTools.Method(typeof(StartMixingStationBehaviour), nameof(StartMixingStationBehaviour.Pause));
            yield return AccessTools.Method(typeof(StartDryingRackBehaviour), nameof(StartDryingRackBehaviour.Deactivate));
            yield return AccessTools.Method(typeof(StartDryingRackBehaviour), nameof(StartDryingRackBehaviour.Pause));
        }

        private static void Postfix(NpcBehaviour __instance) => Run("fim do trabalho na mesa", () =>
            __instance.Npc?.TryCast<Employee>()?.WorkSpeedController.Remove(BoostLabel));
    }

    private static void Boost(NPC? npc, float speed) => Run("funcionário na mesa", () =>
    {
        if (Math.Abs(speed - 1f) > 0.001f && npc?.TryCast<Employee>() is { } e)
            e.WorkSpeedController.Add(new FloatStack.StackEntry(BoostLabel, speed, FloatStack.EStackMode.Multiplicative, 0));
    });

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

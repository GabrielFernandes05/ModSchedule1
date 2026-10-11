using System;
using HarmonyLib;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Tools;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>Funcionários mais rápidos, salário ajustável e mais estações/vasos por funcionário.</summary>
public sealed class BetterEmployees : Feature<BetterEmployees>
{
    public override string Id => "BetterEmployees";
    public override string Name => "Better Employees";
    public override string Description => "Trabalho e passo mais rápidos, salário ajustável, mais estações por funcionário.";
    // Todos os PCs: limites de atribuição são checados só na UI de quem usa a prancheta; velocidade/salário valem no host.

    private const string SpeedLabel = "ModSchedule1";
    private Pref<float> _workSpeed = null!, _walkSpeed = null!, _wage = null!;
    private Pref<int> _botanistPots = null!, _chemistStations = null!, _handlerStations = null!, _cleanerBins = null!;

    protected override void OnInit()
    {
        _workSpeed = Setting("WorkSpeed", 1.5f, "Velocidade de trabalho x N", "Regar, plantar, colher, embalar, operar estações (soma com efeitos de drogas)");
        _walkSpeed = Setting("WalkSpeed", 2.6f, "Velocidade de caminhada (m/s)", "Padrão do jogo: 1.8. 0 = não mexer", min: 0, max: 6);
        _wage = Setting("WageMultiplier", 1f, "Salário diário x N", "1 = normal, 0.5 = metade", min: 0, max: 3);
        _botanistPots = Setting("BotanistMaxPots", 12, "Botanist: vasos por funcionário", "Jogo: 8. Acima do que cabe na lista, os extras funcionam mas não aparecem nela");
        _chemistStations = Setting("ChemistMaxStations", 6, "Chemist: estações por funcionário", "Jogo: 4");
        _handlerStations = Setting("HandlerMaxStations", 5, "Handler: estações por funcionário", "Jogo: 3");
        _cleanerBins = Setting("CleanerMaxBins", 8, "Cleaner: lixeiras por funcionário", "Jogo: 6");
    }

    // Employee.Awake (todas as subclasses chamam base.Awake) roda depois do NPC definir a velocidade padrão e antes
    // de Initialize/AssignProperty. Nada disso vai pro save: remover o mod volta tudo ao normal.
    [HarmonyPatch(typeof(Employee), nameof(Employee.Awake))]
    private static class AwakePatch
    {
        private static void Postfix(Employee __instance) => I.Guard("Employee.Awake", () =>
        {
            float wage = __instance.DailyWage;
            __instance.DailyWage = (float)Math.Round(wage * Math.Max(0f, I._wage.Value));
            if (Math.Abs(I._workSpeed.Value - 1f) > 0.001f)
                __instance.WorkSpeedController.Add(new FloatStack.StackEntry(SpeedLabel, Math.Max(0.1f, I._workSpeed.Value),
                    FloatStack.EStackMode.Multiplicative, 0));
            if (I._walkSpeed.Value > 0f)
                __instance.Movement.SpeedController.SetDefaultSpeed(I._walkSpeed.Value);
            I.Log.Debug($"{__instance.EmployeeType}: salário {wage} -> {__instance.DailyWage}, trabalho x{I._workSpeed.Value}, passo {I._walkSpeed.Value} m/s");
        });
    }

    // A config de atribuições é criada dentro do AssignProperty de cada cargo (depois do base), por isso 4 patches.
    [HarmonyPatch(typeof(Botanist), nameof(Botanist.AssignProperty))]
    private static class BotanistPatch
    {
        private static void Postfix(Botanist __instance) => I.Guard("Botanist.AssignProperty", () =>
            __instance.configuration.Assigns.MaxItems = I._botanistPots.Value);
    }

    [HarmonyPatch(typeof(Chemist), nameof(Chemist.AssignProperty))]
    private static class ChemistPatch
    {
        private static void Postfix(Chemist __instance) => I.Guard("Chemist.AssignProperty", () =>
            __instance.configuration.Stations.MaxItems = I._chemistStations.Value);
    }

    [HarmonyPatch(typeof(Packager), nameof(Packager.AssignProperty))]
    private static class PackagerPatch
    {
        private static void Postfix(Packager __instance) => I.Guard("Packager.AssignProperty", () =>
            __instance.configuration.Stations.MaxItems = I._handlerStations.Value);
    }

    [HarmonyPatch(typeof(Cleaner), nameof(Cleaner.AssignProperty))]
    private static class CleanerPatch
    {
        private static void Postfix(Cleaner __instance) => I.Guard("Cleaner.AssignProperty", () =>
            __instance.configuration.Bins.MaxItems = I._cleanerBins.Value);
    }
}

using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tools;
using Il2CppScheduleOne.UI.Management;
using ModSchedule1.PhoneUI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModSchedule1.Features;

/// <summary>
/// Funcionários mais rápidos, salário ajustável, mais estações/vasos por funcionário, mais vagas por imóvel, mais rotas
/// por handler e salário pago sozinho pelo banco (no host: é ele que decide se o funcionário foi pago).
/// </summary>
public sealed class BetterEmployees : Feature<BetterEmployees>
{
    public override string Id => "BetterEmployees";
    public override string Name => "Better Employees";
    public override string Description => "Mais rápidos, salário pago sozinho pelo banco, mais vagas por imóvel, mais estações e rotas.";
    // Todos os PCs: limites de atribuição são checados só na UI de quem usa a prancheta; velocidade/salário valem no host.

    private const string SpeedLabel = "ModSchedule1";
    private Pref<float> _workSpeed = null!, _walkSpeed = null!, _wage = null!;
    private Pref<int> _botanistPots = null!, _chemistStations = null!, _handlerStations = null!, _cleanerBins = null!,
        _capacity = null!, _routes = null!;
    private Pref<bool> _autoPay = null!;
    private bool _dumpedRoutes;

    protected override void OnInit()
    {
        _workSpeed = Setting("WorkSpeed", 1.5f, "Velocidade de trabalho x N", "Regar, plantar, colher, embalar, operar estações (soma com efeitos de drogas)");
        _walkSpeed = Setting("WalkSpeed", 2.6f, "Velocidade de caminhada (m/s)", "Padrão do jogo: 1.8. 0 = não mexer", min: 0, max: 6);
        _wage = Setting("WageMultiplier", 1f, "Salário diário x N", "1 = normal, 0.5 = metade", min: 0, max: 3);
        _botanistPots = Setting("BotanistMaxPots", 12, "Botanist: vasos por funcionário", "Jogo: 8. Acima do que cabe na lista, os extras funcionam mas não aparecem nela");
        _chemistStations = Setting("ChemistMaxStations", 6, "Chemist: estações por funcionário", "Jogo: 4");
        _handlerStations = Setting("HandlerMaxStations", 5, "Handler: estações por funcionário", "Jogo: 3");
        _cleanerBins = Setting("CleanerMaxBins", 8, "Cleaner: lixeiras por funcionário", "Jogo: 6");
        _autoPay = Setting("AutoPay", true, "Pagar salário pelo banco", "Sem dinheiro no armário, o host paga o salário do dia pelo banco");
        _capacity = Setting("CapacityMultiplier", 2, "Vagas de funcionários por imóvel x",
            "1 = jogo. Atenção: tirar o mod ou baixar isso faz o jogo descartar quem passar do limite", min: 1, max: 5);
        _routes = Setting("HandlerMaxRoutes", 10, "Handler: rotas por funcionário", "Jogo: 5", min: 1, max: 20);
    }

    // O jogo paga no UpdateBehaviour (todo minuto, no servidor) tirando o salário do armário. Antes dele, sem dinheiro
    // no armário, pagamos pelo banco: o funcionário nem chega a parar.
    [HarmonyPatch(typeof(Employee), nameof(Employee.UpdateBehaviour))]
    private static class AutoPayPatch
    {
        private static void Prefix(Employee __instance)
        {
            if (!I.Active || !I._autoPay.Value || !Net.IsHost) return;
            if (__instance.Fired || __instance.PaidForToday || __instance.DailyWage <= 0 || __instance.GetHome() == null) return;
            if (!TimeManager.InstanceExists || TimeManager.Instance.IsEndOfDay || __instance.IsPayAvailable()) return;   // dinheiro no armário: jogo paga
            I.Guard("salário automático", () =>
            {
                float wage = __instance.DailyWage;
                if (Bank.Online < wage) return;   // sem saldo: segue o jogo (pede dinheiro no armário)
                MoneyManager.Instance.CreateOnlineTransaction($"Salário: {__instance.FullName}", -wage, 1f, "ModSchedule1");
                __instance.SetIsPaid();
                I.Log.Info($"salário de {__instance.FullName} pago pelo banco: {Ui.Money(wage)}");
            });
        }
    }

    // Vagas por imóvel: precisa ser igual no host e nos clientes (os dois checam). Cada vaga precisa de um ponto de
    // espera; os extras são cópias dos existentes, um pouco deslocadas.
    [HarmonyPatch(typeof(Property), nameof(Property.Awake))]
    private static class CapacityPatch
    {
        private static void Prefix(Property __instance) => I.Guard("Property.Awake", () =>
        {
            int mult = Math.Max(1, I._capacity.Value);
            if (mult == 1 || __instance.EmployeeCapacity <= 0) return;
            int old = __instance.EmployeeCapacity, cap = old * mult;
            var points = __instance.EmployeeIdlePoints;
            if (points == null || points.Length == 0) return;
            if (points.Length < cap)
            {
                var grown = new Il2CppReferenceArray<Transform>(cap);
                for (int i = 0; i < cap; i++)
                {
                    if (i < points.Length) { grown[i] = points[i]; continue; }
                    var src = points[i % points.Length];
                    var copy = Object.Instantiate(src.gameObject, src.parent, true).transform;
                    copy.position = src.position + src.right * (0.9f * (i / points.Length));
                    copy.name = src.name + "_MS1";
                    grown[i] = copy;
                }
                __instance.EmployeeIdlePoints = grown;
            }
            __instance.EmployeeCapacity = cap;
            I.Log.Debug($"{__instance.PropertyName}: vagas {old} -> {cap}");
        });
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
        {
            __instance.configuration.Stations.MaxItems = I._handlerStations.Value;
            __instance.configuration.Routes.MaxRoutes = I._routes.Value;
        });
    }

    // A tela de rotas tem um número fixo de linhas no prefab: sem linhas a mais, as rotas extras ficariam invisíveis.
    [HarmonyPatch(typeof(RouteListFieldUI), nameof(RouteListFieldUI.Start))]
    private static class RouteUiPatch
    {
        private static void Prefix(RouteListFieldUI __instance) => I.Guard("RouteListFieldUI.Start", () =>
        {
            var entries = __instance.RouteEntries;
            if (entries == null || entries.Length == 0 || entries.Length >= I._routes.Value) return;
            NativeUi.DumpOnce(ref I._dumpedRoutes, entries[0].transform.parent, "lista de rotas do handler", 2);
            var grown = new Il2CppReferenceArray<RouteEntryUI>(I._routes.Value);
            for (int i = 0; i < grown.Length; i++)
                grown[i] = i < entries.Length ? entries[i]
                    : Object.Instantiate(entries[0].gameObject, entries[0].transform.parent, false).GetComponent<RouteEntryUI>();
            __instance.RouteEntries = grown;
        });
    }

    [HarmonyPatch(typeof(Cleaner), nameof(Cleaner.AssignProperty))]
    private static class CleanerPatch
    {
        private static void Postfix(Cleaner __instance) => I.Guard("Cleaner.AssignProperty", () =>
            __instance.configuration.Bins.MaxItems = I._cleanerBins.Value);
    }
}

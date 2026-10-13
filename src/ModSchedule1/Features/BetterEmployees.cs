using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tools;
using Il2CppScheduleOne.UI.Management;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;
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
        _capacity = null!, _capacityMin = null!, _routes = null!;
    private Pref<bool> _autoPay = null!;
    private bool _dumpedRoutes;
    private static float _routeScrollY;   // escolher origem/destino recria a prancheta: volta para onde estava
    // vagas originais por imóvel (PropertyCode), lidas na 1ª vez que o vemos: cada cena recria os imóveis com o valor do
    // jogo e só o mod mexe nele
    private readonly Dictionary<string, int> _baseCapacity = new();

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
            "1 = jogo. Baixar não demite ninguém (o imóvel fica cheio até alguém sair); tirar o mod sim: o jogo descarta quem passar do limite", min: 1, max: 5);
        _capacityMin = Setting("CapacityMin", 0, "Vagas por imóvel (mínimo fixo)",
            "0 = só o multiplicador. Ex.: 20 = todo imóvel que aceita funcionários fica com pelo menos 20 vagas. Muda na hora", min: 0, max: 50);
        _routes = Setting("HandlerMaxRoutes", 10, "Handler: rotas por funcionário", "Jogo: 5", min: 1, max: 20);
        Enabled.Changed += ApplyCapacity;
        _capacity.Changed += ApplyCapacity;
        _capacityMin.Changed += ApplyCapacity;
    }

    // Enabled e não Active: um erro em outra parte da feature (Faulted) não pode encolher as vagas e perder funcionários
    private int Capacity(int original) => Enabled.Value ? Math.Max(original * Math.Max(1, _capacity.Value), _capacityMin.Value) : original;

    // O Awake dos imóveis não serve: Bungalow, Sweatshop, Manor etc. têm Awake próprio e a chamada ao base foi
    // embutida no IL2CPP (o patch só pegava nos imóveis da classe base). A cena carrega antes do save.
    public override void OnSceneLoaded(string scene) { if (scene == "Main") ApplyCapacity(); }

    /// <summary>Config mudou (ou chegou a do host): aplica na hora em todo imóvel carregado.</summary>
    private void ApplyCapacity()
    {
        var all = Property.Properties;
        if (all == null) return;
        for (int i = 0; i < all.Count; i++) if (all[i] != null) ApplyCapacity(all[i]);
    }

    /// <summary>Vagas = original x multiplicador (ou o mínimo fixo), nunca abaixo de quem já trabalha lá. Precisa ser igual no host e nos clientes (os dois checam).</summary>
    private void ApplyCapacity(Property p)
    {
        try
        {
            if (!_baseCapacity.TryGetValue(p.PropertyCode, out int original)) _baseCapacity[p.PropertyCode] = original = p.EmployeeCapacity;
            if (original <= 0) return;
            int cap = Math.Max(Capacity(original), p.Employees != null ? p.Employees.Count : 0);
            if (cap == p.EmployeeCapacity || !GrowIdlePoints(p, cap)) return;
            p.EmployeeCapacity = cap;
            Log.Debug($"{p.PropertyName}: vagas {original} -> {cap}");
        }
        catch (Exception e) { Log.Error($"vagas de {p.PropertyName}: {e}"); }
    }

    /// <summary>
    /// Cada vaga precisa de um ponto de espera; os extras são cópias dos originais em fileiras de 5 ao lado e atrás de
    /// cada um. Só cresce: funcionários já contratados guardam o ponto deles.
    /// ponytail: grade fixa de 0,9 m sem olhar o navmesh; com muitas vagas por ponto original a fila pode atravessar
    /// parede (aí: NavMesh.SamplePosition).
    /// </summary>
    private static bool GrowIdlePoints(Property p, int cap)
    {
        var points = p.EmployeeIdlePoints;
        if (points == null || points.Length == 0) return false;
        if (points.Length >= cap) return true;
        int originals = 0;
        while (originals < points.Length && !points[originals].name.EndsWith("_MS1")) originals++;
        if (originals == 0) originals = points.Length;
        var grown = new Il2CppReferenceArray<Transform>(cap);
        for (int i = 0; i < cap; i++)
        {
            if (i < points.Length) { grown[i] = points[i]; continue; }
            var src = points[i % originals];
            var copy = Object.Instantiate(src.gameObject, src.parent, true).transform;
            int k = i / originals;   // 1ª cópia de cada original = 1, ...
            copy.position = src.position + src.right * (0.9f * (k % 5)) - src.forward * (0.9f * (k / 5));
            copy.name = src.name + "_MS1";
            grown[i] = copy;
        }
        p.EmployeeIdlePoints = grown;
        return true;
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

    // Checagem de vagas do jogo (host: contratar e carregar o save). Carregando, ninguém é descartado: imóvel já cheio
    // (vagas baixadas, feature desligada) ganha mais uma vaga. Roda mesmo desligado: é o que evita perder funcionários.
    [HarmonyPatch(typeof(EmployeeManager), nameof(EmployeeManager.CreateEmployee_Server))]
    private static class CreateEmployeePatch
    {
        private static void Prefix(Property property)
        {
            if (property == null) return;
            try
            {
                // a tela de contratação já aprovou com o valor atual: aplicar a config aqui só pode aumentar (baixar faria o
                // jogo cobrar a contratação e recusar o funcionário)
                int shown = property.EmployeeCapacity;
                I.ApplyCapacity(property);
                if (property.EmployeeCapacity < shown) property.EmployeeCapacity = shown;
                if (property.Employees.Count < property.EmployeeCapacity || !LoadManager.InstanceExists || !LoadManager.Instance.IsLoading) return;
                int need = property.Employees.Count + 1;
                if (!GrowIdlePoints(property, need)) return;
                property.EmployeeCapacity = need;
                I.Log.Warn($"{property.PropertyName}: o save tem mais funcionários que vagas; vaga {need} aberta para ninguém ser demitido");
            }
            catch (Exception e) { I.Log.Error($"CreateEmployee_Server: {e}"); }
        }
    }

    // O jogo lê EmployeeIdlePoints[índice] sem checar o tamanho. Num cliente com menos vagas que os funcionários do host
    // (config mudou no meio da sessão), garante o ponto de espera e a vaga antes. Roda mesmo desligado.
    [HarmonyPatch(typeof(Employee), nameof(Employee.AssignProperty))]
    private static class AssignPropertyPatch
    {
        private static void Prefix(Property prop)
        {
            if (prop == null) return;
            try
            {
                I._baseCapacity.TryAdd(prop.PropertyCode, prop.EmployeeCapacity);   // original antes de mexer
                int need = (prop.Employees != null ? prop.Employees.Count : 0) + 1;
                if (prop.EmployeeIdlePoints != null && prop.EmployeeIdlePoints.Length >= need) return;
                if (GrowIdlePoints(prop, need) && prop.EmployeeCapacity < need) prop.EmployeeCapacity = need;
            }
            catch (Exception e) { I.Log.Error($"AssignProperty: {e}"); }
        }
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
    // As linhas vão para uma lista rolável do tamanho da original (a prancheta não cresce). O jogo chama Bind (e o
    // primeiro Refresh) antes do Start, então refazemos o Refresh com as linhas novas.
    [HarmonyPatch(typeof(RouteListFieldUI), nameof(RouteListFieldUI.Start))]
    private static class RouteUiPatch
    {
        private static void Prefix(RouteListFieldUI __instance) => I.Guard("RouteListFieldUI.Start", () =>
        {
            var entries = __instance.RouteEntries;
            if (entries == null || entries.Length == 0 || entries.Length >= I._routes.Value) return;
            NativeUi.DumpOnce(ref I._dumpedRoutes, entries[0].transform.parent, "lista de rotas do handler", 2);
            Transform content;
            try { content = RouteScroll(__instance, entries); }
            catch (Exception e)   // layout do prefab inesperado: sem rolagem, mas com as linhas (como antes)
            {
                I.Log.Error($"lista rolável de rotas: {e}");
                content = entries[0].transform.parent;
            }
            var grown = new Il2CppReferenceArray<RouteEntryUI>(I._routes.Value);
            for (int i = 0; i < grown.Length; i++)
                grown[i] = i < entries.Length ? entries[i]
                    : Object.Instantiate(entries[0].gameObject, content, false).GetComponent<RouteEntryUI>();
            __instance.RouteEntries = grown;
            var fields = __instance.Fields;
            if (fields != null && fields.Count > 0 && fields[0] != null) __instance.Refresh(fields[0].Routes);
        });

        // Depois do Start (que liga o botão ao AddClicked): a rota nova aparece no fim, então rola até lá. A posição fica
        // guardada porque escolher origem/destino fecha e recria a prancheta.
        private static void Postfix(RouteListFieldUI __instance) => I.Guard("RouteListFieldUI.Start (rolagem)", () =>
        {
            var entries = __instance.RouteEntries;
            ScrollRect? scroll = null;   // GetComponentInParent<T>(bool) quebra no IL2CPP: sobe na mão
            for (var t = entries != null && entries.Length > 0 ? entries[0].transform.parent : null; t != null && scroll == null; t = t.parent)
                if (t.name == "MS1_RouteScroll") scroll = t.GetComponentInChildren<ScrollRect>();
            if (scroll == null || __instance.AddButton == null) return;
            if (ManagementClipboard.InstanceExists && ManagementClipboard.Instance.StatePreserved)
                scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, _routeScrollY);
            scroll.onValueChanged.AddListener((UnityEngine.Events.UnityAction<Vector2>)new Action<Vector2>(_ =>
            {
                if (scroll != null) _routeScrollY = scroll.content.anchoredPosition.y;
            }));
            __instance.AddButton.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                if (scroll == null) return;
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
            }));
        });
    }

    /// <summary>
    /// Troca as linhas de rota por uma lista rolável no mesmo lugar e da mesma altura. Funciona com o pai em layout
    /// automático ou posições fixas. O botão "+" fica onde está; se ele vem logo abaixo das linhas, a lista mostra uma
    /// linha a menos para o total caber como antes.
    /// </summary>
    private static Transform RouteScroll(RouteListFieldUI ui, Il2CppReferenceArray<RouteEntryUI> entries)
    {
        var first = entries[0].GetComponent<RectTransform>();
        var list = first.parent;
        bool addInList = ui.AddButton != null && ui.AddButton.transform.parent == list;
        float rowH = first.rect.height > 1f ? first.rect.height : LayoutUtility.GetPreferredHeight(first);
        var vlg = list.GetComponent<VerticalLayoutGroup>();
        float gap = vlg != null ? vlg.spacing
            : entries.Length > 1 ? Mathf.Max(0f, Mathf.Abs(first.localPosition.y - entries[1].transform.localPosition.y) - rowH) : 0f;
        int rows = Math.Max(1, entries.Length - (addInList ? 1 : 0));
        float height = rows * rowH + (rows - 1) * gap;

        var slot = Ui.Child("MS1_RouteScroll", list).GetComponent<RectTransform>();
        slot.SetSiblingIndex(first.GetSiblingIndex());
        slot.pivot = new Vector2(0f, 1f);
        slot.sizeDelta = new Vector2(first.rect.width, height);
        if (list.GetComponent<LayoutGroup>() != null)
        {
            var le = slot.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
            le.flexibleHeight = 0f;
        }
        else slot.localPosition = first.localPosition + new Vector3(first.rect.xMin, first.rect.yMax, 0f);   // canto sup. esq. da 1ª linha

        var content = Ui.ScrollList(slot, 0f);
        var cv = content.GetComponent<VerticalLayoutGroup>();
        cv.padding = new RectOffset(0, 0, 0, 0);
        cv.spacing = gap;
        content.parent.parent.GetComponent<ScrollRect>().movementType = ScrollRect.MovementType.Clamped;
        // altura fixa por linha (as âncoras do prefab podem ser esticadas; o layout precisa de um tamanho real)
        for (int i = 0; i < entries.Length; i++)
        {
            var le = entries[i].GetComponent<LayoutElement>();
            if (le == null) le = entries[i].gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = rowH;
            entries[i].transform.SetParent(content, false);
        }
        return content;
    }

    [HarmonyPatch(typeof(Cleaner), nameof(Cleaner.AssignProperty))]
    private static class CleanerPatch
    {
        private static void Postfix(Cleaner __instance) => I.Guard("Cleaner.AssignProperty", () =>
            __instance.configuration.Bins.MaxItems = I._cleanerBins.Value);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.UI.MainMenu;
using Il2CppScheduleOne.UI.Settings;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModSchedule1.PhoneUI;

/// <summary>
/// Aba "Mods" dentro de Configurações (menu principal e pausa). Linhas são clones das linhas nativas de
/// liga/desliga e slider (mesma fonte e estilo), religadas às nossas Prefs. A SettingsScreen monta as abas a partir
/// de Categories no primeiro OnEnable, então basta acrescentar a nossa no Awake (a tela é desativada no Awake).
/// </summary>
internal static class ModsSettingsTab
{
    private static readonly Log Log = new("Menu");
    private const string TabName = "MS1_ModsTab";
    private static readonly List<Action> Refreshers = new();
    private static int _tabIndex = -1;
    private static bool _dumped;

    [HarmonyPatch(typeof(SettingsScreen), nameof(SettingsScreen.Awake))]
    internal static class AwakePatch
    {
        private static void Postfix(SettingsScreen __instance)
        {
            try { Build(__instance); }
            catch (Exception e) { Log.Error($"aba Mods não criada: {e}"); }
        }
    }

    [HarmonyPatch(typeof(SettingsScreen), nameof(SettingsScreen.ShowCategory))]
    internal static class ShowPatch
    {
        private static void Postfix(int index)
        {
            if (index == _tabIndex) Refresh();
        }
    }

    public static void Refresh()
    {
        foreach (var r in Refreshers)
        {
            try { r(); } catch (Exception e) { Log.Error($"refresh: {e.Message}"); }
        }
    }

    private static void Build(SettingsScreen screen)
    {
        var cats = screen.Categories;
        for (int i = 0; i < cats.Length; i++)
            if (cats[i].Toggle != null && cats[i].Toggle.name == TabName) return;

        if (!_dumped && Core.DebugLogging)
            for (int i = 0; i < cats.Length; i++)
                NativeUi.DumpOnce(ref _dumped, cats[i].Panel.transform, $"Settings categoria {i}", 5);

        var toggleTemplate = screen.GetComponentInChildren<SettingsToggle>(true);
        var sliderTemplate = screen.GetComponentInChildren<SettingsSlider>(true);
        if (toggleTemplate == null) { Log.Error("nenhuma linha de liga/desliga para usar de modelo"); return; }
        var toggleRow = RowRoot(toggleTemplate.transform);
        var sliderRow = sliderTemplate != null ? RowRoot(sliderTemplate.transform) : null;
        var font = toggleTemplate.GetComponentInChildren<TextMeshProUGUI>(true);

        // aba: clone da última aba existente
        var last = cats[cats.Length - 1];
        var tabGo = Object.Instantiate(last.Toggle.gameObject, last.Toggle.transform.parent, false);
        tabGo.name = TabName;
        var tab = tabGo.GetComponent<Toggle>();
        tab.onValueChanged = new Toggle.ToggleEvent();
        var tabLabel = tabGo.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tabLabel != null) tabLabel.text = "Mods";
        var tabSelectable = tabGo.GetComponent<UISelectable>();
        if (tabSelectable != null && screen.Tab != null) screen.Tab.AddSelectable(tabSelectable);

        // painel: clone do painel que já tem rolagem (muitas linhas); senão, rolagem nossa
        GameObject sourcePanel = last.Panel;
        for (int i = 0; i < cats.Length; i++)
            if (cats[i].Panel.GetComponentInChildren<ScrollRect>(true) != null) { sourcePanel = cats[i].Panel; break; }
        var panel = Object.Instantiate(sourcePanel, sourcePanel.transform.parent, false);
        panel.name = "MS1_ModsPanel";
        Transform content;
        var scroll = panel.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null && scroll.content != null) { content = scroll.content; Clear(content); }
        else { Clear(panel.transform); content = Ui.ScrollList(panel.transform, 0); }
        var uiPanel = panel.GetComponentInChildren<UIPanel>(true);
        if (uiPanel != null) uiPanel.ClearAllSelectables();

        Refreshers.Clear();
        Header(content, font, $"ModSchedule1 v{Core.Version}", 1.25f);
        Info(content, font, () => ConfigSync.Active
            ? "Multiplayer: as configs de jogo seguem as do host (marcadas como 'do host')."
            : "Configs de jogo (pilhas, estações, funcionários...) valem para todos quando você hospeda.");
        AddToggle(content, toggleRow, uiPanel, "Logs detalhados (debug)", () => Core.DebugLogging, Core.SetDebugLogging, () => true);

        foreach (var feature in Core.Features)
        {
            var f = feature;
            Header(content, font, f.Name + (f.Shared ? "" : "   (pessoal)"), 1.1f);
            if (f.Faulted) Info(content, font, () => "Desativada por erro nesta sessão (veja o log).");
            foreach (var pref in f.Prefs)
            {
                var p = pref;
                string label = p.Entry.Identifier == "Enabled" ? "Ativo" : p.Entry.DisplayName;
                Func<bool> editable = () => !p.Overridden;
                Func<string> suffix = () => p.Overridden ? "  (do host)" : "";
                switch (p)
                {
                    case Pref<bool> b:
                        AddToggle(content, toggleRow, uiPanel, label, () => b.Value, v => { b.Value = v; MelonPreferences.Save(); }, editable, suffix);
                        break;
                    case Pref<int> n when sliderRow != null:
                        AddSlider(content, sliderRow, uiPanel, label, p.Min, p.Max, true, () => n.Value,
                            v => { n.Value = Mathf.RoundToInt(v); MelonPreferences.Save(); }, editable, suffix);
                        break;
                    case Pref<float> fl when sliderRow != null:
                        AddSlider(content, sliderRow, uiPanel, label, p.Min, p.Max, false, () => fl.Value,
                            v => { fl.Value = (float)Math.Round(v, 2); MelonPreferences.Save(); }, editable, suffix);
                        break;
                    default:
                        Info(content, font, () => $"{label}: {Format(p.BoxedValue)}   (edite em UserData/ModSchedule1.cfg)");
                        break;
                }
            }
            foreach (var (actionLabel, run) in f.Actions)
                AddAction(content, toggleRow, uiPanel, actionLabel, run, () => f.Active);
        }

        var arr = new Il2CppReferenceArray<SettingsScreen.SettingsCategory>(cats.Length + 1);
        for (int i = 0; i < cats.Length; i++) arr[i] = cats[i];
        arr[cats.Length] = new SettingsScreen.SettingsCategory { Toggle = tab, Panel = panel };
        screen.Categories = arr;
        _tabIndex = cats.Length;
        panel.SetActive(false);
        Log.Info($"aba Mods criada ({Refreshers.Count} itens)");
    }

    /// <summary>Raiz da linha = ancestral cujo pai tem o LayoutGroup que empilha as linhas.</summary>
    private static Transform RowRoot(Transform t)
    {
        var r = t;
        for (int i = 0; i < 6 && r.parent != null; i++)
        {
            if (r.parent.GetComponent<LayoutGroup>() != null) return r;
            r = r.parent;
        }
        return t;
    }

    private static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static GameObject CloneRow(Transform template, Transform content, string name)
    {
        var row = Object.Instantiate(template.gameObject, content, false);
        row.name = name;
        row.SetActive(true);
        var le = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
        float h = template.GetComponent<RectTransform>().rect.height;
        if (h > 1) le.minHeight = le.preferredHeight = h;
        return row;
    }

    private static void Editable(GameObject row, bool on)
    {
        var cg = row.GetComponent<CanvasGroup>() ?? row.AddComponent<CanvasGroup>();
        cg.alpha = on ? 1f : 0.45f;
        cg.interactable = cg.blocksRaycasts = on;
    }

    private static void AddToggle(Transform content, Transform template, UIPanel? uiPanel, string label,
        Func<bool> get, Action<bool> set, Func<bool> editable, Func<string>? suffix = null)
    {
        var row = CloneRow(template, content, "MS1_Toggle");
        foreach (var st in row.GetComponentsInChildren<SettingsToggle>(true)) Object.DestroyImmediate(st);   // tira o vínculo com a config do jogo
        var ui = row.GetComponentInChildren<UIToggle>(true);
        if (ui == null) { Object.Destroy(row); return; }
        ui.OnChanged = new UnityEvent<bool>();
        ui.OnChanged.AddListener((UnityAction<bool>)new Action<bool>(v => { set(v); Refresh(); }));
        if (uiPanel != null && ui.selectable != null) uiPanel.AddSelectable(ui.selectable);
        Refreshers.Add(() =>
        {
            string text = label + (suffix?.Invoke() ?? "");
            ui.optionName = text;   // UIOption.Awake reescreve nameText com optionName
            if (ui.nameText != null) ui.nameText.text = text;
            ui.SetStateWithoutNotify(get());
            Editable(row, editable());
        });
    }

    /// <summary>Ação ("Limpar agora"): linha de liga/desliga que executa e volta ao estado normal.</summary>
    private static void AddAction(Transform content, Transform template, UIPanel? uiPanel, string label, Action run, Func<bool> enabled)
    {
        UIToggle? ui = null;
        AddToggle(content, template, uiPanel, label, () => false, _ =>
        {
            try { run(); } catch (Exception e) { Log.Error($"{label}: {e.Message}"); }
        }, enabled);
        var row = content.GetChild(content.childCount - 1).gameObject;
        ui = row.GetComponentInChildren<UIToggle>(true);
        Refreshers.Add(() => { if (ui != null && ui.buttonText != null) ui.buttonText.text = "Executar"; });
    }

    private static void AddSlider(Transform content, Transform template, UIPanel? uiPanel, string label, float min, float max,
        bool whole, Func<float> get, Action<float> set, Func<bool> editable, Func<string> suffix)
    {
        var row = CloneRow(template, content, "MS1_Slider");
        foreach (var ss in row.GetComponentsInChildren<SettingsSlider>(true)) Object.DestroyImmediate(ss);
        var slider = row.GetComponentInChildren<Slider>(true);
        if (slider == null) { Object.Destroy(row); return; }
        var ui = row.GetComponentInChildren<UISlider>(true);
        if (ui != null) ui.OnChanged = new UnityEvent<float>();

        // rótulo = primeiro texto fora do slider; valor = texto do UISlider ou o "Value" no handle (padrão do jogo)
        TextMeshProUGUI? name = null, value = ui != null ? ui.valueText : null;
        foreach (var t in row.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            bool inSlider = t.transform.IsChildOf(slider.transform);
            if (!inSlider && name == null && (ui == null || t != ui.valueText)) name = t;
            else if (inSlider && value == null) value = t;
        }

        slider.onValueChanged = new Slider.SliderEvent();
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;
        slider.onValueChanged.AddListener((UnityAction<float>)new Action<float>(v =>
        {
            set(v);
            if (value != null) value.text = Format(get());
        }));
        if (uiPanel != null && ui != null && ui.selectable != null) uiPanel.AddSelectable(ui.selectable);
        Refreshers.Add(() =>
        {
            string text = label + suffix();
            if (ui != null) ui.optionName = text;
            if (name != null) name.text = text;
            slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
            if (value != null) { value.text = Format(get()); value.gameObject.SetActive(true); }
            Editable(row, editable());
        });
    }

    private static void Header(Transform content, TextMeshProUGUI? style, string text, float scale)
    {
        var t = NewText(content, style, scale, 46);
        t.fontStyle = FontStyles.Bold;
        t.text = text;
    }

    private static void Info(Transform content, TextMeshProUGUI? style, Func<string> text)
    {
        var t = NewText(content, style, 0.8f, 30);
        t.color = new Color(t.color.r, t.color.g, t.color.b, 0.7f);
        Refreshers.Add(() => t.text = text());
    }

    private static TextMeshProUGUI NewText(Transform content, TextMeshProUGUI? style, float scale, float height)
    {
        var go = Ui.Child("MS1_Text", content);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (style != null)
        {
            t.font = style.font;
            t.fontSharedMaterial = style.fontSharedMaterial;
            t.color = style.color;
            t.fontSize = style.fontSize * scale;
        }
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = true;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = height;
        return t;
    }

    private static string Format(object? v) => v switch
    {
        float f => f.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "sim" : "não",
        string s when s.Length == 0 => "(vazio)",
        null => "-",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "-",
    };
}

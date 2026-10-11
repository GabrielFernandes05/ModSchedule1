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
/// Aba "Mods" dentro de Configurações + botão "Mods" no menu principal e no de pausa (abre direto na aba).
/// Linhas são clones das linhas nativas de liga/desliga e slider (mesma fonte e estilo), religadas às nossas Prefs.
/// Cada mod é uma linha liga/desliga; clicar no nome abre/fecha as opções dele.
/// A SettingsScreen monta as abas a partir de Categories no primeiro OnEnable, então basta acrescentar a nossa no Awake.
/// </summary>
internal static class ModsSettingsTab
{
    private static readonly Log Log = new("Menu");
    private const string TabName = "MS1_ModsTab", MenuButtonName = "MS1_ModsButton";
    private static readonly List<(GameObject Owner, Action Run)> Refreshers = new();
    private static readonly List<SettingsScreen> Screens = new();
    private static SettingsScreen? _pending;   // botão "Mods" do menu: abrir esta tela na aba Mods
    private static float _pendingAt;
    private static bool _selfOpened, _dumped, _dumpedBar, _dumpedMenu;

    [HarmonyPatch(typeof(SettingsScreen), nameof(SettingsScreen.Awake))]
    internal static class AwakePatch
    {
        private static void Postfix(SettingsScreen __instance)
        {
            try { Build(__instance); }
            catch (Exception e) { Log.Error($"aba Mods não criada: {e}"); }
            try { AddMenuButtons(__instance); }
            catch (Exception e) { Log.Error($"botão Mods do menu não criado: {e}"); }
        }
    }

    public static void Refresh()
    {
        Refreshers.RemoveAll(r => r.Owner == null);
        foreach (var (_, run) in Refreshers)
        {
            try { run(); } catch (Exception e) { Log.Error($"refresh: {e.Message}"); }
        }
    }

    /// <summary>Chamado todo frame: conclui o "abrir na aba Mods" depois que a tela de Configurações abriu.</summary>
    public static void Tick()
    {
        if (_pending == null) return;
        try { TickPending(); }
        catch (Exception e) { _pending = null; Log.Error($"abrir aba Mods: {e.Message}"); }
    }

    private static void TickPending()
    {
        if (_pending == null) return;
        float waited = Time.unscaledTime - _pendingAt;
        // qualquer tela de Configurações que abriu (o botão clonado pode abrir outra instância que não a _pending)
        Screens.RemoveAll(s => s == null);
        foreach (var screen in Screens)
        {
            if (!screen.gameObject.activeInHierarchy || !screen._initialized) continue;
            var tab = ModsToggle(screen);
            if (tab != null) tab.isOn = true;
            _pending = null;
            return;
        }
        if (waited > 0.3f && !_selfOpened)
        {
            // o clone do botão Settings deveria ter aberto a tela; se não abriu, abrimos nós
            _selfOpened = true;
            Log.Debug("tela de Configurações não abriu pelo botão clonado; abrindo direto");
            _pending.Open();
        }
        else if (waited > 3f) _pending = null;
    }

    private static Toggle? ModsToggle(SettingsScreen screen)
    {
        var cats = screen.Categories;
        for (int i = 0; i < cats.Length; i++)
            if (cats[i].Toggle != null && cats[i].Toggle.name == TabName) return cats[i].Toggle;
        return null;
    }

    private static void Build(SettingsScreen screen)
    {
        if (ModsToggle(screen) != null) return;
        var cats = screen.Categories;

        if (!_dumped && Core.DebugLogging)
            for (int i = 0; i < cats.Length; i++)
                NativeUi.DumpOnce(ref _dumped, cats[i].Panel.transform, $"Settings categoria {i}", 5);

        var toggleTemplate = screen.GetComponentInChildren<SettingsToggle>(true);
        var sliderTemplate = screen.GetComponentInChildren<SettingsSlider>(true);
        if (toggleTemplate == null) { Log.Error("nenhuma linha de liga/desliga para usar de modelo"); return; }
        var toggleRow = RowRoot(toggleTemplate.transform);
        var sliderRow = sliderTemplate != null ? RowRoot(sliderTemplate.transform) : null;
        var font = toggleTemplate.GetComponentInChildren<TextMeshProUGUI>(true);

        // aba: clone da última aba. A barra pode ter um wrapper por aba: clonamos a "raiz" da aba (filho direto da barra).
        var roots = new List<RectTransform>();
        for (int i = 0; i < cats.Length; i++) roots.Add(TabRoot(cats[i].Toggle.transform).GetComponent<RectTransform>());
        var bar = roots[roots.Count - 1].parent;
        NativeUi.DumpOnce(ref _dumpedBar, bar, "barra de abas das Configurações", 3);
        var tabGo = Object.Instantiate(roots[roots.Count - 1].gameObject, bar, false);
        tabGo.name = TabName;
        var tab = tabGo.GetComponentInChildren<Toggle>(true);
        tab.gameObject.name = TabName;
        tab.onValueChanged = new Toggle.ToggleEvent();
        // ShowCategory é inlinado no IL2CPP (patch nele não roda): atualizamos pelo próprio toggle da aba
        tab.onValueChanged.AddListener((UnityAction<bool>)new Action<bool>(on => { if (on) Refresh(); }));
        tab.isOn = false;
        foreach (var label in tabGo.GetComponentsInChildren<TextMeshProUGUI>(true)) label.text = "Mods";
        var tabSelectable = tab.GetComponent<UISelectable>();
        if (tabSelectable != null && screen.Tab != null) screen.Tab.AddSelectable(tabSelectable);
        if (bar.GetComponent<LayoutGroup>() == null) FitTabs(roots, tabGo.GetComponent<RectTransform>());

        // painel: clone do painel que já tem rolagem (muitas linhas); senão, rolagem nossa
        GameObject sourcePanel = cats[cats.Length - 1].Panel;
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

        // A lista nativa não controla o tamanho dos filhos (linhas têm tamanho fixo): textos novos precisam de largura.
        float width = content.parent != null ? content.parent.GetComponent<RectTransform>().rect.width - 40 : 0;
        if (width < 300) width = 600;
        var b = new Builder(content, toggleRow, sliderRow, uiPanel, font, width);

        b.Title($"ModSchedule1 v{Core.Version}");
        b.Info(() => ConfigSync.Active
            ? "Multiplayer: configs de jogo seguem as do host (marcadas \"do host\"). Clique no nome de um mod para ver as opções."
            : "Clique no nome de um mod para ver as opções. Configs de jogo valem para todos quando você hospeda.");
        b.Toggle(() => "Logs detalhados (debug)", () => Core.DebugLogging, Core.SetDebugLogging, () => true);

        foreach (var feature in Core.Features)
        {
            var f = feature;
            var section = new List<GameObject>();
            bool open = false;
            Func<string> suffix = () => f.Enabled.Overridden ? "  (do host)" : "";
            var head = b.Toggle(() => $"{f.Name}{(f.Faulted ? "  <color=#ff7070>(erro)</color>" : "")}{suffix()}  <alpha=#80>{(open ? "[-]" : "[+]")}",
                () => f.Enabled.Value, v => { f.Enabled.Value = v; MelonPreferences.Save(); }, () => !f.Enabled.Overridden);

            section.Add(b.Info(() => f.Description + (f.Faulted ? " Desativado por erro nesta sessão (veja o log)." : "")
                + (f.Shared ? " Config do host no multiplayer." : " Config pessoal (cada jogador escolhe)."), indent: true));
            foreach (var pref in f.Prefs)
            {
                var p = pref;
                if (p == f.Enabled) continue;
                Func<string> label = () => "    " + p.Entry.DisplayName + (p.Overridden ? "  (do host)" : "");
                Func<bool> editable = () => !p.Overridden;
                GameObject? row = p switch
                {
                    Pref<bool> bp => b.Toggle(label, () => bp.Value, v => { bp.Value = v; MelonPreferences.Save(); }, editable),
                    Pref<int> n when sliderRow != null => b.Slider(label, p.Min, p.Max, true, () => n.Value,
                        v => { n.Value = Mathf.RoundToInt(v); MelonPreferences.Save(); }, editable),
                    Pref<float> fl when sliderRow != null => b.Slider(label, p.Min, p.Max, false, () => fl.Value,
                        v => { fl.Value = (float)Math.Round(v, 2); MelonPreferences.Save(); }, editable),
                    _ => b.Info(() => $"{p.Entry.DisplayName}: {Format(p.BoxedValue)}   (edite em UserData/ModSchedule1.cfg)", indent: true),
                };
                if (row != null) section.Add(row);
            }
            foreach (var (actionLabel, run) in f.Actions)
            {
                var row = b.Action("    " + actionLabel, run, () => f.Active);
                if (row != null) section.Add(row);
            }

            foreach (var go in section) go.SetActive(false);
            // ponytail: abrir/fechar só pelo mouse (clique no nome); no controle as opções ficam na config
            if (head != null) b.Clickable(head, () =>
            {
                open = !open;
                foreach (var go in section) go.SetActive(open);
                Refresh();
            });
        }

        var arr = new Il2CppReferenceArray<SettingsScreen.SettingsCategory>(cats.Length + 1);
        for (int i = 0; i < cats.Length; i++) arr[i] = cats[i];
        arr[cats.Length] = new SettingsScreen.SettingsCategory { Toggle = tab, Panel = panel };
        screen.Categories = arr;
        panel.SetActive(false);
        Screens.RemoveAll(s => s == null);
        Screens.Add(screen);
        Refresh();   // grava os textos antes do Awake das linhas (UIOption.Awake copia optionName para o rótulo)
        Log.Info($"aba Mods criada ({b.Rows} linhas)");
    }

    /// <summary>
    /// Botão "Mods" logo abaixo de "Settings" nos menus (principal e pausa): clone do próprio botão Settings, que
    /// continua abrindo as Configurações; o Tick troca para a aba Mods quando a tela abre.
    /// </summary>
    private static void AddMenuButtons(SettingsScreen screen)
    {
        int added = 0;
        foreach (var sel in screen.transform.root.GetComponentsInChildren<Selectable>(true))
        {
            if (sel.transform.IsChildOf(screen.transform)) continue;
            var label = sel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null || !IsSettings(label.text)) continue;
            var parent = sel.transform.parent;
            if (parent == null || parent.Find(MenuButtonName) != null) continue;
            NativeUi.DumpOnce(ref _dumpedMenu, parent, "menu com o botão Settings", 2);

            var go = Object.Instantiate(sel.gameObject, parent, false);
            go.name = MenuButtonName;
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (IsSettings(t.text)) t.text = "Mods";
            Action open = () => { _pending = screen; _pendingAt = Time.unscaledTime; _selfOpened = false; };
            go.GetComponent<Button>()?.onClick.AddListener((UnityAction)open);
            go.GetComponent<UITrigger>()?.OnTrigger.AddListener((UnityAction)open);
            var selectable = go.GetComponent<UISelectable>();
            var menuPanel = sel.GetComponentInParent<UIPanel>(true);
            if (selectable != null && menuPanel != null) menuPanel.AddSelectable(selectable);

            var rt = go.GetComponent<RectTransform>();
            var srt = sel.GetComponent<RectTransform>();
            if (parent.GetComponent<LayoutGroup>() != null) go.transform.SetSiblingIndex(sel.transform.GetSiblingIndex() + 1);
            else
            {
                // sem layout automático: abre espaço logo abaixo do Settings, empurrando os de baixo
                float step = srt.rect.height + 8f;
                for (int i = 0; i < parent.childCount; i++)
                {
                    var c = parent.GetChild(i).GetComponent<RectTransform>();
                    if (c != null && c != rt && c.anchoredPosition.y < srt.anchoredPosition.y - 1f)
                        c.anchoredPosition -= new Vector2(0, step);
                }
                rt.anchoredPosition = srt.anchoredPosition - new Vector2(0, step);
            }
            added++;
            Log.Debug($"botão Mods criado em {parent.name}");
        }
        if (added == 0) Log.Debug($"nenhum botão Settings fora da tela de Configurações em {screen.transform.root.name}");
    }

    private static bool IsSettings(string? text) => string.Equals(text?.Trim(), "Settings", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sobe por wrappers de filho único até o elemento que é filho direto da barra de abas.</summary>
    private static Transform TabRoot(Transform toggle)
    {
        var r = toggle;
        for (int i = 0; i < 3 && r.parent != null && r.parent.childCount == 1; i++) r = r.parent;
        return r;
    }

    /// <summary>
    /// Barra sem layout automático: o clone nasceria em cima da última aba. Redistribui as N+1 abas no mesmo espaço
    /// que as N ocupavam (mesmo eixo, largura proporcional), então a barra não estoura para fora da tela.
    /// </summary>
    private static void FitTabs(List<RectTransform> tabs, RectTransform ours)
    {
        int n = tabs.Count;
        if (n < 2) { ours.anchoredPosition = tabs[0].anchoredPosition + new Vector2(tabs[0].rect.width + 10, 0); return; }
        Vector2 first = tabs[0].anchoredPosition, lastPos = tabs[n - 1].anchoredPosition;
        bool horizontal = Mathf.Abs(lastPos.x - first.x) >= Mathf.Abs(lastPos.y - first.y);
        Vector2 step = (lastPos - first) / n;   // n+1 abas no vão que antes tinha n-1 intervalos
        float scale = n / (float)(n + 1);
        var all = new List<RectTransform>(tabs) { ours };
        for (int i = 0; i < all.Count; i++)
        {
            all[i].anchoredPosition = first + step * i;
            var size = tabs[n - 1].sizeDelta;
            all[i].sizeDelta = horizontal ? new Vector2(size.x * scale, size.y) : new Vector2(size.x, size.y * scale);
        }
        Log.Debug($"abas redistribuídas ({(horizontal ? "horizontal" : "vertical")}): {n} -> {n + 1}");
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

    private static string Format(object? v) => v switch
    {
        float f => f.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "sim" : "não",
        string s when s.Length == 0 => "(vazio)",
        null => "-",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "-",
    };

    /// <summary>Monta as linhas do painel. Cada linha registra um refresher (rótulo, valor, editável).</summary>
    private sealed class Builder
    {
        private readonly Transform _content, _toggleRow;
        private readonly Transform? _sliderRow;
        private readonly UIPanel? _uiPanel;
        private readonly TextMeshProUGUI? _font;
        private readonly float _width;
        public int Rows;

        public Builder(Transform content, Transform toggleRow, Transform? sliderRow, UIPanel? uiPanel, TextMeshProUGUI? font, float width)
        {
            _content = content; _toggleRow = toggleRow; _sliderRow = sliderRow; _uiPanel = uiPanel; _font = font; _width = width;
        }

        private void Add(GameObject owner, Action run) { Refreshers.Add((owner, run)); Rows++; }

        private GameObject CloneRow(Transform template, string name)
        {
            var row = Object.Instantiate(template.gameObject, _content, false);
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

        /// <summary>Rótulo nativo em uma linha só, encolhendo a fonte se o texto for longo.</summary>
        private static void FitLabel(TextMeshProUGUI? t)
        {
            if (t == null) return;
            t.richText = true;
            t.enableWordWrapping = false;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Min(12f, t.fontSize);
            t.enableAutoSizing = true;
        }

        public GameObject? Toggle(Func<string> label, Func<bool> get, Action<bool> set, Func<bool> editable)
        {
            var row = CloneRow(_toggleRow, "MS1_Toggle");
            foreach (var st in row.GetComponentsInChildren<SettingsToggle>(true)) Object.DestroyImmediate(st);   // tira o vínculo com a config do jogo
            var ui = row.GetComponentInChildren<UIToggle>(true);
            if (ui == null) { Object.Destroy(row); return null; }
            ui.OnChanged = new UnityEvent<bool>();
            ui.OnChanged.AddListener((UnityAction<bool>)new Action<bool>(v => { set(v); Refresh(); }));
            if (_uiPanel != null && ui.selectable != null) _uiPanel.AddSelectable(ui.selectable);
            FitLabel(ui.nameText);
            Add(row, () =>
            {
                string text = label();
                ui.optionName = text;   // UIOption.Awake reescreve nameText com optionName
                if (ui.nameText != null) ui.nameText.text = text;
                ui.SetStateWithoutNotify(get());
                Editable(row, editable());
            });
            return row;
        }

        /// <summary>Clicar no rótulo da linha executa <paramref name="onClick"/> (o resto da linha continua liga/desliga).</summary>
        public void Clickable(GameObject row, Action onClick)
        {
            var name = row.GetComponentInChildren<UIToggle>(true)?.nameText;
            if (name == null) return;
            name.raycastTarget = true;
            var button = name.GetComponent<Button>() ?? name.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener((UnityAction)onClick);
        }

        /// <summary>Ação ("Limpar agora"): linha de liga/desliga que executa e volta ao estado normal.</summary>
        public GameObject? Action(string label, Action run, Func<bool> enabled)
        {
            var row = Toggle(() => label, () => false, _ =>
            {
                try { run(); } catch (Exception e) { Log.Error($"{label.Trim()}: {e.Message}"); }
            }, enabled);
            var ui = row != null ? row.GetComponentInChildren<UIToggle>(true) : null;
            if (row != null) Add(row, () => { if (ui != null && ui.buttonText != null) ui.buttonText.text = "Executar"; });
            return row;
        }

        public GameObject? Slider(Func<string> label, float min, float max, bool whole, Func<float> get, Action<float> set, Func<bool> editable)
        {
            if (_sliderRow == null) return null;
            var row = CloneRow(_sliderRow, "MS1_Slider");
            foreach (var ss in row.GetComponentsInChildren<SettingsSlider>(true)) Object.DestroyImmediate(ss);
            var slider = row.GetComponentInChildren<Slider>(true);
            if (slider == null) { Object.Destroy(row); return null; }
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
            FitLabel(name);

            slider.onValueChanged = new UnityEngine.UI.Slider.SliderEvent();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.onValueChanged.AddListener((UnityAction<float>)new Action<float>(v =>
            {
                set(v);
                if (value != null) value.text = Format(get());
            }));
            if (_uiPanel != null && ui != null && ui.selectable != null) _uiPanel.AddSelectable(ui.selectable);
            Add(row, () =>
            {
                string text = label();
                if (ui != null) ui.optionName = text;
                if (name != null) name.text = text;
                slider.SetValueWithoutNotify(Mathf.Clamp(get(), min, max));
                if (value != null) { value.text = Format(get()); value.gameObject.SetActive(true); }
                Editable(row, editable());
            });
            return row;
        }

        public void Title(string text)
        {
            var t = NewText(1.15f);
            t.fontStyle = FontStyles.Bold;
            t.text = text;
            Fit(t);
        }

        public GameObject Info(Func<string> text, bool indent = false)
        {
            var t = NewText(0.8f);
            t.color = new Color(t.color.r, t.color.g, t.color.b, 0.7f);
            if (indent) t.margin = new Vector4(28, 0, 0, 0);
            Add(t.gameObject, () => { t.text = text(); Fit(t); });
            return t.gameObject;
        }

        private TextMeshProUGUI NewText(float scale)
        {
            var go = Ui.Child("MS1_Text", _content);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                t.font = _font.font;
                t.fontSharedMaterial = _font.fontSharedMaterial;
                t.color = _font.color;
                t.fontSize = _font.fontSize * scale;
            }
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            t.margin = new Vector4(10, 0, 0, 0);
            go.AddComponent<LayoutElement>();
            return t;
        }

        /// <summary>Altura pelo texto (quebra em várias linhas): a lista nativa usa o tamanho do próprio filho.</summary>
        private void Fit(TextMeshProUGUI t)
        {
            float h = Mathf.Max(30f, t.GetPreferredValues(t.text, _width - t.margin.x, 0).y + 10f);
            t.GetComponent<RectTransform>().sizeDelta = new Vector2(_width, h);
            var le = t.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = h;
        }
    }
}

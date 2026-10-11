using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.PhoneUI;

/// <summary>App "ModSchedule1" no celular: liga/desliga cada mod em jogo (substitui o Mod Manager - Phone App).</summary>
public sealed class ControlPanelApp : PhoneApp
{
    public override string AppName => "ModSchedule1Panel";
    public override string IconLabel => "Mods";

    private static readonly Color On = new(0.18f, 0.6f, 0.3f), Off = new(0.35f, 0.35f, 0.38f), Err = new(0.7f, 0.2f, 0.2f);
    private readonly List<System.Action> _refreshers = new();

    protected override void BuildUI(GameObject container)
    {
        _refreshers.Clear();
        var header = Ui.Child("Header", container.transform);
        var hrt = header.GetComponent<RectTransform>();
        hrt.anchorMin = new Vector2(0, 1);
        hrt.anchorMax = new Vector2(1, 1);
        hrt.pivot = new Vector2(0.5f, 1);
        hrt.sizeDelta = new Vector2(0, 60);
        hrt.anchoredPosition = Vector2.zero;
        var title = Ui.Text(header.transform, $"  ModSchedule1 v{Core.Version}", 26, bold: true);
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;

        var list = Ui.ScrollList(container.transform, topMargin: 64);

        foreach (var feature in Core.Features)
        {
            var f = feature;
            var row = Ui.Row(list);
            var label = Ui.Text(row.transform, "", 18);
            var settings = BuildSettings(list, f);
            if (settings != null)
                Ui.Button(row.transform, "Ajustes", Off, () => settings.SetActive(!settings.activeSelf), width: 90);
            var (_, btnText, btnBg) = Ui.Button(row.transform, "", Off, () =>
            {
                if (f.Faulted) return;
                f.EnabledEntry.Value = !f.EnabledEntry.Value;
                MelonPreferences.Save();
                Refresh();
            });
            _refreshers.Add(() =>
            {
                label.text = f.Name + (f.HostOnly && !SafeIsHost() ? "  <i>(só o host)</i>" : "");
                label.supportRichText = true;
                btnText.text = f.Faulted ? "ERRO (ver log)" : f.EnabledEntry.Value ? "ATIVO" : "DESATIVADO";
                btnBg.color = f.Faulted ? Err : f.EnabledEntry.Value ? On : Off;
            });
        }

        var debugRow = Ui.Row(list);
        Ui.Text(debugRow.transform, "Logs detalhados (debug)", 18);
        var (_, dText, dBg) = Ui.Button(debugRow.transform, "", Off, () =>
        {
            Core.SetDebugLogging(!Core.DebugLogging);
            Refresh();
        });
        _refreshers.Add(() =>
        {
            dText.text = Core.DebugLogging ? "ATIVO" : "DESATIVADO";
            dBg.color = Core.DebugLogging ? On : Off;
        });

        Ui.Text(list, "Ajustes: valores de cada mod. Textos e listas: UserData/ModSchedule1.cfg", 14, minHeight: 40).color = new Color(0.7f, 0.7f, 0.7f);
        Refresh();
    }

    /// <summary>
    /// Painel recolhível com as configurações da feature (tudo menos Enabled): bool liga/desliga, número com -/+.
    /// Texto só é exibido (edição no .cfg). Mudanças disparam OnEntryValueChanged, então a feature reaplica sozinha.
    /// </summary>
    private GameObject? BuildSettings(Transform list, Feature f)
    {
        var entries = new List<MelonPreferences_Entry>();
        foreach (var e in f.Category.Entries)
            if (e.Identifier != "Enabled") entries.Add(e);
        var actions = f.Actions;
        if (entries.Count == 0 && actions.Count == 0) return null;

        var panel = Ui.Child("Settings_" + f.Id, list);
        var v = panel.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4;
        v.padding = new RectOffset(30, 0, 0, 8);
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        foreach (var entry in entries)
        {
            var e = entry;
            var row = Ui.Row(panel.transform, 38);
            var label = Ui.Text(row.transform, "", 15);
            label.color = new Color(0.85f, 0.85f, 0.85f);
            System.Action refresh = () => label.text = $"{e.DisplayName}: <b>{Format(e.BoxedValue)}</b>";
            label.supportRichText = true;

            switch (e)
            {
                case MelonPreferences_Entry<bool> b:
                    Ui.Button(row.transform, "alternar", Off, () => { b.Value = !b.Value; Saved(); }, width: 100, height: 32);
                    break;
                case MelonPreferences_Entry<int> i:
                    Ui.Button(row.transform, "-", Off, () => { i.Value = System.Math.Max(0, i.Value - 1); Saved(); }, width: 44, height: 32);
                    Ui.Button(row.transform, "+", Off, () => { i.Value += 1; Saved(); }, width: 44, height: 32);
                    break;
                case MelonPreferences_Entry<float> fl:
                    Ui.Button(row.transform, "-", Off, () => { fl.Value = System.Math.Max(0f, fl.Value - Step(fl.Value)); Saved(); }, width: 44, height: 32);
                    Ui.Button(row.transform, "+", Off, () => { fl.Value += Step(fl.Value); Saved(); }, width: 44, height: 32);
                    break;
                default:
                    label.fontSize = 13;   // texto/listas: só leitura aqui
                    break;
            }
            _refreshers.Add(refresh);
        }
        if (actions.Count > 0)
        {
            var row = Ui.Row(panel.transform, 40);
            foreach (var (label, run) in actions)
                Ui.Button(row.transform, label, On, () => { run(); Refresh(); }, width: 200, height: 34);
        }
        panel.SetActive(false);
        return panel;
    }

    // ponytail: passo de 10% do valor (mín. 0.1) serve de 0.5 a 100000 sem teclado; precisão fina fica no .cfg.
    private static float Step(float value) => System.Math.Max(0.1f, (float)System.Math.Round(System.Math.Abs(value) * 0.1f, 1));

    private static string Format(object? value) => value switch
    {
        bool b => b ? "sim" : "não",
        float f => f.ToString("0.##"),
        string s when s.Length == 0 => "(vazio)",
        null => "-",
        _ => value.ToString() ?? "-",
    };

    private void Saved()
    {
        MelonPreferences.Save();
        Refresh();
    }

    protected override void OnOpened() => Refresh();

    private void Refresh()
    {
        foreach (var r in _refreshers)
        {
            try { r(); } catch (System.Exception e) { Log.Error($"painel: {e.Message}"); }
        }
    }

    private static bool SafeIsHost()
    {
        try { return Net.IsHost; } catch { return false; }
    }
}

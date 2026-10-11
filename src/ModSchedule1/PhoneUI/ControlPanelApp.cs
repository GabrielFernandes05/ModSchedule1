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

        Ui.Text(list, "Configurações finas: UserData/ModSchedule1.cfg", 14, minHeight: 40).color = new Color(0.7f, 0.7f, 0.7f);
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

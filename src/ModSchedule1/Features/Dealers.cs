using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Economy;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>
/// App "Dealers": todos os dealers recrutados numa tela (dinheiro, clientes, estoque) e coletar o dinheiro à distância.
/// O app nativo de dealers mostra um por vez e não coleta; este complementa.
/// </summary>
public sealed class Dealers : Feature<Dealers>
{
    public override string Id => "Dealers";
    public override string Name => "Dealers App";
    public override string Description => "Resumo de todos os dealers e coleta de dinheiro à distância.";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config
    // Coletar = mesma ação do diálogo do jogo (dinheiro vai pra quem clicou, SetCash(0) via RPC): vale para todos.

    // Cash é SyncVar: depois de coletar, o valor antigo ainda aparece até o servidor responder.
    private readonly Dictionary<IntPtr, float> _collectedAt = new();

    protected override void OnInit() => PhoneApp.All.Add(new DealersApp());

    public static IEnumerable<Dealer> Recruited()
    {
        foreach (var d in Dealer.AllPlayerDealers)
            if (d != null && d.IsRecruited) yield return d;
    }

    public bool Pending(Dealer d) => _collectedAt.TryGetValue(d.Pointer, out var t) && Time.unscaledTime - t < 3f && d.Cash > 0;

    public float Collect(Dealer d)
    {
        if (!Active || d.Cash <= 0 || Pending(d)) return 0;
        float cash = d.Cash;
        d.CollectCash();
        _collectedAt[d.Pointer] = Time.unscaledTime;
        Log.Info($"coletado {Ui.Money(cash)} de {d.FullName}");
        return cash;
    }

    public float CollectAll()
    {
        float total = 0;
        foreach (var d in Recruited()) total += Collect(d);
        return total;
    }

    public override void OnSceneLoaded(string scene) => _collectedAt.Clear();
}

public sealed class DealersApp : PhoneApp
{
    public override string AppName => "ModSchedule1Dealers";
    public override string IconLabel => "Dealers";
    protected override bool ShowIcon => Core.SeparateIcons;
    public override string IconGlyph => "D";
    public override Color IconColor => new(0.6f, 0.35f, 0.15f);

    private Transform _list = null!;
    private Text _summary = null!;
    private readonly List<(Dealer Dealer, Text Info, Text Collect)> _rows = new();
    private int _builtFor = -1;

    protected override void BuildUI(GameObject container)
    {
        _list = Header(container, "Dealers", out _);
        _builtFor = -1;
    }

    protected override void OnOpened() => OnTick();

    protected override void OnTick()
    {
        if (!Dealers.I.Active) { Rebuild(disabled: true); return; }
        int count = 0;
        foreach (var _ in Dealers.Recruited()) count++;
        if (count != _builtFor) Rebuild(disabled: false);

        float total = 0;
        foreach (var (d, info, collect) in _rows)
        {
            if (d == null) continue;
            total += d.Cash;
            string state = !d.IsConscious ? "  <color=#e66>inconsciente</color>" : "";
            info.text = $"<b>{d.FullName}</b>  ({d.Region}){state}\n" +
                        $"Dinheiro: <b>{Ui.Money(d.Cash)}</b>   Clientes: {d.AssignedCustomers.Count}/10   " +
                        $"Itens: {d.GetTotalInventoryItemCount()}   Corte: {d.DealerData.SalesCutPercentage:P0}";
            collect.text = Dealers.I.Pending(d) ? "coletando..." : d.Cash > 0 ? $"Coletar {Ui.Money(d.Cash)}" : "sem dinheiro";
        }
        _summary.text = $"{_rows.Count} dealer(s) recrutado(s)   Total com eles: <b>{Ui.Money(total)}</b>";
    }

    private void Rebuild(bool disabled)
    {
        Ui.Clear(_list);
        _rows.Clear();
        var top = Ui.Row(_list, 44);
        _summary = Ui.Text(top.transform, "", 18);
        _summary.supportRichText = true;
        if (disabled)
        {
            _summary.text = "Ative o 'Dealers App' no app Mods.";
            _builtFor = -1;
            return;
        }
        Ui.Button(top.transform, "Coletar todos", new Color(0.6f, 0.35f, 0.15f),
            () => Dealers.I.Guard("coletar todos", () => Dealers.I.CollectAll()), width: 180, height: 36);

        _builtFor = 0;
        foreach (var dealer in Dealers.Recruited())
        {
            var d = dealer;
            _builtFor++;
            var card = Ui.Card(_list);
            var row = Ui.Row(card.transform, 56);
            var info = Ui.Text(row.transform, "", 16, minHeight: 50);
            info.supportRichText = true;
            var (_, collect, _) = Ui.Button(row.transform, "", new Color(0.6f, 0.35f, 0.15f),
                () => Dealers.I.Guard("coletar", () => Dealers.I.Collect(d)), width: 200, height: 40);
            _rows.Add((d, info, collect));
        }
        if (_builtFor == 0) Ui.Text(_list, "Nenhum dealer recrutado ainda.", 16);
    }
}

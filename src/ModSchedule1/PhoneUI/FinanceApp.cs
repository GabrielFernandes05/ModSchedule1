using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using ModSchedule1.Features;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.PhoneUI;

/// <summary>App "Finanças": banco (depositar/sacar) + lavagem de dinheiro de cada negócio + últimas transações.</summary>
public sealed class FinanceApp : PhoneApp
{
    public override string AppName => "ModSchedule1Finance";
    public override string IconLabel => "Finanças";
    public override string IconGlyph => "$";
    public override Color IconColor => new(0.15f, 0.5f, 0.25f);

    private Transform _list = null!;
    private Text _summary = null!, _selected = null!, _ledger = null!;
    private GameObject _bankCard = null!, _dealersCard = null!;
    private Text _dealers = null!, _collectAllLabel = null!;

    private static Color Accent() => Color.Lerp(Ui.Native.Card, Ui.Native.Accent, 0.35f);
    private Transform _laundry = null!;
    private readonly List<(Business Business, Text Info, Text Ops, Text MaxLabel)> _rows = new();
    private int _builtFor = -1;
    private float _amount;

    protected override void BuildUI(GameObject container)
    {
        _list = Header(container, "Finanças", out _);
        _builtFor = -1;
        _rows.Clear();

        _summary = Ui.Text(_list, "", 18, minHeight: 56);
        _summary.supportRichText = true;

        // banco
        _bankCard = Ui.Card(_list);
        Ui.Text(_bankCard.transform, "Banco", 20, bold: true);
        _selected = Ui.Text(_bankCard.transform, "", 22, bold: true, anchor: TextAnchor.MiddleCenter, minHeight: 36);
        var chips = Ui.Row(_bankCard.transform, 40);
        foreach (var step in new[] { 100, 500, 1000, 5000 })
        {
            int s = step;
            Ui.Button(chips.transform, "+" + Ui.Money(s), default, () => { _amount += s; OnTick(); }, width: 100, height: 34);
        }
        Ui.Button(chips.transform, "Zerar", default, () => { _amount = 0; OnTick(); }, width: 90, height: 34);
        var max = Ui.Row(_bankCard.transform, 40);
        Ui.Button(max.transform, "Máx. depósito", default, () => { _amount = Bank.I.MaxDeposit; OnTick(); }, width: 170, height: 34);
        Ui.Button(max.transform, "Máx. saque", default, () => { _amount = Bank.Online; OnTick(); }, width: 170, height: 34);
        var act = Ui.Row(_bankCard.transform, 46);
        Ui.Button(act.transform, "Depositar", Accent(), () => Bank.I.Guard("depositar", () => { if (Bank.I.Deposit(_amount)) _amount = 0; OnTick(); }), width: 200, height: 40);
        Ui.Button(act.transform, "Sacar", Accent(), () => Bank.I.Guard("sacar", () => { if (Bank.I.Withdraw(_amount)) _amount = 0; OnTick(); }), width: 200, height: 40);

        // lavagem: um cartão por negócio (montado no OnTick, muda quando compra negócio)
        Ui.Text(_list, "Lavagem de dinheiro", 20, bold: true);
        _laundry = Ui.Child("Laundry", _list).transform;
        var v = _laundry.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = 8;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        // dealers: dinheiro com eles + coletar de todos (o app nativo de dealers coleta um por vez)
        _dealersCard = Ui.Card(_list);
        Ui.Text(_dealersCard.transform, "Dealers", 20, bold: true);
        _dealers = Ui.Text(_dealersCard.transform, "", 15, minHeight: 20);
        _dealers.supportRichText = true;
        var drow = Ui.Row(_dealersCard.transform, 44);
        (_, _collectAllLabel, _) = Ui.Button(drow.transform, "", Accent(), () => Dealers.I.Guard("coletar de todos", () => { Dealers.I.CollectAll(); OnTick(); }), width: 280, height: 38);

        Ui.Text(_list, "Últimas transações (desta sessão)", 18, bold: true);
        _ledger = Ui.Text(_list, "", 14, minHeight: 20);
        _ledger.color = Ui.Muted;
    }

    protected override void OnOpened() => OnTick();

    protected override void OnTick()
    {
        if (!MoneyManager.InstanceExists) return;
        bool bank = Bank.I.Active, laundry = Laundry.I.Active;
        float room = Bank.I.DepositRoom;
        _summary.text = $"Em mãos: <b>{Ui.Money(Bank.Cash)}</b>     No banco: <b>{Ui.Money(Bank.Online)}</b>\n" +
                        (bank ? (room < float.MaxValue ? $"Depósito liberado esta semana: {Ui.Money(room)}" : "Depósito sem limite semanal") : "Banco desativado (Configurações > Mods)");
        _bankCard.SetActive(bank);
        _selected.text = Bank.I.Busy ? "processando..." : $"Valor: {Ui.Money(_amount)}";

        _laundry.gameObject.SetActive(laundry);
        if (laundry) TickLaundry();

        _dealersCard.SetActive(Dealers.I.Active);
        if (Dealers.I.Active)
        {
            var dealerLines = new List<string>();
            foreach (var d in Dealers.Recruited())
                dealerLines.Add($"{d.FullName}: <b>{Ui.Money(d.Cash)}</b>{(Dealers.I.Pending(d) ? " (coletando...)" : "")}");
            _dealers.text = dealerLines.Count > 0 ? string.Join("   •   ", dealerLines) : "Nenhum dealer recrutado.";
            float held = Dealers.I.TotalHeld();
            _collectAllLabel.text = held > 0 ? $"Coletar de todos ({Ui.Money(held)})" : "Nada para coletar";
        }

        var ledger = MoneyManager.Instance.ledger;
        var lines = new List<string>();
        for (int i = ledger.Count - 1; i >= 0 && lines.Count < 8; i--)
        {
            var t = ledger[i];
            lines.Add($"{(t.total_Amount >= 0 ? "+" : "-")}{Ui.Money(Math.Abs(t.total_Amount))}   {t.transaction_Name}");
        }
        _ledger.text = lines.Count > 0 ? string.Join("\n", lines) : "nenhuma ainda";
    }

    private void TickLaundry()
    {
        var owned = Business.OwnedBusinesses;
        if (owned.Count != _builtFor) RebuildLaundry();
        foreach (var (b, info, ops, maxLabel) in _rows)
        {
            if (b == null) continue;
            info.text = $"<b>{b.PropertyName}</b>   lavando {Ui.Money(b.currentLaunderTotal)} de {Ui.Money(b.LaunderCapacity)}";
            var lines = new List<string>();
            foreach (var op in b.LaunderingOperations)
            {
                int left = Math.Max(0, op.completionTime_Minutes - op.minutesSinceStarted);
                lines.Add($"  {Ui.Money(op.amount)}  -  falta {left / 60}h{left % 60:00}");
            }
            ops.text = lines.Count > 0 ? string.Join("\n", lines) : "  nenhuma operação em andamento";
            int max = Laundry.I.MaxFor(b, keepReserve: true);
            maxLabel.text = Laundry.I.OnCooldown(b) ? "enviando..." : max >= 10 ? $"Lavar {Ui.Money(max)}" : "sem espaço/dinheiro";
        }
    }

    private void RebuildLaundry()
    {
        Ui.Clear(_laundry);
        _rows.Clear();
        var owned = Business.OwnedBusinesses;
        _builtFor = owned.Count;
        if (owned.Count == 0) { Ui.Text(_laundry, "Você ainda não tem negócios para lavar dinheiro.", 16).color = Ui.Muted; return; }
        foreach (var business in owned)
        {
            var b = business;
            var card = Ui.Card(_laundry);
            var info = Ui.Text(card.transform, "", 18);
            info.supportRichText = true;
            var ops = Ui.Text(card.transform, "", 15, minHeight: 20);
            ops.color = Ui.Muted;
            var row = Ui.Row(card.transform, 40);
            var (_, maxLabel, _) = Ui.Button(row.transform, "", Accent(),
                () => Laundry.I.Guard("lavar máximo", () => Laundry.I.Launder(b, Laundry.I.MaxFor(b, keepReserve: true))), width: 240, height: 34);
            Ui.Button(row.transform, "Lavar metade", default,
                () => Laundry.I.Guard("lavar metade", () => Laundry.I.Launder(b, Laundry.I.MaxFor(b, keepReserve: true) / 2)), width: 160, height: 34);
            _rows.Add((b, info, ops, maxLabel));
        }
    }
}

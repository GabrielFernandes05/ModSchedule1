using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Money;
using MelonLoader;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>App "Banco": depositar e sacar sem ir até o ATM, com o mesmo limite semanal de depósito do jogo.</summary>
public sealed class Bank : Feature<Bank>
{
    public override string Id => "Bank";
    public override string Name => "Bank App";
    public override string Description => "App no celular para depositar e sacar como no ATM.";
    // O ATM do jogo roda inteiro na máquina do jogador; o app faz as mesmas chamadas, então vale para host e clientes.

    public const float WeeklyLimit = 10000f;   // ATM.WeeklyDepositLimit é const no jogo: nunca gravar, só repetir o valor
    private MelonPreferences_Entry<bool> _respectLimit = null!;
    private float _busyUntil;

    protected override void OnInit()
    {
        _respectLimit = Setting("RespectWeeklyLimit", true, "Respeitar limite semanal de depósito ($10.000)",
            "Compartilha o limite com os ATMs, como no jogo");
        PhoneApp.All.Add(new BankApp());
    }

    public static float Cash => MoneyManager.Instance.cashBalance;
    public static float Online => MoneyManager.Instance.sync___get_value_onlineBalance();
    public bool Busy => Time.unscaledTime < _busyUntil;

    public float DepositRoom => _respectLimit.Value ? Math.Max(0f, WeeklyLimit - ATM.WeeklyDepositSum) : float.MaxValue;
    public float MaxDeposit => Math.Min(Cash, DepositRoom);

    public bool Deposit(float amount)
    {
        amount = Mathf.Floor(amount);
        if (!Active || Busy || amount <= 0 || amount > MaxDeposit) return false;
        var mm = MoneyManager.Instance;
        mm.ChangeCashBalance(-amount, true, true);
        mm.CreateOnlineTransaction("Cash Deposit", amount, 1f, string.Empty);
        ATM.WeeklyDepositSum += amount;   // static comum (não const): é o mesmo contador que os ATMs usam
        _busyUntil = Time.unscaledTime + 1.5f;
        Log.Info($"depósito {Ui.Money(amount)} (semana: {Ui.Money(ATM.WeeklyDepositSum)})");
        return true;
    }

    public bool Withdraw(float amount)
    {
        amount = Mathf.Floor(amount);
        if (!Active || Busy || amount <= 0 || amount > Online) return false;   // o servidor não valida saldo: checa aqui
        var mm = MoneyManager.Instance;
        mm.ChangeCashBalance(amount, true, true);
        mm.CreateOnlineTransaction("Cash Withdrawal", -amount, 1f, string.Empty);
        _busyUntil = Time.unscaledTime + 1.5f;
        Log.Info($"saque {Ui.Money(amount)}");
        return true;
    }
}

public sealed class BankApp : PhoneApp
{
    public override string AppName => "ModSchedule1Bank";
    public override string IconLabel => "Banco";
    protected override bool ShowIcon => Core.SeparateIcons;
    public override string IconGlyph => "$";
    public override Color IconColor => new(0.15f, 0.5f, 0.25f);

    private Text _balances = null!, _selected = null!, _ledger = null!;
    private float _amount;

    protected override void BuildUI(GameObject container)
    {
        var list = Header(container, "Banco", out _);
        _balances = Ui.Text(list, "", 18, minHeight: 80);
        _balances.supportRichText = true;

        var card = Ui.Card(list);
        _selected = Ui.Text(card.transform, "", 22, bold: true, anchor: TextAnchor.MiddleCenter, minHeight: 36);
        var chips = Ui.Row(card.transform, 40);
        foreach (var step in new[] { 100, 500, 1000, 5000 })
        {
            int s = step;
            Ui.Button(chips.transform, "+" + Ui.Money(s), new Color(0.3f, 0.3f, 0.34f), () => { _amount += s; OnTick(); }, width: 110, height: 34);
        }
        Ui.Button(chips.transform, "Zerar", new Color(0.45f, 0.25f, 0.25f), () => { _amount = 0; OnTick(); }, width: 90, height: 34);

        var all = Ui.Row(card.transform, 40);
        Ui.Button(all.transform, "Máx. depósito", new Color(0.3f, 0.3f, 0.34f), () => { _amount = Bank.I.MaxDeposit; OnTick(); }, width: 170, height: 34);
        Ui.Button(all.transform, "Máx. saque", new Color(0.3f, 0.3f, 0.34f), () => { _amount = Bank.Online; OnTick(); }, width: 170, height: 34);

        var act = Ui.Row(card.transform, 46);
        Ui.Button(act.transform, "Depositar", new Color(0.15f, 0.5f, 0.25f), () => Bank.I.Guard("depositar", () =>
        {
            if (Bank.I.Deposit(_amount)) _amount = 0;
            OnTick();
        }), width: 200, height: 40);
        Ui.Button(act.transform, "Sacar", new Color(0.2f, 0.4f, 0.65f), () => Bank.I.Guard("sacar", () =>
        {
            if (Bank.I.Withdraw(_amount)) _amount = 0;
            OnTick();
        }), width: 200, height: 40);

        Ui.Text(list, "Últimas transações (desta sessão)", 16, bold: true);
        _ledger = Ui.Text(list, "", 14, minHeight: 20);
        _ledger.color = new Color(0.8f, 0.8f, 0.8f);
    }

    protected override void OnOpened() => OnTick();

    protected override void OnTick()
    {
        if (!Bank.I.Active) { _balances.text = "Ative o 'Bank App' no app Mods."; return; }
        var room = Bank.I.DepositRoom;
        _balances.text = $"Em mãos: <b>{Ui.Money(Bank.Cash)}</b>\nNo banco: <b>{Ui.Money(Bank.Online)}</b>\n" +
                         (room < float.MaxValue ? $"Depósito liberado esta semana: <b>{Ui.Money(room)}</b>" : "Depósito sem limite semanal");
        _selected.text = Bank.I.Busy ? "processando..." : $"Valor: {Ui.Money(_amount)}";

        var ledger = MoneyManager.Instance.ledger;
        var lines = new List<string>();
        for (int i = ledger.Count - 1; i >= 0 && lines.Count < 8; i--)
        {
            var t = ledger[i];
            lines.Add($"{(t.total_Amount >= 0 ? "+" : "-")}{Ui.Money(Math.Abs(t.total_Amount))}   {t.transaction_Name}");
        }
        _ledger.text = lines.Count > 0 ? string.Join("\n", lines) : "nenhuma ainda";
    }
}

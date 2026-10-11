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
    public override string Description => "Depositar e sacar pelo app Finanças do celular, como no ATM.";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config
    // O ATM do jogo roda inteiro na máquina do jogador; o app faz as mesmas chamadas, então vale para host e clientes.

    public const float WeeklyLimit = 10000f;   // ATM.WeeklyDepositLimit é const no jogo: nunca gravar, só repetir o valor
    private Pref<bool> _respectLimit = null!;
    private float _busyUntil;

    protected override void OnInit()
    {
        _respectLimit = Setting("RespectWeeklyLimit", true, "Respeitar limite semanal de depósito ($10.000)",
            "Compartilha o limite com os ATMs, como no jogo");
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

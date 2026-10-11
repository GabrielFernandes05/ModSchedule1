using System;
using System.Collections.Generic;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Variables;
using MelonLoader;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>App "Lavagem": status de cada negócio e lavar dinheiro à distância; lavagem automática opcional.</summary>
public sealed class Laundry : Feature<Laundry>
{
    public override string Id => "Laundry";
    public override string Name => "Laundry App";
    public override string Description => "Lavar dinheiro pelo app Finanças do celular (e lavagem automática opcional).";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config
    // Cada jogador lava o próprio dinheiro em mãos: roda em todos os PCs, sem ser só host.

    private const int MinAmount = 10;   // mesmo mínimo da tela do jogo (LaunderingInterface.MinLaunderAmount é const)
    private Pref<bool> _auto = null!;
    private Pref<int> _autoInterval = null!;
    private Pref<int> _keepCash = null!;
    private int _lastAutoMinute = -1;
    // O pedido vai por RPC e a operação só aparece na lista depois da ida e volta: evita lavar duas vezes acima da capacidade.
    private readonly Dictionary<IntPtr, float> _cooldownUntil = new();

    protected override void OnInit()
    {
        _auto = Setting("AutoLaunder", false, "Lavagem automática", "Lava o máximo possível em todos os negócios periodicamente");
        _autoInterval = Setting("AutoLaunderMinutes", 60, "Intervalo da automática (min de jogo)", min: 10, max: 720);
        _keepCash = Setting("KeepCash", 500, "Manter em mãos ($)", "A lavagem (manual 'máximo' e automática) não usa esse valor", min: 0, max: 20000);
    }

    public static float Cash => MoneyManager.Instance.cashBalance;

    /// <summary>Quanto dá para lavar agora neste negócio: capacidade livre x dinheiro em mãos (menos a reserva).</summary>
    public int MaxFor(Business b, bool keepReserve)
    {
        float cash = Cash - (keepReserve ? _keepCash.Value : 0);
        return (int)Mathf.Min(b.appliedLaunderLimit, cash);
    }

    public bool OnCooldown(Business b) => _cooldownUntil.TryGetValue(b.Pointer, out var t) && Time.unscaledTime < t;

    /// <summary>Mesmo caminho da tela do jogo: tira o dinheiro em mãos, pede a operação ao servidor, conta a estatística.</summary>
    public bool Launder(Business b, int amount)
    {
        if (!Active || amount < MinAmount || OnCooldown(b) || amount > MaxFor(b, keepReserve: false)) return false;
        MoneyManager.Instance.ChangeCashBalance(-amount, true, true);
        b.StartLaunderingOperation(amount);
        _cooldownUntil[b.Pointer] = Time.unscaledTime + 3f;
        try
        {
            var vars = VariableDatabase.Instance;
            float started = vars.GetValue<float>("LaunderingOperationsStarted");
            vars.SetVariableValue("LaunderingOperationsStarted", (started + 1f).ToString(), true);
        }
        catch (Exception e) { Log.Debug($"estatística LaunderingOperationsStarted: {e.Message}"); }
        Log.Info($"lavando {Ui.Money(amount)} em {b.PropertyName}");
        return true;
    }

    public override void OnSceneLoaded(string scene)
    {
        _cooldownUntil.Clear();
        _lastAutoMinute = -1;
    }

    public override void OnUpdate()
    {
        if (!_auto.Value || !TimeManager.InstanceExists || !MoneyManager.InstanceExists) return;
        var tm = TimeManager.Instance;
        int now = tm.ElapsedDays * 1440 + tm.DailyMinSum;
        if (_lastAutoMinute >= 0 && now - _lastAutoMinute < Math.Max(1, _autoInterval.Value)) return;
        _lastAutoMinute = now;
        foreach (var b in Business.OwnedBusinesses)
        {
            int amount = MaxFor(b, keepReserve: true);
            if (amount >= MinAmount) Launder(b, amount);
        }
    }
}

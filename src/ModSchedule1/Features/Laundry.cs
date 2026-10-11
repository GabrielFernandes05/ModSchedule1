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
    public override string Description => "App no celular para lavar dinheiro à distância (e lavagem automática opcional).";
    // Cada jogador lava o próprio dinheiro em mãos: roda em todos os PCs, sem ser só host.

    private const int MinAmount = 10;   // mesmo mínimo da tela do jogo (LaunderingInterface.MinLaunderAmount é const)
    private MelonPreferences_Entry<bool> _auto = null!;
    private MelonPreferences_Entry<int> _autoInterval = null!;
    private MelonPreferences_Entry<int> _keepCash = null!;
    private int _lastAutoMinute = -1;
    // O pedido vai por RPC e a operação só aparece na lista depois da ida e volta: evita lavar duas vezes acima da capacidade.
    private readonly Dictionary<IntPtr, float> _cooldownUntil = new();

    protected override void OnInit()
    {
        _auto = Setting("AutoLaunder", false, "Lavagem automática", "Lava o máximo possível em todos os negócios periodicamente");
        _autoInterval = Setting("AutoLaunderMinutes", 60, "Intervalo da automática (min de jogo)");
        _keepCash = Setting("KeepCash", 500, "Manter em mãos ($)", "A lavagem (manual 'máximo' e automática) não usa esse valor");
        PhoneApp.All.Add(new LaundryApp());
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

public sealed class LaundryApp : PhoneApp
{
    public override string AppName => "ModSchedule1Laundry";
    public override string IconLabel => "Lavagem";
    protected override bool ShowIcon => Core.SeparateIcons;
    public override string IconGlyph => "L";
    public override Color IconColor => new(0.2f, 0.45f, 0.75f);

    private Transform _list = null!;
    private Text _summary = null!;
    private readonly List<(Business Business, Text Info, Text Ops, Text MaxLabel)> _rows = new();
    private int _builtFor = -1;

    protected override void BuildUI(GameObject container)
    {
        _list = Header(container, "Lavagem de dinheiro", out _);
        _builtFor = -1;
    }

    protected override void OnOpened() => OnTick();

    protected override void OnTick()
    {
        if (!Laundry.I.Active) { Rebuild(disabled: true); return; }
        var owned = Business.OwnedBusinesses;
        if (owned.Count != _builtFor) Rebuild(disabled: false);
        _summary.text = $"Dinheiro em mãos: <b>{Ui.Money(Laundry.Cash)}</b>";
        foreach (var (b, info, ops, maxLabel) in _rows)
        {
            if (b == null) continue;
            float total = b.currentLaunderTotal;
            info.text = $"<b>{b.PropertyName}</b>   lavando {Ui.Money(total)} de {Ui.Money(b.LaunderCapacity)}";
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

    private void Rebuild(bool disabled)
    {
        Ui.Clear(_list);
        _rows.Clear();
        _summary = Ui.Text(_list, "", 18);
        _summary.supportRichText = true;
        if (disabled)
        {
            _summary.text = "Ative o 'Laundry App' no app Mods.";
            _builtFor = -1;
            return;
        }
        var owned = Business.OwnedBusinesses;
        _builtFor = owned.Count;
        if (owned.Count == 0) Ui.Text(_list, "Você ainda não tem negócios para lavar dinheiro.", 16);
        foreach (var business in owned)
        {
            var b = business;
            var card = Ui.Card(_list);
            var info = Ui.Text(card.transform, "", 18);
            info.supportRichText = true;
            var ops = Ui.Text(card.transform, "", 15, minHeight: 20);
            ops.color = new Color(0.8f, 0.8f, 0.8f);
            var row = Ui.Row(card.transform, 40);
            var (_, maxLabel, _) = Ui.Button(row.transform, "", new Color(0.2f, 0.45f, 0.75f),
                () => Laundry.I.Guard("lavar máximo", () => Laundry.I.Launder(b, Laundry.I.MaxFor(b, keepReserve: true))), width: 240, height: 34);
            Ui.Button(row.transform, "Lavar metade", new Color(0.35f, 0.35f, 0.38f),
                () => Laundry.I.Guard("lavar metade", () => Laundry.I.Launder(b, Laundry.I.MaxFor(b, keepReserve: true) / 2)), width: 160, height: 34);
            _rows.Add((b, info, ops, maxLabel));
        }
    }
}

using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ModSchedule1.Features;

/// <summary>
/// Paga sozinho a dívida com os fornecedores (sementes, pseudo, coca...). O jogo só abate a dívida com dinheiro deixado
/// no esconderijo do fornecedor: depositamos lá à distância e o próprio jogo (no host) recolhe, abate a dívida,
/// melhora a relação e manda a mensagem "recebi $X". Só deposita com o esconderijo vazio, então dois jogadores com a
/// opção ligada não pagam a mesma dívida duas vezes (se coincidir, o excesso fica no esconderijo para a próxima).
/// </summary>
public sealed class SupplierDebt : Feature<SupplierDebt>
{
    public override string Id => "SupplierDebt";
    public override string Name => "Fornecedores: pagar dívida";
    public override string Description => "Paga sozinho a dívida com os fornecedores (sementes, pseudo...), sem ir ao esconderijo.";
    public override bool DefaultEnabled => false;   // mexe no seu dinheiro: liga quem quiser
    public override bool Shared => false;           // pessoal: sai do dinheiro de quem liga

    private Pref<bool> _fromBank = null!;
    private Pref<int> _reserve = null!;
    private readonly Dictionary<IntPtr, float> _paidAt = new();
    private float _nextCheck;

    protected override void OnInit()
    {
        _fromBank = Setting("FromBank", true, "Pagar pelo banco (desligado: dinheiro em mãos)");
        _reserve = Setting("Reserve", 0, "Manter no mínimo ($)", "Nunca paga se o saldo usado ficar abaixo disso", min: 0, max: 50000);
    }

    public override void OnSceneLoaded(string scene) => _paidAt.Clear();

    public override void OnUpdate()
    {
        if (Time.unscaledTime < _nextCheck || SceneManager.GetActiveScene().name != "Main" || !MoneyManager.InstanceExists) return;
        _nextCheck = Time.unscaledTime + 2f;
        foreach (var npc in NPCManager.NPCRegistry)
        {
            var supplier = npc?.TryCast<Supplier>();
            if (supplier != null) Guard("pagar " + supplier.FirstName, () => Pay(supplier));
        }
    }

    private void Pay(Supplier s)
    {
        var stash = s.Stash;
        if (s.Debt < 1f || stash == null || stash.Storage == null || stash.Storage.IsOpened) return;
        if (stash.CashAmount >= 1f) return;   // depósito anterior ainda não recolhido pelo jogo
        if (_paidAt.TryGetValue(s.Pointer, out var at) && Time.unscaledTime - at < 15f) return;   // espera o jogo recolher

        float available = (_fromBank.Value ? Bank.Online : Bank.Cash) - _reserve.Value;
        float amount = Mathf.Floor(Mathf.Min(s.Debt, available));
        if (amount < 1f) return;
        var cash = MoneyManager.Instance.GetCashInstance(amount);
        if (!stash.Storage.CanItemFit(cash, 1)) { Log.Debug($"esconderijo de {s.FullName} cheio"); return; }

        stash.Storage.InsertItem(cash, true);   // primeiro o depósito: se falhar, seu dinheiro não sai
        if (_fromBank.Value) MoneyManager.Instance.CreateOnlineTransaction($"Dívida com {s.FullName}", -amount, 1f, "ModSchedule1");
        else MoneyManager.Instance.ChangeCashBalance(-amount, true, true);
        _paidAt[s.Pointer] = Time.unscaledTime;
        Log.Info($"{Ui.Money(amount)} deixados no esconderijo de {s.FullName} (dívida {Ui.Money(s.Debt)}, {(_fromBank.Value ? "banco" : "em mãos")})");
    }
}

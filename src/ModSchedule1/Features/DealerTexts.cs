using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Product;
using ModSchedule1.PhoneUI;

namespace ModSchedule1.Features;

/// <summary>
/// Dealers mandam mensagem no app Messages: a cada venda, quando o produto está acabando (o jogo já avisa quando zera)
/// e um resumo à meia-noite. Tudo sai do servidor com network=true, como as mensagens nativas dos dealers: cada
/// jogador recebe uma cópia e quem não tem o mod também vê.
/// </summary>
public sealed class DealerTexts : Feature<DealerTexts>
{
    public override string Id => "DealerTexts";
    public override string Name => "Dealers mandam mensagem";
    public override string Description => "Dealers avisam cada venda, quando o produto está acabando e mandam um resumo do dia.";
    public override bool HostOnly => true;   // a venda só é processada no servidor

    private Pref<bool> _perDeal = null!, _daily = null!;
    private Pref<int> _lowStock = null!;
    private readonly Dictionary<IntPtr, (Dealer Dealer, int Deals, float Gross, float Share)> _today = new();
    private readonly HashSet<IntPtr> _warnedLow = new();
    private TimeManager? _subscribed;

    protected override void OnInit()
    {
        _perDeal = Setting("PerDeal", true, "Avisar cada venda");
        _lowStock = Setting("LowStock", 10, "Avisar com produto abaixo de (unidades, 0 = não)", min: 0, max: 200);
        _daily = Setting("DailySummary", true, "Resumo do dia (meia-noite)");
    }

    public override void OnSceneLoaded(string scene) { _today.Clear(); _warnedLow.Clear(); }

    public override void OnUpdate()
    {
        if (!TimeManager.InstanceExists) return;
        var tm = TimeManager.Instance;
        if (_subscribed != null && _subscribed.Pointer == tm.Pointer) return;
        var handler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(() => Guard("resumo do dia", DailySummary)));
        tm.onDayPass = Il2CppSystem.Delegate.Combine(tm.onDayPass, handler).Cast<Il2CppSystem.Action>();
        _subscribed = tm;
    }

    private static void Text(Dealer dealer, string text)
    {
        var conv = dealer.MSGConversation;
        if (conv == null) return;
        conv.SendMessage(new Message(text, Message.ESenderType.Other, false, -1), true, true);
    }

    private void OnDeal(Customer customer, Dealer dealer, float total, ProductList products)
    {
        float share = total * (1f - dealer.DealerData.SalesCutPercentage);
        _today.TryGetValue(dealer.Pointer, out var day);
        _today[dealer.Pointer] = (dealer, day.Deals + 1, day.Gross + total, day.Share + share);
        if (_perDeal.Value)
            Text(dealer, $"Vendi {products.GetCommaSeperatedString()} pra {customer.NPC.FullName} por {Ui.Money(total)}. Sua parte: {Ui.Money(share)}.");

        int stock = dealer.GetPackagedProductAmount();
        if (_lowStock.Value > 0 && stock > 0 && stock < _lowStock.Value)
        {
            if (_warnedLow.Add(dealer.Pointer)) Text(dealer, $"Tô ficando sem produto: só sobraram {stock} unidades.");
        }
        else if (stock >= _lowStock.Value) _warnedLow.Remove(dealer.Pointer);
        Log.Debug($"{dealer.FullName}: venda {Ui.Money(total)} (estoque {stock})");
    }

    private void DailySummary()
    {
        if (!Active || !_daily.Value) { _today.Clear(); return; }
        foreach (var (dealer, deals, gross, share) in _today.Values)
        {
            if (dealer == null || deals == 0) continue;
            Text(dealer, $"Resumo do dia: {deals} {(deals == 1 ? "venda" : "vendas")}, {Ui.Money(gross)} no total, sua parte {Ui.Money(share)}. " +
                         $"Tô com {Ui.Money(dealer.Cash)} pra você coletar.");
        }
        Log.Info($"resumo do dia enviado ({_today.Count} dealers com vendas)");
        _today.Clear();
    }

    // Lógica do RPC da venda: roda só no servidor, para vendas do jogador e de dealers. O nome tem um sufixo de hash
    // que muda entre versões do jogo, então achamos pelo prefixo.
    [HarmonyPatch]
    private static class HandoverPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(Customer)).First(m => m.Name.StartsWith("RpcLogic___ProcessHandoverServerSide"));

        private static void Postfix(Customer __instance, bool handoverByPlayer, float totalPayment, ProductList productList, NetworkObject dealerObject)
        {
            if (!I.Active || handoverByPlayer || dealerObject == null) return;
            I.Guard("venda do dealer", () =>
            {
                var dealer = dealerObject.GetComponent<Dealer>();
                if (dealer == null || !dealer.IsRecruited || dealer.DealerData.DealerType.ToString() != "PlayerDealer") return;
                I.OnDeal(__instance, dealer, totalPayment, productList);
            });
        }
    }
}

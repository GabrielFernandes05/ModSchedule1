using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.UI.Phone.Messages;
using ModSchedule1.PhoneUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModSchedule1.Features;

/// <summary>
/// Coletar o dinheiro dos dealers à distância: clicando no bloco "Cash" do app nativo de dealers, ou em
/// "Coletar de todos" no app Finanças. Mesma ação do diálogo do jogo (Dealer.CollectCash).
/// </summary>
public sealed class Dealers : Feature<Dealers>
{
    public override string Id => "Dealers";
    public override string Name => "Dealers+";
    public override string Description => "Coletar dinheiro dos dealers: clique no Cash do app de dealers, ou Finanças > Coletar de todos.";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config

    // Cash é SyncVar: depois de coletar, o valor antigo ainda aparece até o servidor responder.
    private readonly Dictionary<IntPtr, float> _collectedAt = new();
    private Button? _cardButton;
    private string? _titleOriginal;
    private bool _dumped;

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

    public override void OnSceneLoaded(string scene) => _collectedAt.Clear();

    public float CollectAll()
    {
        float total = 0;
        foreach (var d in Recruited()) total += Collect(d);
        return total;
    }

    public float TotalHeld()
    {
        float total = 0;
        foreach (var d in Recruited()) if (!Pending(d)) total += d.Cash;
        return total;
    }

    /// <summary>
    /// O bloco "Cash" do app nativo vira botão (sem elementos novos no layout do jogo): clicar coleta o dinheiro do
    /// dealer exibido. O título avisa; "Coletar de todos" fica no app Finanças.
    /// </summary>
    private void Refresh(DealerManagementApp app)
    {
        var card = app.CashLabel.transform.parent;
        var title = card.Find("Title")?.GetComponent<Text>();
        _titleOriginal ??= title?.text;
        if (_cardButton == null || _cardButton.gameObject != card.gameObject)
        {
            NativeUi.DumpOnce(ref _dumped, card, "DealerManagementApp (bloco Cash)");
            _cardButton = card.GetComponent<Button>() ?? card.gameObject.AddComponent<Button>();
            _cardButton.targetGraphic = card.GetComponent<Image>();
            _cardButton.onClick.AddListener((UnityEngine.Events.UnityAction)new Action(() =>
                Guard("coletar", () => { var d = app.SelectedDealer; if (d != null) Collect(d); Refresh(app); })));
        }
        var dealer = app.SelectedDealer;
        bool can = Active && dealer != null && dealer.Cash > 0 && !Pending(dealer);
        _cardButton.enabled = Active;
        _cardButton.interactable = can;
        if (title != null)
            title.text = !Active || dealer == null ? _titleOriginal ?? "Cash"
                : Pending(dealer) ? "Cash (coletando...)"
                : dealer.Cash > 0 ? "Cash (clique p/ coletar)" : _titleOriginal ?? "Cash";
    }

    [HarmonyPatch(typeof(DealerManagementApp), nameof(DealerManagementApp.SetDisplayedDealer))]
    private static class SetDisplayedDealerPatch
    {
        private static void Postfix(DealerManagementApp __instance) => I.Guard("DealerManagementApp", () => I.Refresh(__instance));
    }
}

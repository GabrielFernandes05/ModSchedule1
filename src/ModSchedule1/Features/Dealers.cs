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
/// Coletar o dinheiro dos dealers à distância, dentro do app nativo de dealers do celular: botão "Coletar $X" ao lado
/// do dinheiro do dealer exibido e "Coletar todos" no topo. Mesma ação do diálogo do jogo (Dealer.CollectCash).
/// </summary>
public sealed class Dealers : Feature<Dealers>
{
    public override string Id => "Dealers";
    public override string Name => "Dealers+";
    public override string Description => "Coletar dinheiro dos dealers pelo app de dealers do celular.";
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config

    // Cash é SyncVar: depois de coletar, o valor antigo ainda aparece até o servidor responder.
    private readonly Dictionary<IntPtr, float> _collectedAt = new();
    private Button? _collect, _collectAll;
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

    private void Refresh(DealerManagementApp app)
    {
        if (_collect == null || _collectAll == null)
        {
            NativeUi.DumpOnce(ref _dumped, app.CashLabel.transform.parent, "DealerManagementApp (CashLabel.parent)");
            // Clona o botão de texto nativo do app (Assign customer) para ficar com a mesma cara.
            _collect = NativeUi.CloneButton(app.AssignCustomerButton, app.CashLabel.transform, "MS1_Collect", () =>
                Guard("coletar", () => { var d = app.SelectedDealer; if (d != null) Collect(d); Refresh(app); }));
            _collectAll = NativeUi.CloneButton(app.AssignCustomerButton, app.Content, "MS1_CollectAll", () =>
                Guard("coletar todos", () => { foreach (var d in Recruited()) Collect(d); Refresh(app); }));
            NativeUi.Place(_collectAll, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-12, -8), new Vector2(230, 36));
        }

        var dealer = app.SelectedDealer;
        _collect.gameObject.SetActive(Active && dealer != null);
        _collectAll.gameObject.SetActive(Active);
        if (dealer != null)
        {
            // logo à direita do texto do dinheiro (a largura muda com o valor)
            NativeUi.Place(_collect, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(app.CashLabel.preferredWidth + 12, 0), new Vector2(170, 32));
            NativeUi.SetText(_collect, Pending(dealer) ? "coletando..." : dealer.Cash > 0 ? $"Coletar {Ui.Money(dealer.Cash)}" : "sem dinheiro");
            _collect.interactable = dealer.Cash > 0 && !Pending(dealer);
        }
        float total = 0;
        foreach (var d in Recruited()) if (!Pending(d)) total += d.Cash;
        NativeUi.SetText(_collectAll, total > 0 ? $"Coletar todos ({Ui.Money(total)})" : "Nada para coletar");
        _collectAll.interactable = total > 0;
    }

    [HarmonyPatch(typeof(DealerManagementApp), nameof(DealerManagementApp.SetDisplayedDealer))]
    private static class SetDisplayedDealerPatch
    {
        private static void Postfix(DealerManagementApp __instance) => I.Guard("DealerManagementApp", () => I.Refresh(__instance));
    }
}

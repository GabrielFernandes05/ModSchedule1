using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Phone.Messages;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>
/// Esconde do app Mensagens as conversas de clientes sem negócio pendente (deal concluído, recusado, expirado).
/// Usa o "Hide" do próprio jogo: fica salvo e a conversa volta sozinha quando o cliente manda mensagem nova.
/// </summary>
public sealed class AutoClearDeals : Feature<AutoClearDeals>
{
    public override string Id => "AutoClearDeals";
    public override string Name => "Auto Clear Completed Deals";
    public override string Description => "Esconde conversas de clientes sem negócio pendente no app Mensagens.";
    // Estado de UI local de cada jogador: cada PC limpa a própria lista, sem rede.

    private MelonPreferences_Entry<bool> _onlyRead = null!;

    protected override void OnInit()
    {
        _onlyRead = Setting("OnlyRead", false, "Só esconder conversas já lidas", "Evita sumir com mensagem que você ainda não viu");
    }

    public override IReadOnlyList<(string Label, Action Run)> Actions => new (string, Action)[]
    {
        ("Limpar agora", () => Guard("Limpar agora", Sweep)),
        ("Marcar tudo como lido", () => Guard("Marcar lidas", MarkAllRead)),
    };

    /// <summary>
    /// Regra conservadora: só cliente (nunca fornecedor/dealer), sem respostas pendentes (oferta, contraproposta),
    /// sem oferta nem contrato ativo, conversa fechada e que o jogo permite esconder.
    /// </summary>
    private bool TryHide(MSGConversation? c)
    {
        if (c == null || c.entry == null || !c.EntryVisible || c.IsOpen || c.AreResponsesActive) return false;
        if (_onlyRead.Value && !c.Read) return false;
        var cats = c.Categories;
        if (!cats.Contains(EConversationCategory.Customer) || cats.Contains(EConversationCategory.Supplier) ||
            cats.Contains(EConversationCategory.Dealer)) return false;
        var sender = c._sender;
        if (sender == null || !sender.CanConversationBeHidden || !sender.DisplayRelationshipInfo) return false;
        if (!sender.TryGetNPC(out var npc) || npc == null) return false;
        var customer = npc.GetComponent<Customer>();
        if (customer == null || customer.OfferedContractInfo != null || customer.CurrentContract != null) return false;
        c.SetEntryVisibility(false);   // também marca como lida; salvo como IsHidden
        return true;
    }

    private void Sweep()
    {
        var list = MessagesApp.ActiveConversations;
        int hidden = 0;
        for (int i = list.Count - 1; i >= 0; i--)
            if (TryHide(list[i])) hidden++;
        if (hidden > 0) Log.Debug($"{hidden} conversa(s) escondida(s)");
    }

    private void MarkAllRead()
    {
        if (!MessagesApp.InstanceExists) return;
        var unread = MessagesApp.Instance.unreadConversations;
        int n = unread.Count;
        for (int i = n - 1; i >= 0; i--) unread[i].SetRead(true);   // SetRead remove da lista: de trás pra frente
        Log.Info($"{n} conversa(s) marcada(s) como lida(s)");
    }

    [HarmonyPatch(typeof(MessagesApp), nameof(MessagesApp.SetOpen))]
    private static class OpenPatch
    {
        private static void Postfix(bool open)
        {
            if (open) I.Guard("MessagesApp.SetOpen", I.Sweep);
        }
    }

    // Deal concluído/falhou/expirou: roda em todos os PCs (QuestManager replica o fim da quest).
    [HarmonyPatch(typeof(Customer), nameof(Customer.CurrentContractEnded))]
    private static class ContractEndedPatch
    {
        private static void Postfix(Customer __instance) =>
            I.Guard("Customer.CurrentContractEnded", () => I.TryHide(__instance.NPC?.MSGConversation));
    }

    // Bug do jogo: filtros de categoria reativam entradas escondidas. Reaplica o esconder depois deles.
    [HarmonyPatch]
    private static class FilterPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MessagesApp), nameof(MessagesApp.ClearFilter));
            yield return AccessTools.Method(typeof(MessagesApp), nameof(MessagesApp.FilterByCategory));
        }

        private static void Postfix() => I.Guard("MessagesApp filtro", () =>
        {
            var list = MessagesApp.ActiveConversations;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c != null && !c.EntryVisible && c.entry != null) c.entry.gameObject.SetActive(false);
            }
        });
    }
}

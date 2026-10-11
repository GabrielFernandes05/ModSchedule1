using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Trash;
using MelonLoader;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>Remove o lixo assim que ele nasce no mundo (sacos de solo vazios, garrafas, bitucas...).</summary>
public sealed class NoMoreTrash : Feature<NoMoreTrash>
{
    public override string Id => "NoMoreTrash";
    public override string Name => "No More Trash";
    public override string Description => "Some com o lixo gerado no mundo. Itens específicos podem ser mantidos na config.";
    public override bool HostOnly => true;   // o lixo é objeto de rede: só o host destrói, os clientes recebem o despawn

    private Pref<string> _keep = null!;
    private Pref<bool> _keepBags = null!;
    private readonly List<TrashItem> _pending = new();

    protected override void OnInit()
    {
        _keep = Setting("KeepIds", "", "IDs de lixo para manter",
            "Lista separada por vírgula (ex.: glassbottle,cigarette). O ID de cada lixo aparece no log com DebugLogging ligado.");
        _keepBags = Setting("KeepTrashBags", true, "Manter sacos de lixo", "Não remove os sacos de lixo que você ou os funcionários fecham");
    }

    private bool ShouldKeep(string id)
    {
        if (_keepBags.Value && id.Contains("trashbag")) return true;
        foreach (var k in _keep.Value.Split(','))
            if (k.Trim().Equals(id, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Destrói no frame seguinte: quem criou o lixo ainda mexe nele (velocidade, GUID) logo após o retorno.
    public override void OnUpdate()
    {
        if (_pending.Count == 0) return;
        foreach (var trash in _pending)
        {
            if (trash == null) continue;   // já destruído/coletado
            Log.Debug($"removido: {trash.ID}");
            trash.DestroyTrash();
        }
        _pending.Clear();
    }

    public override void OnSceneLoaded(string scene) => _pending.Clear();

    // CreateTrashItemInternal roda para todo lixo criado (NPC, jogador, gerador, save carregado). TrashItem.Start não existe mais no 0.4.7.
    [HarmonyPatch(typeof(TrashManager), nameof(TrashManager.CreateTrashItemInternal))]
    private static class CreateTrashPatch
    {
        private static void Postfix(TrashItem __result)
        {
            I.Guard("CreateTrashItemInternal", () =>
            {
                if (__result == null) return;
                if (I.ShouldKeep(__result.ID)) { I.Log.Debug($"mantido: {__result.ID}"); return; }
                I._pending.Add(__result);
            });
        }
    }
}

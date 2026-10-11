using HarmonyLib;
using Il2CppScheduleOne.Trash;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>Remove o lixo assim que ele nasce no mundo (sacos de solo vazios, garrafas, bitucas...).</summary>
public sealed class NoMoreTrash : Feature<NoMoreTrash>
{
    public override string Id => "NoMoreTrash";
    public override string Name => "No More Trash";
    public override string Description => "Some com o lixo gerado no mundo. Itens específicos podem ser mantidos na config.";
    public override bool HostOnly => true;   // o lixo é objeto de rede: só o host destrói, os clientes recebem o despawn

    private MelonPreferences_Entry<string> _keep = null!;

    protected override void OnInit()
    {
        _keep = Setting("KeepIds", "", "IDs de lixo para manter",
            "Lista separada por vírgula (ex.: trashbag,glassbottle). O ID de cada lixo aparece no log com DebugLogging ligado.");
    }

    private bool ShouldKeep(string id)
    {
        foreach (var k in _keep.Value.Split(','))
            if (k.Trim().Equals(id, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    [HarmonyPatch(typeof(TrashItem), "Start")]
    private static class TrashStartPatch
    {
        private static void Postfix(TrashItem __instance)
        {
            I.Guard("TrashItem.Start", () =>
            {
                // Lixo "solto" fica num container *_Temp; lixo dentro de lixeiras/sacos não é tocado.
                var parent = __instance.transform.parent;
                if (parent == null || !parent.gameObject.name.Contains("_Temp")) return;
                if (I.ShouldKeep(__instance.ID)) { I.Log.Debug($"mantido: {__instance.ID}"); return; }
                I.Log.Debug($"removido: {__instance.ID}");
                __instance.DestroyTrash();
            });
        }
    }
}

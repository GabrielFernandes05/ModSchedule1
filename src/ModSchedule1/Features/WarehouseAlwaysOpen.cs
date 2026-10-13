using HarmonyLib;
using Il2CppScheduleOne.Map;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>Warehouse (Dark Market) aberto 24h. Opcionalmente ignora o rank necessário para desbloquear.</summary>
public sealed class WarehouseAlwaysOpen : Feature<WarehouseAlwaysOpen>
{
    public override string Id => "WarehouseAlwaysOpen";
    public override string Name => "Warehouse Always Open";
    public override string Description => "Warehouse (Dark Market) aberto a qualquer hora.";

    private Pref<bool> _ignoreRank = null!;
    private bool _unlockSent;

    protected override void OnInit()
    {
        _ignoreRank = Setting("UnlockWithoutRank", false, "Desbloquear sem rank",
            "Desbloqueia o Warehouse mesmo sem o rank exigido. ATENÇÃO: fica gravado no save (o host desbloqueia para todos).");
    }

    private static bool Unlocked(DarkMarket market) => market.Unlocked;

    // Destrava de vez pelo caminho do próprio jogo (RPC do host), assim o save e os outros jogadores ficam coerentes.
    public override void OnUpdate()
    {
        if (!_ignoreRank.Value || !Net.IsHost || !DarkMarket.InstanceExists) return;
        var market = DarkMarket.Instance;
        if (market.Unlocked || _unlockSent) return;
        _unlockSent = true;
        Log.Info("desbloqueando o Warehouse sem rank (opção UnlockWithoutRank)");
        market.SendUnlocked();
    }

    public override void OnSceneLoaded(string scene) => _unlockSent = false;

    // DarkMarket.Update faz IsOpen = ShouldBeOpen() todo frame; no IL2CPP o ShouldBeOpen foi embutido ali (patch nele
    // nunca rodava, então portões e porta seguiam o horário). Corrigimos IsOpen logo depois.
    [HarmonyPatch(typeof(DarkMarket), nameof(DarkMarket.Update))]
    private static class UpdatePatch
    {
        private static void Postfix(DarkMarket __instance)
        {
            if (!I.Active) return;
            try { if (!__instance.IsOpen && Unlocked(__instance)) __instance.IsOpen = true; }
            catch (System.Exception e) { I.Fault("DarkMarket.Update", e); }
        }
    }

    [HarmonyPatch(typeof(DarkMarketAccessZone), nameof(DarkMarketAccessZone.GetIsOpen))]
    private static class AccessZonePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (__result || !I.Active) return;
            try { if (DarkMarket.InstanceExists && DarkMarket.Instance.Unlocked) __result = true; }
            catch (System.Exception e) { I.Fault("AccessZone.GetIsOpen", e); }
        }
    }
}

using System;
using HarmonyLib;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ObjectScripts;

namespace ModSchedule1.Features;

/// <summary>
/// Fertilizante, SpeedGrow e PGR ficam no solo entre colheitas enquanto o solo ainda tem usos (Long-Life e
/// Extra-Long-Life). Solo comum (1 uso) se esgota na colheita e limpa tudo, como no jogo.
/// </summary>
public sealed class AbsorbentSoil : Feature<AbsorbentSoil>
{
    public override string Id => "AbsorbentSoil";
    public override string Name => "Absorbent Soil";
    public override string Description => "Aditivos ficam no solo entre colheitas enquanto o solo tiver usos.";
    // Roda em todos os PCs: a colheita limpa os aditivos localmente em cada um.

    private static IntPtr _keepFor = IntPtr.Zero;

    // Pot.OnPlantFullyHarvested: Plant=null; usos-1; ClearAdditives(); se usos<=0 ClearSoil() (que limpa de novo).
    [HarmonyPatch(typeof(Pot), nameof(Pot.OnPlantFullyHarvested))]
    private static class HarvestPatch
    {
        private static void Prefix(Pot __instance)
        {
            _keepFor = IntPtr.Zero;
            if (!I.Active) return;
            try
            {
                if (__instance.Plant == null || __instance._remainingSoilUses - 1 <= 0) return;
                _keepFor = __instance.Pointer;
                I.Log.Debug($"colheita: mantendo {__instance.AppliedAdditives.Count} aditivo(s), solo com {__instance._remainingSoilUses - 1} uso(s)");
            }
            catch (Exception e) { I.Fault("OnPlantFullyHarvested", e); }
        }

        private static void Postfix() => _keepFor = IntPtr.Zero;
    }

    [HarmonyPatch(typeof(GrowContainer), nameof(GrowContainer.ClearAdditives))]
    private static class ClearAdditivesPatch
    {
        private static bool Prefix(GrowContainer __instance) =>
            _keepFor == IntPtr.Zero || __instance.Pointer != _keepFor;   // false = pula a limpeza
    }
}

using HarmonyLib;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Compass;
using MelonLoader;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>Mostra a distância de todos os marcadores na bússola, não só dos que estão a até 50 m.</summary>
public sealed class CompassDistances : Feature<CompassDistances>
{
    public override string Id => "CompassDistances";
    public override string Name => "Compass Distances";
    public override string Description => "Sempre mostra a distância dos marcadores na bússola.";

    private MelonPreferences_Entry<float> _range = null!;

    protected override void OnInit()
    {
        _range = Setting("MaxDistance", 100000f, "Distância máxima exibida (m)", "Marcadores até essa distância mostram o rótulo");
    }

    // UpdateElement escreve o rótulo só se distância <= 50 (DISTANCE_LABEL_THRESHOLD é const, embutido no código
    // nativo: gravar nele derrubava o jogo na v0.3.0). Aqui completamos o rótulo que o jogo deixou vazio.
    [HarmonyPatch(typeof(CompassManager), nameof(CompassManager.UpdateElement))]
    private static class UpdateElementPatch
    {
        private static void Postfix(CompassManager __instance, CompassManager.Element element)
        {
            if (!I.Active || element == null || !element.Visible) return;
            try
            {
                var target = element.TargetTransform;
                var cam = __instance.cam;
                if (target == null || cam == null) return;
                float dist = Vector3.Distance(cam.position, target.position);
                if (dist <= 50f || dist > I._range.Value) return;
                element.DistanceLabel.text = UnitsUtility.FormatShortDistance(dist, UnitsUtility.ERoundingType.Up);
            }
            catch (System.Exception e) { I.Fault("UpdateElement", e); }
        }
    }
}

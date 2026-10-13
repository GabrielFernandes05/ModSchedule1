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
    public override bool Shared => false;   // pessoal: cada jogador usa a própria config

    private Pref<float> _range = null!;

    protected override void OnInit()
    {
        _range = Setting("MaxDistance", 100000f, "Distância máxima exibida (m)", "Marcadores até essa distância mostram o rótulo", min: 50, max: 100000);
    }

    // UpdateElement escreve o rótulo só se distância <= 50 e apaga nos outros (DISTANCE_LABEL_THRESHOLD é const,
    // embutido no código nativo: gravar nele derrubava o jogo na v0.3.0). No IL2CPP o UpdateElement foi embutido no laço
    // de UpdateElements (todo frame); só AddElement chama de verdade. Completamos os rótulos depois do laço.
    [HarmonyPatch(typeof(CompassManager), nameof(CompassManager.UpdateElements))]
    private static class UpdateElementsPatch
    {
        private static void Postfix(CompassManager __instance)
        {
            if (!I.Active) return;
            try
            {
                var cam = __instance.cam;
                if (cam == null) return;
                foreach (var element in __instance.elements)
                {
                    if (element == null || !element.Visible) continue;
                    var target = element.TargetTransform;
                    if (target == null) continue;
                    float dist = Vector3.Distance(cam.position, target.position);
                    if (dist <= 50f || dist > I._range.Value) continue;
                    element.DistanceLabel.text = UnitsUtility.FormatShortDistance(dist, UnitsUtility.ERoundingType.Up);
                }
            }
            catch (System.Exception e) { I.Fault("UpdateElements", e); }
        }
    }
}

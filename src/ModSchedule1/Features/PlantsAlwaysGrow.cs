using Il2CppInterop.Runtime;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Growing;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>Plantas e cogumelos continuam crescendo (e gastando água) depois das 4h, enquanto ninguém dorme.</summary>
public sealed class PlantsAlwaysGrow : Feature<PlantsAlwaysGrow>
{
    public override string Id => "PlantsAlwaysGrow";
    public override string Name => "Plants Always Grow";
    public override string Description => "Plantas e cogumelos continuam crescendo depois das 4h.";
    // Não é só host: cada PC calcula o crescimento localmente a cada minuto (como o jogo faz), então todos precisam rodar.

    private TimeManager? _subscribed;
    private bool _loggedTonight;

    // Às 4:00 o jogo só dispara onUncappedMinutePass (estações usam esse e seguem funcionando); vasos usam onMinutePass.
    public override void OnUpdate()
    {
        if (!TimeManager.InstanceExists) return;
        var tm = TimeManager.Instance;
        if (_subscribed != null && _subscribed.Pointer == tm.Pointer) return;
        tm.onUncappedMinutePass.Add(DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new System.Action(OnUncappedMinute)));
        _subscribed = tm;
        Log.Debug("inscrito em TimeManager.onUncappedMinutePass");
    }

    private void OnUncappedMinute()
    {
        if (!EndOfDay.IsNow) { _loggedTonight = false; return; }
        if (!Active) return;
        Guard("OnUncappedMinute", () =>
        {
            var containers = Object.FindObjectsOfType<GrowContainer>();
            EndOfDay.Run(() =>
            {
                foreach (var c in containers)
                    if (c != null) c.OnMinPass();   // mesmo caminho de um minuto normal: cresce, gasta água, cogumelos
            });
            if (!_loggedTonight) Log.Info($"4h: plantas seguem crescendo ({containers.Length} vasos/camas)");
            _loggedTonight = true;
        });
    }
}

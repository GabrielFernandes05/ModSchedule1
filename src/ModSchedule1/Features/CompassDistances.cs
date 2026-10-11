using Il2CppScheduleOne.UI.Compass;
using MelonLoader;

namespace ModSchedule1.Features;

/// <summary>Mostra a distância de todos os marcadores na bússola, não só dos que estão perto.</summary>
public sealed class CompassDistances : Feature<CompassDistances>
{
    public override string Id => "CompassDistances";
    public override string Name => "Compass Distances";
    public override string Description => "Sempre mostra a distância dos marcadores na bússola.";

    private MelonPreferences_Entry<float> _range = null!;
    private float _original = -1;   // valor do jogo, para restaurar ao desativar

    protected override void OnInit()
    {
        _range = Setting("MaxDistance", 100000f, "Distância máxima exibida (m)", "Marcadores até essa distância mostram o rótulo");
        EnabledEntry.OnEntryValueChanged.Subscribe((_, _) => Apply());
        _range.OnEntryValueChanged.Subscribe((_, _) => Apply());
    }

    public override void OnSceneLoaded(string scene)
    {
        if (scene == "Main") Apply();
    }

    // CompassManager só esconde o rótulo acima de DISTANCE_LABEL_THRESHOLD (campo estático, não const).
    private void Apply()
    {
        try
        {
            float current = CompassManager.DISTANCE_LABEL_THRESHOLD;
            if (_original < 0 && current > 0 && current < _range.Value) _original = current;
            float target = Active ? _range.Value : (_original > 0 ? _original : current);
            CompassManager.DISTANCE_LABEL_THRESHOLD = target;
            Log.Debug($"DISTANCE_LABEL_THRESHOLD {current} -> {target} (original {_original})");
        }
        catch (System.Exception e) { Fault("Apply", e); }
    }
}

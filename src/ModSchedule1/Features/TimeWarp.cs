using System;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace ModSchedule1.Features;

/// <summary>
/// Acelera o relógio do jogo com uma tecla (liga/desliga). Usa o multiplicador do próprio TimeManager: os minutos
/// continuam passando um a um, então plantas, mesas, entregas e NPCs andam como no normal, só mais rápido.
/// O jogo passa no máximo 1 minuto por frame (velocidade real ~ FPS minutos/s). Às 4:00 o relógio para (precisa
/// dormir), então desligamos sozinhos. Só o servidor muda o relógio: no multiplayer, só o host usa.
/// </summary>
public sealed class TimeWarp : Feature<TimeWarp>
{
    public override string Id => "TimeWarp";
    public override string Name => "Acelerar o tempo";
    public override string Description => "Tecla (padrão F6) liga/desliga o tempo acelerado. Para sozinho às 4:00. Só o host.";
    public override bool HostOnly => true;

    private Pref<int> _speed = null!;
    private Pref<string> _key = null!;
    private bool _on;
    private string? _parsedFrom;
    private KeyCode _keyCode = KeyCode.F6;

    protected override void OnInit()
    {
        _speed = Setting("Speed", 10, "Velocidade (x)", min: 2, max: 60);
        _key = Setting("Key", "F6", "Tecla (nome do KeyCode da Unity)", shared: false);
        Enabled.Changed += () => { if (!Enabled.Value) Stop(null); };
    }

    public override void OnSceneLoaded(string scene) => _on = false;   // TimeManager novo começa em 1x

    public override void OnUpdate()
    {
        if (!TimeManager.InstanceExists) return;
        var tm = TimeManager.Instance;
        if (Input.GetKeyDown(Key()))
        {
            if (_on) Stop("Tempo normal.");
            else if (tm.IsEndOfDay) Notify("São 4:00: o relógio só volta depois de dormir.");
            else { _on = true; Notify($"Tempo acelerado ({_speed.Value}x). {_key.Value} para voltar ao normal."); }
        }
        if (!_on) return;
        if (tm.IsEndOfDay) { Stop("4:00: hora de dormir. Tempo normal."); return; }
        if (Math.Abs(tm.TimeSpeedMultiplier - _speed.Value) > 0.01f) tm.SetTimeSpeedMultiplier(_speed.Value);
    }

    private KeyCode Key()
    {
        if (_parsedFrom == _key.Value) return _keyCode;
        _parsedFrom = _key.Value;
        if (Enum.TryParse<KeyCode>(_key.Value, true, out var k)) _keyCode = k;
        else Log.Warn($"tecla '{_key.Value}' inválida (use nomes como F6, F7, Keypad0); usando {_keyCode}");
        return _keyCode;
    }

    private void Stop(string? message)
    {
        if (!_on) return;
        _on = false;
        if (TimeManager.InstanceExists && Net.IsHost) TimeManager.Instance.SetTimeSpeedMultiplier(1f);
        if (message != null) Notify(message);
    }

    private void Notify(string text)
    {
        if (HintDisplay.InstanceExists) HintDisplay.Instance.ShowHint(text, 4f);
        Log.Info(text);
    }
}

using Il2CppScheduleOne.GameTime;

namespace ModSchedule1.Features;

/// <summary>
/// Às 4:00 o relógio congela (TimeManager.IsEndOfDay =&gt; CurrentTime == 400) até alguém dormir. Plantas e funcionários
/// checam IsEndOfDay e param. Para fazê-los agir como num minuto normal, trocamos CurrentTime por 3:59 só durante a
/// chamada (campo direto, sem disparar eventos) e devolvemos 4:00 em seguida. Patchar o getter IsEndOfDay não serve:
/// no IL2CPP ele pode estar inline no código nativo de quem chama.
/// </summary>
internal static class EndOfDay
{
    private const int Frozen = 400, Spoofed = 359;
    private static int _depth;
    private static bool _spoofing;

    public static bool IsNow => TimeManager.InstanceExists && TimeManager.Instance.CurrentTime == Frozen;

    public static void Enter()
    {
        if (_depth++ > 0 || !IsNow) return;
        TimeManager.Instance._CurrentTime_k__BackingField = Spoofed;
        _spoofing = true;
    }

    public static void Exit()
    {
        if (--_depth > 0) return;
        Restore();
    }

    /// <summary>Rede de segurança chamada todo frame: se uma exceção pulou o Exit, o relógio não fica preso em 3:59.</summary>
    public static void Restore()
    {
        _depth = 0;
        if (!_spoofing) return;
        _spoofing = false;
        if (TimeManager.InstanceExists) TimeManager.Instance._CurrentTime_k__BackingField = Frozen;
    }

    public static void Run(System.Action action)
    {
        Enter();
        try { action(); }
        finally { Exit(); }
    }
}

using MelonLoader.Logging;
using MelonLoader;

namespace ModSchedule1;

/// <summary>Per-feature logger. Everything goes to MelonLoader/Latest.log; Debug only when Core.DebugLogging is on.</summary>
public sealed class Log
{
    private readonly MelonLogger.Instance _logger;

    public Log(string source) => _logger = new MelonLogger.Instance("ModSchedule1:" + source, ColorARGB.Cyan);

    public void Info(string msg) => _logger.Msg(msg);
    public void Warn(string msg) => _logger.Warning(msg);
    public void Error(string msg) => _logger.Error(msg);

    public void Debug(string msg)
    {
        if (Core.DebugLogging) _logger.Msg(ColorARGB.Gray, "[debug] " + msg);
    }
}

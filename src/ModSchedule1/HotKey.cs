using System;
using UnityEngine;

namespace ModSchedule1;

/// <summary>Tecla configurável por nome do KeyCode da Unity ("F6", "M", "Keypad0"...). Nome inválido: usa o padrão.</summary>
public sealed class HotKey
{
    private readonly Pref<string> _pref;
    private readonly KeyCode _fallback;
    private readonly Log _log;
    private string? _parsedFrom;
    private KeyCode _code;

    public HotKey(Pref<string> pref, KeyCode fallback, Log log)
    {
        _pref = pref;
        _fallback = _code = fallback;
        _log = log;
    }

    public string Name => _pref.Value;

    public bool Pressed => Input.GetKeyDown(Code);

    private KeyCode Code
    {
        get
        {
            if (_parsedFrom == _pref.Value) return _code;
            _parsedFrom = _pref.Value;
            if (Enum.TryParse<KeyCode>(_pref.Value, true, out var k)) _code = k;
            else { _code = _fallback; _log.Warn($"tecla '{_pref.Value}' inválida (use nomes como F6, F7, M, Keypad0); usando {_fallback}"); }
            return _code;
        }
    }
}

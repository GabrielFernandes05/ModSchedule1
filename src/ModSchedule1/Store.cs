using System;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;

namespace ModSchedule1;

/// <summary>
/// Dados do jogador que não são config (histórico, favoritos): JSON em UserData/ModSchedule1/&lt;nome&gt;.json.
/// Local de cada jogador, fora do save do jogo, então remover o mod nunca corrompe um save.
/// </summary>
public static class Store
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, IncludeFields = true };
    private static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "ModSchedule1");

    public static T Load<T>(string name) where T : new()
    {
        var path = Path.Combine(Dir, name + ".json");
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception e)
        {
            Core.Log.Error($"{path} corrompido, começando vazio (cópia em .bak): {e.Message}");
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
        }
        return new T();
    }

    public static void Save<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, name + ".json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Options));
            File.Move(path + ".tmp", path, overwrite: true);   // grava inteiro ou nada
        }
        catch (Exception e) { Core.Log.Error($"falha ao salvar {name}.json: {e.Message}"); }
    }
}

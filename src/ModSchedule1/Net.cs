using Il2CppFishNet;
using Il2CppScheduleOne.Networking;

namespace ModSchedule1;

public static class Net
{
    /// <summary>True for single-player and for the multiplayer host (FishNet server).</summary>
    public static bool IsHost => InstanceFinder.IsServer;

    /// <summary>Entrou na sessão de outro jogador.</summary>
    public static bool IsClientOnly => InstanceFinder.IsClientOnly;

    /// <summary>
    /// Sessão multiplayer pela Steam. Metadados do lobby: só o dono (host) grava, e quem entra depois já recebe
    /// tudo (o próprio jogo guarda "owner", "version", "ready" ali). Limite de 8 KB por valor.
    /// </summary>
    public static bool InLobby => Lobby.InstanceExists && Lobby.Instance.IsInLobby;

    public static string? GetLobbyData(string key) => InLobby ? Lobby.Instance._lobbyService.GetLobbyData(key) : null;

    public static void SetLobbyData(string key, string value)
    {
        if (InLobby) Lobby.Instance.SetLobbyData(key, value);
    }
}

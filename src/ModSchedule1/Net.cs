using Il2CppFishNet;

namespace ModSchedule1;

public static class Net
{
    /// <summary>True for single-player and for the multiplayer host (FishNet server).</summary>
    public static bool IsHost => InstanceFinder.IsServer;
}

using System.Globalization;

namespace StardewCodexChat.Core;

/// <summary>Keep the existing disk keys, separating farmers sharing the same farm.</summary>
public static class PlayerSaveKey
{
    public static string Create(string? saveFolder, ulong worldId, long playerId) =>
        $"{saveFolder ?? "farmhand"}:{worldId.ToString(CultureInfo.InvariantCulture)}:{playerId.ToString(CultureInfo.InvariantCulture)}";
}

using Dalamud.Configuration;
using System;

namespace StreamerMode;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool SpoofCharacterName { get; set; } = false;
    public string SpoofedName { get; set; } = string.Empty;
    public bool SpoofWorld { get; set; } = false;
    public bool SpoofRandomCharacterNames { get; set; } = false;
    public bool HideMinionNameplate { get; set; } = false;
    public bool SpoofRandomFriendNames { get; set; } = false;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace StreamerMode.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Streamer Mode##With a hidden ID", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 100),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var spoofEnabled = plugin.Configuration.SpoofCharacterName;
        if (ImGui.Checkbox("Spoof character name", ref spoofEnabled))
        {
            plugin.Configuration.SpoofCharacterName = spoofEnabled;
            plugin.Configuration.Save();
        }

        if (spoofEnabled)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(200);
            var spoofedName = plugin.Configuration.SpoofedName;
            if (ImGui.InputText("##SpoofedName", ref spoofedName, 64))
            {
                plugin.Configuration.SpoofedName = spoofedName;
                plugin.Configuration.Save();
            }
            ImGui.Unindent();
        }

        var spoofWorld = plugin.Configuration.SpoofWorld;
        if (ImGui.Checkbox("Spoof world", ref spoofWorld))
        {
            plugin.Configuration.SpoofWorld = spoofWorld;
            plugin.Configuration.Save();
        }
    }
}

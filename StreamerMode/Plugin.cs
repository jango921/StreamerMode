using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using StreamerMode.Windows;
using System.Collections.Generic;

namespace StreamerMode;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlateGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/sm";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("StreamerMode");
    private MainWindow MainWindow { get; init; }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!Configuration.SpoofCharacterName || string.IsNullOrWhiteSpace(Configuration.SpoofedName))
            return;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var realName = localPlayer.Name.TextValue;

        foreach (var handler in handlers)
        {
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter)
                continue;

            if (handler.Name.TextValue == realName)
                handler.Name = new SeString(new TextPayload(Configuration.SpoofedName));
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (!Configuration.SpoofCharacterName && !Configuration.SpoofWorld)
            return;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var mutable = (IMutableChatMessage)message;
        var realName = localPlayer.Name.TextValue;

        // Cross-world senders append the world name to the TextValue, so use StartsWith
        if (!mutable.Sender.TextValue.StartsWith(realName))
            return;

        var displayName = (Configuration.SpoofCharacterName && !string.IsNullOrWhiteSpace(Configuration.SpoofedName))
            ? Configuration.SpoofedName
            : realName;

        var newSender = Configuration.SpoofWorld
            ? $"{displayName}@Streaming"
            : displayName;

        mutable.Sender = new SeString(new TextPayload(newSender));
    }

    private unsafe void OnDtrPostUpdate(AddonEvent type, AddonArgs args)
    {
        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;

        var worldName = PlayerState.HomeWorld.Value.Name.ToString();
        if (string.IsNullOrEmpty(worldName)) return;

        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null || node->Type != NodeType.Text) continue;

            var textNode = (AtkTextNode*)node;
            var nodeText = textNode->NodeText.ToString();
            if (nodeText != worldName && nodeText != "Streaming") continue;

            var spoof = Configuration.SpoofWorld;

            if (spoof)
                textNode->NodeText.SetString("Streaming");

            if (node->PrevSiblingNode != null)
                node->PrevSiblingNode->ToggleVisibility(!spoof);
            if (node->NextSiblingNode != null)
                node->NextSiblingNode->ToggleVisibility(!spoof);

            break;
        }
    }

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Streamer Mode window"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        NamePlateGui.OnNamePlateUpdate += OnNamePlateUpdate;
        ChatGui.ChatMessage += OnChatMessage;
        AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "_DTR", OnDtrPostUpdate);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        NamePlateGui.OnNamePlateUpdate -= OnNamePlateUpdate;
        ChatGui.ChatMessage -= OnChatMessage;
        AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "_DTR", OnDtrPostUpdate);
        ECommonsMain.Dispose();

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}

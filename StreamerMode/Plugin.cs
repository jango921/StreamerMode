using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using StreamerMode.Windows;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;

    private const string CommandName = "/sm";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("StreamerMode");
    private MainWindow MainWindow { get; init; }

    private readonly Dictionary<string, string> _randomNameMap = new();
    private readonly Dictionary<string, string> _randomDetailMap = new();
    private static readonly Random Rng = new();

    // Matches FFXIV player names: two words, each starting with a capital letter (e.g. "John Smith")
    private static readonly Regex PlayerNameRegex = new(
        @"^[A-Z][a-zA-Z'-]{1,14} [A-Z][a-zA-Z'-]{1,14}$", RegexOptions.Compiled);

    private static readonly string[] FirstNames =
    [
        "Aelius", "Beren", "Caelum", "Daeris", "Evara", "Faeron", "Galeth", "Halwyn",
        "Ilvara", "Joren", "Kaelith", "Lyrin", "Maeven", "Naeros", "Orvyn", "Pyriel",
        "Quorin", "Raevyn", "Selith", "Taeven", "Ulvyn", "Vaelos", "Wyren", "Xaelin",
        "Ysolde", "Zevran", "Aleth", "Brynn", "Corvin", "Daelin"
    ];

    private static readonly string[] LastNames =
    [
        "Ashveil", "Blackthorn", "Cloudweave", "Dawnbreak", "Embersong", "Frostmere",
        "Goldmantle", "Harrowgate", "Ironveil", "Jadewing", "Kindlewick", "Lowmist",
        "Moonwhisper", "Nightvale", "Oakheart", "Petalwind", "Quicksilver", "Ravenwood",
        "Stonecrest", "Twilightborn", "Umberspire", "Valecroft", "Westmoor", "Xandermere",
        "Yewbrook", "Zephyrfall", "Ambervale", "Brightwater", "Cinderfell", "Darkhollow"
    ];

    // All entries are ≤4 chars so they fit in narrow FriendList columns (location, job).
    private static readonly string[] FakeShortDetails =
    [
        "Mist", "Gobl", "LvBd", "Shir", "Empy", "Kug.", "MorD", "Isgd",
        "Crys", "Eulm", "Firm", "Radr", "Tuly", "Sol9", "OSha", "CdSl",
        "Hidn", "Anon", "Unk.", "???",
    ];

    // Strings that should never be randomized regardless of feature flags
    private static readonly HashSet<string> PreservedStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Status values
        "Online", "Offline", "Away", "Busy", "Do Not Disturb",
        // Friend list column headers / UI labels
        "Location", "Company", "Free Company", "Grand Company",
        "Last Online", "World", "Name", "Class/Job", "Lang",
        "Move online players to top of list",
    };

    private string GetOrCreateRandomName(string realName)
    {
        if (_randomNameMap.TryGetValue(realName, out var spoofed))
            return spoofed;

        var generated = $"{FirstNames[Rng.Next(FirstNames.Length)]} {LastNames[Rng.Next(LastNames.Length)]}";
        _randomNameMap[realName] = generated;
        return generated;
    }

    private string GetOrCreateRandomDetail(string realText)
    {
        if (_randomDetailMap.TryGetValue(realText, out var spoofed))
            return spoofed;

        var generated = FakeShortDetails[Rng.Next(FakeShortDetails.Length)];
        _randomDetailMap[realText] = generated;
        return generated;
    }

    public void ClearRandomNameMap()
    {
        _randomNameMap.Clear();
        _randomDetailMap.Clear();
    }
    public void RequestNamePlateRedraw() => NamePlateGui.RequestRedraw();

    // Character Config → Nameplates → NPC → Minions → Display Name
    // 0 = Always, 1 = When Targeted, 2 = Never
    internal void SetMinionNameplateDisplayMode(bool hide)
    {
        GameConfig.UiConfig.Set("NamePlateDispTypeMinion", hide ? 2u : 0u);
    }

    private void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!Configuration.SpoofCharacterName && !Configuration.SpoofRandomCharacterNames)
            return;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var realName = localPlayer.Name.TextValue;

        foreach (var handler in handlers)
        {
            if (handler.NamePlateKind != NamePlateKind.PlayerCharacter)
                continue;

            var name = handler.Name.TextValue;

            if (name == realName)
            {
                if (Configuration.SpoofCharacterName && !string.IsNullOrWhiteSpace(Configuration.SpoofedName))
                    handler.Name = new SeString(new TextPayload(Configuration.SpoofedName));
            }
            else if (Configuration.SpoofRandomCharacterNames)
            {
                handler.Name = new SeString(new TextPayload(GetOrCreateRandomName(name)));
            }
        }
    }

    // Runs after the game's own nameplate render pass with ALL active handlers, so node-flag
    // changes here survive for the current frame regardless of incremental-update state.
    private unsafe void OnPostNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!Configuration.HideMinionNameplate) return;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        foreach (var handler in handlers)
        {
            var obj = handler.GameObject;
            if (obj?.ObjectKind != ObjectKind.Companion) continue;
            if (obj.OwnerId != localPlayer.EntityId) continue;

            var npObject = (AddonNamePlate.NamePlateObject*)handler.NamePlateObjectAddress;
            if (npObject == null) continue;

            var root = npObject->RootComponentNode;
            if (root == null) continue;

            ((AtkResNode*)root)->ToggleVisibility(false);
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        var mutable = (IMutableChatMessage)message;
        var realName = localPlayer.Name.TextValue;
        var senderText = mutable.Sender.TextValue;

        // Cross-world senders append the world name to the TextValue, so use StartsWith
        if (senderText.StartsWith(realName))
        {
            if (!Configuration.SpoofCharacterName && !Configuration.SpoofWorld)
                return;

            var displayName = (Configuration.SpoofCharacterName && !string.IsNullOrWhiteSpace(Configuration.SpoofedName))
                ? Configuration.SpoofedName
                : realName;

            var newSender = Configuration.SpoofWorld
                ? $"{displayName}@Streaming"
                : displayName;

            mutable.Sender = new SeString(new TextPayload(newSender));
        }
        else if (Configuration.SpoofRandomCharacterNames)
        {
            // Cross-world senders include @World; strip it to get a stable map key
            var atIndex = senderText.IndexOf('@');
            var baseName = atIndex >= 0 ? senderText[..atIndex] : senderText;
            if (!string.IsNullOrWhiteSpace(baseName))
                mutable.Sender = new SeString(new TextPayload(GetOrCreateRandomName(baseName)));
        }
    }

    // Addons whose text nodes can contain the local player's name
    private static readonly string[] NameAddons =
    [
        "Character",           // Character panel
        "_PartyList",          // Party list HUD
        "_AllianceList1",      // Alliance list (A)
        "_AllianceList2",      // Alliance list (B)
        "_TargetInfo",         // Target bar
        "_FocusTargetInfo",    // Focus target
        "CharaCard",           // Adventurer plate / character card
        "CharacterInspect",    // Character inspection
        "FriendList",          // Friend list
        "GuildMember",         // FC member list
        "LinkShell",           // Linkshell
        "CrossWorldLinkshell", // Cross-world linkshell
        "Social",              // Social list
        "BlackList",           // Blacklist
    ];

    // Builds a text → replacement map covering both SpoofCharacterName (local player)
    // and SpoofRandomCharacterNames (other party members).
    private Dictionary<string, string> BuildReplacements()
    {
        var map = new Dictionary<string, string>();
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null) return map;

        var realName = localPlayer.Name.TextValue;

        if (Configuration.SpoofCharacterName && !string.IsNullOrWhiteSpace(Configuration.SpoofedName))
        {
            var spoofed = Configuration.SpoofedName;
            map[realName] = spoofed;

            var si = realName.IndexOf(' ');
            if (si > 0)
                map.TryAdd(realName[..si], spoofed.Contains(' ') ? spoofed[..spoofed.IndexOf(' ')] : spoofed);
        }

        if (Configuration.SpoofRandomCharacterNames)
        {
            foreach (var member in PartyList)
            {
                var memberName = member.Name.ToString();
                if (string.IsNullOrWhiteSpace(memberName) || memberName == realName) continue;

                var random = GetOrCreateRandomName(memberName);
                map.TryAdd(memberName, random);

                var si = memberName.IndexOf(' ');
                if (si > 0)
                    map.TryAdd(memberName[..si], random.Contains(' ') ? random[..random.IndexOf(' ')] : random);
            }
        }

        return map;
    }

    // Persistent addons scanned every tick via generic NodeList walk.
    private static readonly string[] FrameworkScannedAddons = ["_AllianceList1", "_AllianceList2", "Social"];

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        if (!Configuration.SpoofCharacterName && !Configuration.SpoofRandomCharacterNames)
            return;

        var replacements = BuildReplacements();
        if (replacements.Count == 0) return;

        // _PartyList: name nodes live in AddonPartyList.PartyListMemberStruct.Name (direct struct
        // fields), not in the flat NodeList — access them via the typed struct pointer directly.
        var partyPtr = GameGui.GetAddonByName("_PartyList");
        if (partyPtr != nint.Zero)
        {
            var partyBase = (AtkUnitBase*)(nint)partyPtr;
            if (partyBase->IsVisible)
                UpdatePartyListNodes((AddonPartyList*)(nint)partyPtr, replacements);
        }

        foreach (var addonName in FrameworkScannedAddons)
        {
            var ptr = GameGui.GetAddonByName(addonName);
            if (ptr == nint.Zero) continue;

            var addon = (AtkUnitBase*)(nint)ptr;
            if (!addon->IsVisible) continue;

            ReplaceInAddon(&addon->UldManager, replacements);
        }
    }

    private static unsafe void UpdatePartyListNodes(AddonPartyList* addon, IReadOnlyDictionary<string, string> replacements)
    {
        var count = Math.Min(addon->MemberCount, 8);
        // _partyMembers is internal FixedSizeArray8 at offset 0x238; access via pointer arithmetic.
        var members = (AddonPartyList.PartyListMemberStruct*)((byte*)addon + 0x238);

        for (var i = 0; i < count; i++)
        {
            var nameNode = members[i].Name;
            if (nameNode == null) continue;

            var text = nameNode->NodeText.ToString();
            if (!string.IsNullOrWhiteSpace(text) && replacements.TryGetValue(text, out var replacement))
                nameNode->NodeText.SetString(replacement);
        }
    }

    private unsafe void OnAddonNameUpdate(AddonEvent type, AddonArgs args)
    {
        var isFriendList = args.AddonName == "FriendList";
        var needBasic = Configuration.SpoofCharacterName || Configuration.SpoofRandomCharacterNames;
        var needFriends = Configuration.SpoofRandomFriendNames && isFriendList;

        if (!needBasic && !needFriends)
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        if (addon == null) return;

        var replacements = BuildReplacements();

        if (needFriends)
        {
            // Pattern-based pass: replaces known names AND any unrecognised text that
            // looks like an FFXIV player name (two capitalised words).
            ReplaceNamesPatternBased(&addon->UldManager, replacements);
            return;
        }

        if (replacements.Count == 0) return;
        ReplaceInAddon(&addon->UldManager, replacements);
    }

    private static unsafe void ReplaceInAddon(AtkUldManager* manager, IReadOnlyDictionary<string, string> replacements)
    {
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null) continue;

            if (node->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                if (replacements.TryGetValue(textNode->NodeText.ToString(), out var replacement))
                    textNode->NodeText.SetString(replacement);
            }
            else if ((ushort)node->Type >= 1000)
            {
                var compNode = (AtkComponentNode*)node;
                if (compNode->Component != null)
                    ReplaceInAddon(&compNode->Component->UldManager, replacements);
            }
        }
    }

    // Like ReplaceInAddon but also randomizes player names (regex), locations, and company names
    // found in the friend list. Text in PreservedStrings (Online/Offline/etc.) is left alone.
    private unsafe void ReplaceNamesPatternBased(AtkUldManager* manager, IReadOnlyDictionary<string, string> knownReplacements)
    {
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null) continue;

            if (node->Type == NodeType.Text)
            {
                var textNode = (AtkTextNode*)node;
                var text = textNode->NodeText.ToString();
                if (string.IsNullOrWhiteSpace(text)) continue;

                if (knownReplacements.TryGetValue(text, out var replacement))
                    textNode->NodeText.SetString(replacement);
                else if (PlayerNameRegex.IsMatch(text))
                    textNode->NodeText.SetString(GetOrCreateRandomName(text));
                else if (!PreservedStrings.Contains(text) && HasAnyLetter(text))
                    textNode->NodeText.SetString(GetOrCreateRandomDetail(text));
            }
            else if ((ushort)node->Type >= 1000)
            {
                var compNode = (AtkComponentNode*)node;
                if (compNode->Component != null)
                    ReplaceNamesPatternBased(&compNode->Component->UldManager, knownReplacements);
            }
        }
    }

    private static bool HasAnyLetter(string s)
    {
        foreach (var c in s)
            if (char.IsLetter(c)) return true;
        return false;
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

        NamePlateGui.OnDataUpdate += OnNamePlateUpdate;
        NamePlateGui.OnPostDataUpdate += OnPostNamePlateUpdate;
        ChatGui.ChatMessage += OnChatMessage;
        Framework.Update += OnFrameworkUpdate;
        AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "_DTR", OnDtrPostUpdate);

        foreach (var addon in NameAddons)
        {
            AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, OnAddonNameUpdate);
            AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, addon, OnAddonNameUpdate);
            AddonLifecycle.RegisterListener(AddonEvent.PostRefresh, addon, OnAddonNameUpdate);
        }

        SetMinionNameplateDisplayMode(Configuration.HideMinionNameplate);
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        NamePlateGui.OnDataUpdate -= OnNamePlateUpdate;
        NamePlateGui.OnPostDataUpdate -= OnPostNamePlateUpdate;
        ChatGui.ChatMessage -= OnChatMessage;
        Framework.Update -= OnFrameworkUpdate;
        AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "_DTR", OnDtrPostUpdate);

        foreach (var addon in NameAddons)
        {
            AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, addon, OnAddonNameUpdate);
            AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, addon, OnAddonNameUpdate);
            AddonLifecycle.UnregisterListener(AddonEvent.PostRefresh, addon, OnAddonNameUpdate);
        }

        if (Configuration.HideMinionNameplate)
            SetMinionNameplateDisplayMode(false);

        ECommonsMain.Dispose();

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}

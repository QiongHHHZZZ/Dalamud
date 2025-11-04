using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using CheapLoc;
using Dalamud.Bindings.ImGui;
using Dalamud.Configuration.Internal;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Internal.DesignSystem;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Internal;
using Dalamud.Plugin.Internal.AutoUpdate;
using Dalamud.Plugin.Internal.Types;

namespace Dalamud.Interface.Internal.Windows.Settings.Tabs;

[SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:Elements should be documented", Justification = "Internals")]
internal sealed class SettingsTabAutoUpdates : SettingsTab
{
    private AutoUpdateBehavior behavior;
    private bool updateDisabledPlugins;
    private bool checkPeriodically;
    private bool chatNotification;
    private string pickerSearch = string.Empty;
    private List<AutoUpdatePreference> autoUpdatePreferences = [];

    public override SettingsEntry[] Entries { get; } = [];

    public override string Title => Loc.Localize("DalamudSettingsAutoUpdates", "自动更新");

    public override SettingsOpenKind Kind => SettingsOpenKind.AutoUpdates;

    public override void Draw()
    {
        ImGui.TextColoredWrapped(ImGuiColors.DalamudWhite, Loc.Localize("DalamudSettingsAutoUpdateHint",
                                                "Dalamud 可以自动更新你的插件，确保你始终" +
                                                "能获得最新功能和错误修复。可以在此设置自动更新的时机和方式。"));
        ImGuiHelpers.ScaledDummy(2);

        ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey, Loc.Localize("DalamudSettingsAutoUpdateDisclaimer1",
                                                "你始终可以通过插件列表中的更新按钮手动更新插件。" +
                                                "也可右键单击插件并选择\"始终自动更新\"来为特定插件启用自动更新。"));
        ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey, Loc.Localize("DalamudSettingsAutoUpdateDisclaimer2",
                                                "Dalamud 只会在你处于空闲状态时通知更新。"));

        ImGuiHelpers.ScaledDummy(8);

        ImGui.TextColoredWrapped(ImGuiColors.DalamudWhite, Loc.Localize("DalamudSettingsAutoUpdateBehavior",
                                                "当游戏启动时..."));
        var behaviorInt = (int)this.behavior;
        ImGui.RadioButton(Loc.Localize("DalamudSettingsAutoUpdateNone", "不自动检查更新"), ref behaviorInt, (int)AutoUpdateBehavior.None);
        ImGui.RadioButton(Loc.Localize("DalamudSettingsAutoUpdateNotify", "仅通知新更新"), ref behaviorInt, (int)AutoUpdateBehavior.OnlyNotify);
        ImGui.RadioButton(Loc.Localize("DalamudSettingsAutoUpdateMainRepo", "自动更新主库插件"), ref behaviorInt, (int)AutoUpdateBehavior.UpdateMainRepo);
        ImGui.RadioButton(Loc.Localize("DalamudSettingsAutoUpdateAll", "自动更新所有插件"), ref behaviorInt, (int)AutoUpdateBehavior.UpdateAll);
        this.behavior = (AutoUpdateBehavior)behaviorInt;

        if (this.behavior == AutoUpdateBehavior.UpdateAll)
        {
            var warning = Loc.Localize(
                "DalamudSettingsAutoUpdateAllWarning",
                "警告：这将更新所有插件，包括非主库来源的插件。\n" +
                "这些更新未经 Dalamud 团队审核，可能包含恶意代码。");
            ImGui.TextColoredWrapped(ImGuiColors.DalamudOrange, warning);
        }

        ImGuiHelpers.ScaledDummy(8);

        ImGui.Checkbox(Loc.Localize("DalamudSettingsAutoUpdateDisabledPlugins", "自动更新当时被禁用的插件"), ref this.updateDisabledPlugins);
        ImGui.Checkbox(Loc.Localize("DalamudSettingsAutoUpdateChatMessage", "在聊天栏显示可用更新通知"), ref this.chatNotification);
        ImGui.Checkbox(Loc.Localize("DalamudSettingsAutoUpdatePeriodically", "游戏运行时定期检查更新"), ref this.checkPeriodically);
        ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey, Loc.Localize("DalamudSettingsAutoUpdatePeriodicallyHint",
                                                "启动后不会自动更新插件，仅在你未活跃游戏时接收通知。"));

        ImGuiHelpers.ScaledDummy(5);
        ImGui.Separator();
        ImGuiHelpers.ScaledDummy(5);

        ImGui.TextColoredWrapped(ImGuiColors.DalamudWhite, Loc.Localize("DalamudSettingsAutoUpdateOptedIn",
                                                "插件单独设置"));

        ImGui.TextColoredWrapped(ImGuiColors.DalamudWhite, Loc.Localize("DalamudSettingsAutoUpdateOverrideHint",
                                                "在此可为特定插件单独设置是否接收更新，" +
                                                "这将覆盖上述全局设置。"));

        if (this.autoUpdatePreferences.Count == 0)
        {
            ImGuiHelpers.ScaledDummy(20);

            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            {
                ImGuiHelpers.CenteredText(Loc.Localize("DalamudSettingsAutoUpdateOptedInHint2",
                                                       "你尚未为任何插件配置自动更新规则"));
            }

            ImGuiHelpers.ScaledDummy(2);
        }
        else
        {
            ImGuiHelpers.ScaledDummy(5);

            var pic = Service<PluginImageCache>.Get();

            var windowSize = ImGui.GetWindowSize();
            var pluginLineHeight = 32 * ImGuiHelpers.GlobalScale;
            Guid? wantRemovePluginGuid = null;

            foreach (var preference in this.autoUpdatePreferences)
            {
                var pmPlugin = Service<PluginManager>.Get().InstalledPlugins
                                                   .FirstOrDefault(x => x.EffectiveWorkingPluginId == preference.WorkingPluginId);

                var btnOffset = 2;

                if (pmPlugin != null)
                {
                    var cursorBeforeIcon = ImGui.GetCursorPos();
                    pic.TryGetIcon(pmPlugin, pmPlugin.Manifest, pmPlugin.IsThirdParty, out var icon, out _);
                    icon ??= pic.DefaultIcon;

                    ImGui.Image(icon.Handle, new Vector2(pluginLineHeight));

                    if (pmPlugin.IsDev)
                    {
                        ImGui.SetCursorPos(cursorBeforeIcon);
                        using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, 0.7f))
                        {
                            ImGui.Image(pic.DevPluginIcon.Handle, new Vector2(pluginLineHeight));
                        }
                    }

                    ImGui.SameLine();

                    var text = $"{pmPlugin.Name}{(pmPlugin.IsDev ? " (开发版插件" : string.Empty)}";
                    var textHeight = ImGui.CalcTextSize(text);
                    var before = ImGui.GetCursorPos();

                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (pluginLineHeight / 2) - (textHeight.Y / 2));
                    ImGui.Text(text);

                    ImGui.SetCursorPos(before);
                }
                else
                {
                    ImGui.Image(pic.DefaultIcon.Handle, new Vector2(pluginLineHeight));
                    ImGui.SameLine();

                    var text = Loc.Localize("DalamudSettingsAutoUpdateOptInUnknownPlugin", "未知插件");
                    var textHeight = ImGui.CalcTextSize(text);
                    var before = ImGui.GetCursorPos();

                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (pluginLineHeight / 2) - (textHeight.Y / 2));
                    ImGui.Text(text);

                    ImGui.SetCursorPos(before);
                }

                ImGui.SameLine();
                ImGui.SetCursorPosX(windowSize.X - (ImGuiHelpers.GlobalScale * 320));
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (pluginLineHeight / 2) - (ImGui.GetFrameHeight() / 2));

                string OptKindToString(AutoUpdatePreference.OptKind kind)
                {
                    return kind switch
                    {
                        AutoUpdatePreference.OptKind.NeverUpdate => Loc.Localize("DalamudSettingsAutoUpdateOptInNeverUpdate", "从不更新"),
                        AutoUpdatePreference.OptKind.AlwaysUpdate => Loc.Localize("DalamudSettingsAutoUpdateOptInAlwaysUpdate", "总是更新"),
                        _ => throw new ArgumentOutOfRangeException(),
                    };
                }

                ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 250);
                using (var combo = ImRaii.Combo($"###autoUpdateBehavior{preference.WorkingPluginId}", OptKindToString(preference.Kind)))
                {
                    if (combo.Success)
                    {
                        foreach (var kind in Enum.GetValues<AutoUpdatePreference.OptKind>())
                        {
                            if (ImGui.Selectable(OptKindToString(kind)))
                            {
                                preference.Kind = kind;
                            }
                        }
                    }
                }

                ImGui.SameLine();
                ImGui.SetCursorPosX(windowSize.X - (ImGuiHelpers.GlobalScale * 30 * btnOffset) - 5);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (pluginLineHeight / 2) - (ImGui.GetFrameHeight() / 2));

                if (ImGuiComponents.IconButton($"###removePlugin{preference.WorkingPluginId}", FontAwesomeIcon.Trash))
                {
                    wantRemovePluginGuid = preference.WorkingPluginId;
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(Loc.Localize("DalamudSettingsAutoUpdateOptInRemove", "移除规则"));
            }

            if (wantRemovePluginGuid != null)
            {
                this.autoUpdatePreferences.RemoveAll(x => x.WorkingPluginId == wantRemovePluginGuid);
            }
        }

        void OnPluginPicked(LocalPlugin plugin)
        {
            var id = plugin.EffectiveWorkingPluginId;
            if (id == Guid.Empty)
                throw new InvalidOperationException("Plugin ID is empty.");

            this.autoUpdatePreferences.Add(new AutoUpdatePreference(id));
        }

        bool IsPluginDisabled(LocalPlugin plugin)
            => this.autoUpdatePreferences.Any(x => x.WorkingPluginId == plugin.EffectiveWorkingPluginId);

        bool IsPluginFiltered(LocalPlugin plugin)
            => !plugin.IsDev;

        var pickerId = DalamudComponents.DrawPluginPicker(
            "###autoUpdatePicker", ref this.pickerSearch, OnPluginPicked, IsPluginDisabled, IsPluginFiltered);

        const FontAwesomeIcon addButtonIcon = FontAwesomeIcon.Plus;
        var addButtonText = Loc.Localize("DalamudSettingsAutoUpdateOptInAdd", "添加规则");
        ImGuiHelpers.CenterCursorFor(ImGuiComponents.GetIconButtonWithTextWidth(addButtonIcon, addButtonText));
        if (ImGuiComponents.IconButtonWithText(addButtonIcon, addButtonText))
        {
            this.pickerSearch = string.Empty;
            ImGui.OpenPopup(pickerId);
        }

        base.Draw();
    }

    public override void Load()
    {
        var configuration = Service<DalamudConfiguration>.Get();

        this.behavior = configuration.AutoUpdateBehavior ?? AutoUpdateBehavior.None;
        this.updateDisabledPlugins = configuration.UpdateDisabledPlugins;
        this.chatNotification = configuration.SendUpdateNotificationToChat;
        this.checkPeriodically = configuration.CheckPeriodicallyForUpdates;
        this.autoUpdatePreferences = configuration.PluginAutoUpdatePreferences;

        base.Load();
    }

    public override void Save()
    {
        var configuration = Service<DalamudConfiguration>.Get();

        configuration.AutoUpdateBehavior = this.behavior;
        configuration.UpdateDisabledPlugins = this.updateDisabledPlugins;
        configuration.SendUpdateNotificationToChat = this.chatNotification;
        configuration.CheckPeriodicallyForUpdates = this.checkPeriodically;
        configuration.PluginAutoUpdatePreferences = this.autoUpdatePreferences;

        base.Save();
    }
}

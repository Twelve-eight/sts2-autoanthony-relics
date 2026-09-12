using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Adds the "东尼算法 - 遗物 settings" entry row to the vanilla settings
/// screen's General panel, beside AutoAnthony's and Qurious's own group rows
/// - the page lives OUTSIDE BaseLib's shared mod settings page (user order
/// 2026-09-13). Row pattern copied from QuriousCraftingRelics'
/// RelicsSettingsScreenPatch (duplicate the Modding row, relabel, wire the
/// button to push our dedicated submenu page).
///
/// Single-target patch class only (a class with two [HarmonyPatch] targets
/// patches only the last one).
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
internal static class AnthonyRelicsSettingsScreenPatch
{
    private static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            var panel = __instance.GetNode<NSettingsPanel>("%GeneralSettings");
            var content = panel.Content;
            var moddingRow = content.GetNodeOrNull<Control>("Modding");
            if (moddingRow is null || content.GetNodeOrNull("AutoAnthonyRelicsSettingsGroup") is not null)
            {
                return;
            }
            var divider = content.GetNodeOrNull<Node>("ModdingDivider");
            int insertionIndex = divider?.GetIndex(false) ?? content.GetChildCount();
            // Insert AFTER Qurious's / AutoAnthony's own group row when present,
            // else right after the Modding divider - visually "beside" them.
            var anthonyRow = content.GetNodeOrNull<Node>("AutoAnthonySettingsGroup");
            var quriousRow = content.GetNodeOrNull<Node>("QuriousCraftingRelicsSettingsGroup");
            if (quriousRow is not null)
            {
                insertionIndex = quriousRow.GetIndex(false) + 1;
            }
            else if (anthonyRow is not null)
            {
                insertionIndex = anthonyRow.GetIndex(false) + 1;
            }
            AddGroupRow(content, moddingRow, insertionIndex);
            panel.Call(NSettingsPanel.MethodName.RefreshSize);
            panel.Call(NSettingsPanel.MethodName.UpdateNavigation);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] settings row patch failed: {e.Message}");
        }
    }

    private static void AddGroupRow(VBoxContainer content, Control source, int insertionIndex)
    {
        var row = (Control)source.Duplicate(6); // signals+groups, like AutoAnthony
        row.Name = "AutoAnthonyRelicsSettingsGroup";
        FixOwnerRecursive(row, row);
        content.AddChild(row, false, 0);
        content.MoveChild(row, insertionIndex);
        row.Visible = true;
        SetLabel(row.GetNodeOrNull<Node>("Label"), TextOf("SETTINGS_GROUP"));

        var button = row.GetNodeOrNull<NOpenModdingScreenButton>("ModdingButton");
        if (button is null)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] duplicated settings row has no button");
            return;
        }
        button.Name = "AutoAnthonyRelicsSettingsGroupButton";
        ((NClickableControl)button).Enable();
        button.Connect(NButton.SignalName.Released, Callable.From<NButton>(_ => OpenDedicatedPage(row)), 0u);
        SetLabel(button.GetNodeOrNull<Node>("Label"), TextOf("SETTINGS_OPEN"));
    }

    /// <summary>Walk up to the settings screen's submenu stack and push our page.</summary>
    private static void OpenDedicatedPage(Node node)
    {
        for (var current = node; current is not null; current = current.GetParent())
        {
            if (current is NSubmenuStack stack)
            {
                stack.PushSubmenuType(typeof(AnthonyRelicsSettingsSubmenu));
                return;
            }
        }
        MainFile.Logger.Error($"[{MainFile.ModId}] could not locate a submenu stack for the settings page");
    }

    /// <summary>Re-own duplicated nodes (Godot Duplicate keeps old owner refs).</summary>
    private static void FixOwnerRecursive(Node node, Node newOwner)
    {
        if (node.Owner is not null && node.Owner != newOwner)
        {
            node.Owner = newOwner;
        }
        foreach (var child in node.GetChildren())
        {
            FixOwnerRecursive(child, newOwner);
        }
    }

    private static void SetLabel(Node? labelNode, string text)
    {
        if (labelNode is MegaCrit.Sts2.addons.mega_text.MegaLabel mega)
        {
            mega.SetTextAutoSize(text);
        }
        else if (labelNode is RichTextLabel rich)
        {
            rich.Text = text;
        }
    }

    private static string TextOf(string name)
    {
        string key = ModPrefix + name + ".title";
        var loc = MegaCrit.Sts2.Core.Localization.LocString.GetIfExists("settings_ui", key);
        return loc?.GetFormattedText() ?? key;
    }

    private static string ModPrefix =>
        typeof(AutoAnthonyRelicsConfig).Namespace is { } ns && ns.Length > 0
            ? ns.Split('.')[0].ToUpperInvariant() + "-"
            : "AUTOANTHONYRELICS-";
}

/// <summary>
/// GetSubmenuType interception so our page type can be pushed into the
/// main-menu submenu stack (AutoAnthony registry pattern).
/// </summary>
[HarmonyPatch(typeof(NMainMenuSubmenuStack), "GetSubmenuType", new Type[] { typeof(Type) })]
internal static class AnthonyRelicsSettingsSubmenuRegistrationPatch
{
    private static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        return AnthonyRelicsSettingsSubmenu.GetOrCreate((NSubmenuStack)(object)__instance, type, ref __result);
    }
}

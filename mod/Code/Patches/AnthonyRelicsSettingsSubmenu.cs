using System;
using System.Runtime.CompilerServices;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Dedicated settings page for AutoAnthonyRelics, OUTSIDE BaseLib's shared
/// "Mod Configuration" submenu - sitting beside AutoAnthony's own settings
/// entry in the vanilla settings screen (user order 2026-09-13: the page
/// must sit at the same level as the original AutoAnthony, not inside
/// BaseLib's mod settings).
///
/// Pattern copied from QuriousCraftingRelics' verified implementation, which
/// itself was copied from AutoAnthony's decompiled one:
/// - NSettingsScreen._Ready postfix adds a group row to the General panel.
/// - NMainMenuSubmenuStack.GetSubmenuType prefix lazily instantiates this
///   page into the stack (AutoAnthony registry pattern).
/// - The page hosts BaseLib's SimpleModConfig UI for the REGISTERED config
///   instance, with the same Changed()-debounce save wiring as BaseLib's own
///   NModConfigSubmenu (without it, edits never reach the cfg file).
/// </summary>
internal sealed partial class AnthonyRelicsSettingsSubmenu : NSubmenu
{
    private const double AutosaveDelay = 5.0;

    private Control? _initialFocus;
    private ModConfig? _config;
    private double _saveTimer = -1;

    protected override Control? InitialFocusedControl => _initialFocus;

    public override void _Ready()
    {
        try
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, (LayoutPresetMode)0, 0);
            GrowHorizontal = GrowDirection.Both;
            GrowVertical = GrowDirection.Both;

            var title = new Label
            {
                Text = TextOf("SETTINGS_PAGE_TITLE"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(title, false, 0);
            title.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, (LayoutPresetMode)0, 0);
            title.OffsetTop = 35f;
            title.OffsetBottom = 105f;
            title.AddThemeFontSizeOverride("font_size", 34);

            var scroll = new ScrollContainer
            {
                Name = "AutoAnthonyRelicsSettingsScroll",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
                AnchorLeft = 0.19f,
                AnchorRight = 0.81f,
                AnchorTop = 0f,
                AnchorBottom = 1f,
                OffsetTop = 115f,
                OffsetBottom = -105f,
                CustomMinimumSize = new Vector2(800f, 0f),
            };
            AddChild(scroll, false, 0);

            var options = new VBoxContainer
            {
                Name = "AutoAnthonyRelicsSettingsOptions",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(800f, 0f),
            };
            options.AddThemeConstantOverride("separation", 4);
            scroll.AddChild(options, false, 0);

            BuildOptions(options);

            var back = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache
                .GetScene(MegaCrit.Sts2.Core.Helpers.SceneHelper.GetScenePath("ui/back_button"))
                .Instantiate<NBackButton>();
            back.Name = "BackButton";
            AddChild(back, false, 0);
            ConnectSignals();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] settings page build failed: {e}");
        }
    }

    /// <summary>Host BaseLib's config UI for the REGISTERED config instance.</summary>
    private void BuildOptions(VBoxContainer options)
    {
        try
        {
            _config = ModConfigRegistry.Get(MainFile.ModId)
                ?? ModConfigRegistry.Get<AutoAnthonyRelicsConfig>();
            if (_config is null)
            {
                MainFile.Logger.Error($"[{MainFile.ModId}] no registered config; settings page read-only");
                options.AddChild(new Label
                {
                    Text = TextOf("SETTINGS_PAGE_UNAVAILABLE"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                }, false, 0);
                _initialFocus = options.GetChildOrNull<Control>(0);
                return;
            }

            _config.ConfigChanged += OnConfigChanged;
            _config.SetupConfigUI(options);
            _initialFocus = options.GetChildOrNull<Control>(0);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] config UI build failed: {e}");
            options.AddChild(new Label
            {
                Text = TextOf("SETTINGS_PAGE_UNAVAILABLE"),
                HorizontalAlignment = HorizontalAlignment.Center,
            }, false, 0);
        }
    }

    private void OnConfigChanged(object? sender, EventArgs e) => ScheduleSave();

    private void ScheduleSave() => _saveTimer = AutosaveDelay;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_saveTimer <= 0)
        {
            return;
        }
        _saveTimer -= delta;
        if (_saveTimer <= 0)
        {
            SaveNow();
        }
    }

    private void SaveNow()
    {
        _saveTimer = -1;
        try
        {
            _config?.Save();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[{MainFile.ModId}] config save failed: {e.Message}");
        }
    }

    /// <summary>Leaving the page flushes pending edits (BaseLib does the same).</summary>
    protected override void OnSubmenuHidden()
    {
        SaveNow();
        base.OnSubmenuHidden();
    }

    public override void _ExitTree()
    {
        SaveNow();
        if (_config is not null)
        {
            _config.ConfigChanged -= OnConfigChanged;
        }
        base._ExitTree();
    }

    /// <summary>
    /// Lazily create/attach the page into a submenu stack - the
    /// GetSubmenuType interception target (AutoAnthony registry pattern).
    /// </summary>
    internal static bool GetOrCreate(NSubmenuStack stack, Type type, ref NSubmenu result)
    {
        if (type != typeof(AnthonyRelicsSettingsSubmenu))
        {
            return true; // not ours: let vanilla handle it
        }
        if (!Pages.TryGetValue(stack, out var holder))
        {
            var page = new AnthonyRelicsSettingsSubmenu();
            page.Visible = false;
            page.SetAnchorsPreset(LayoutPreset.FullRect, false);
            page.Position = Vector2.Zero;
            page.Size = stack.Size;
            MegaCrit.Sts2.Core.Helpers.GodotTreeExtensions.AddChildSafely(stack, page);
            page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, (LayoutPresetMode)0, 0);
            holder = new PageHolder(page);
            Pages.Add(stack, holder);
        }
        result = holder.Page;
        return false;
    }

    private sealed class PageHolder(AnthonyRelicsSettingsSubmenu page)
    {
        internal AnthonyRelicsSettingsSubmenu Page { get; } = page;
    }

    private static readonly ConditionalWeakTable<NSubmenuStack, PageHolder> Pages = new();

    /// <summary>settings_ui lookup under {MODPREFIX}{NAME}.title (BaseLib convention).</summary>
    private static string TextOf(string name)
    {
        string key = ModPrefix + name + ".title";
        var loc = LocString.GetIfExists("settings_ui", key);
        return loc?.GetFormattedText() ?? key;
    }

    private static string ModPrefix =>
        typeof(AutoAnthonyRelicsConfig).Namespace is { } ns && ns.Length > 0
            ? ns.Split('.')[0].ToUpperInvariant() + "-"
            : "AUTOANTHONYRELICS-";
}

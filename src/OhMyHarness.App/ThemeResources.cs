namespace OhMyHarness.App;

internal static partial class FluentDesign
{
    internal static readonly string[] ThemeResourceKeys = [
        "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush", "TextFillColorDisabledBrush",
        "CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush", "SolidBackgroundFillColorBaseBrush",
        "ControlStrokeColorDefaultBrush", "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "AccentTextFillColorTertiaryBrush",
        "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush"
    ];
    static readonly Dictionary<string, string> aliases = BuildThemeAliases();
    static Dictionary<string, string> BuildThemeAliases()
    {
        var result = new Dictionary<string, string>();
        void Add(string target, string keys) { foreach (var key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries)) result[key] = target; }
        Add("TextFillColorPrimaryBrush", "DefaultTextForegroundThemeBrush SystemControlForegroundBaseHighBrush TextControlForeground TextControlForegroundPointerOver TextControlForegroundFocused TextControlHeaderForeground ComboBoxForeground ComboBoxForegroundPointerOver ComboBoxForegroundPressed ComboBoxForegroundFocused ComboBoxDropDownForeground ComboBoxItemForeground ComboBoxItemForegroundPointerOver ComboBoxItemForegroundPressed ComboBoxItemForegroundSelected ComboBoxItemForegroundSelectedPointerOver ComboBoxItemForegroundSelectedPressed ListViewItemForeground ListViewItemForegroundPointerOver ListViewItemForegroundSelected ListViewItemForegroundPressed MenuFlyoutItemForeground MenuFlyoutItemForegroundPointerOver MenuFlyoutItemForegroundPressed MenuFlyoutSubItemForeground MenuFlyoutSubItemForegroundPointerOver MenuFlyoutSubItemForegroundPressed ToggleMenuFlyoutItemForeground ToggleMenuFlyoutItemForegroundPointerOver ToggleMenuFlyoutItemForegroundPressed FlyoutPresenterForeground ContentDialogForeground ToolTipForeground ButtonForeground ButtonForegroundPointerOver ButtonForegroundPressed ToggleButtonForeground ToggleButtonForegroundPointerOver ToggleButtonForegroundPressed CheckBoxForeground CheckBoxForegroundPointerOver CheckBoxForegroundPressed RadioButtonForeground RadioButtonForegroundPointerOver RadioButtonForegroundPressed ExpanderHeaderForeground ExpanderHeaderForegroundPointerOver ExpanderHeaderForegroundPressed");
        Add("TextFillColorSecondaryBrush", "SystemControlForegroundBaseMediumHighBrush SystemControlPageTextBaseMediumBrush TextControlPlaceholderForeground TextControlPlaceholderForegroundPointerOver TextControlPlaceholderForegroundFocused ComboBoxPlaceHolderForeground ComboBoxPlaceHolderForegroundPointerOver ComboBoxPlaceHolderForegroundPressed ComboBoxDropDownGlyphForeground MenuFlyoutItemKeyboardAcceleratorTextForeground");
        Add("TextFillColorDisabledBrush", "TextControlForegroundDisabled TextControlPlaceholderForegroundDisabled ComboBoxForegroundDisabled ComboBoxItemForegroundDisabled ListViewItemForegroundDisabled MenuFlyoutItemForegroundDisabled MenuFlyoutSubItemForegroundDisabled ButtonForegroundDisabled ToggleButtonForegroundDisabled");
        Add("CardBackgroundFillColorDefaultBrush", "ControlFillColorDefaultBrush ControlFillColorInputActiveBrush ControlFillColorDisabledBrush LayerFillColorDefaultBrush SolidBackgroundFillColorSecondaryBrush AcrylicInAppFillColorDefaultBrush AcrylicInAppFillColorDefaultInverseBrush AcrylicInAppFillColorBaseBrush DesktopAcrylicTransparentBrush SystemControlBackgroundChromeMediumLowBrush TextControlBackground TextControlBackgroundFocused TextControlBackgroundDisabled ComboBoxBackground ComboBoxBackgroundDisabled ComboBoxDropDownBackground FlyoutPresenterBackground MenuFlyoutPresenterBackground ToolTipBackground ContentDialogBackground ContentDialogTopOverlay ContentDialogCommandSpaceBackground ButtonBackground ButtonBackgroundDisabled ToggleButtonBackground ToggleButtonBackgroundDisabled ExpanderHeaderBackground ExpanderContentBackground");
        Add("ControlHoverBrush", "ControlFillColorSecondaryBrush SubtleFillColorSecondaryBrush ButtonBackgroundPointerOver ToggleButtonBackgroundPointerOver TextControlBackgroundPointerOver ComboBoxBackgroundPointerOver ComboBoxItemBackgroundPointerOver ListViewItemBackgroundPointerOver MenuFlyoutItemBackgroundPointerOver MenuFlyoutSubItemBackgroundPointerOver ToggleMenuFlyoutItemBackgroundPointerOver ExpanderHeaderBackgroundPointerOver");
        Add("ControlPressedBrush", "ControlFillColorTertiaryBrush SubtleFillColorTertiaryBrush ButtonBackgroundPressed ToggleButtonBackgroundPressed ComboBoxBackgroundPressed ComboBoxItemBackgroundPressed ListViewItemBackgroundPressed MenuFlyoutItemBackgroundPressed MenuFlyoutSubItemBackgroundPressed ToggleMenuFlyoutItemBackgroundPressed ExpanderHeaderBackgroundPressed");
        Add("ControlSelectedBrush", "ListViewItemBackgroundSelected ComboBoxItemBackgroundSelected");
        Add("ControlSelectedHoverBrush", "ListViewItemBackgroundSelectedPointerOver ListViewItemBackgroundSelectedPressed ComboBoxItemBackgroundSelectedPointerOver ComboBoxItemBackgroundSelectedPressed");
        Add("TransparentBrush", "SubtleFillColorTransparentBrush SystemControlTransparentBrush ListViewItemBackground ComboBoxItemBackground MenuFlyoutItemBackground MenuFlyoutSubItemBackground ToggleMenuFlyoutItemBackground");
        Add("ControlStrokeColorDefaultBrush", "SurfaceStrokeColorFlyoutBrush DividerStrokeColorDefaultBrush FlyoutBorderThemeBrush MenuFlyoutPresenterBorderBrush ComboBoxDropDownBorderBrush ToolTipBorderBrush ContentDialogBorderBrush");
        Add("AccentFillColorDefaultBrush", "AccentButtonBackground ToggleButtonBackgroundChecked CheckBoxCheckBackgroundFillChecked CheckBoxCheckBackgroundFillCheckedPointerOver RadioButtonOuterEllipseFillChecked ToggleSwitchFillOn");
        Add("AccentFillColorSecondaryBrush", "AccentButtonBackgroundPointerOver ToggleButtonBackgroundCheckedPointerOver ToggleSwitchFillOnPointerOver");
        Add("AccentFillColorTertiaryBrush", "AccentButtonBackgroundPressed ToggleButtonBackgroundCheckedPressed ToggleSwitchFillOnPressed");
        Add("TextOnAccentFillColorPrimaryBrush", "AccentButtonForeground AccentButtonForegroundPointerOver AccentButtonForegroundPressed ToggleButtonForegroundChecked ToggleButtonForegroundCheckedPointerOver ToggleButtonForegroundCheckedPressed CheckBoxCheckGlyphForegroundChecked CheckBoxCheckGlyphForegroundCheckedPointerOver RadioButtonCheckGlyphFillChecked ToggleSwitchKnobFillOn ToggleSwitchKnobFillOnPointerOver ToggleSwitchKnobFillOnPressed");
        Add("AccentTextFillColorPrimaryBrush", "SystemControlHyperlinkTextBrush HyperlinkForeground HyperlinkForegroundPointerOver HyperlinkForegroundPressed SystemControlHighlightAccentBrush");
        Add("SelectionBrush", "TextControlSelectionHighlightColor AccentFillColorSelectedTextBackgroundBrush TextSelectionHighlightColorThemeBrush");
        Add("SelectedTextBrush", "TextOnAccentFillColorSelectedTextBrush");
        Add("AccentTextFillColorPrimaryBrush", "TextControlBorderBrushFocused ComboBoxBorderBrushFocused ComboBoxEditableTextBorderBrushFocused");
        Add("AccentFillColorDefaultBrush", "AccentButtonBorderBrush AccentButtonBorderBrushPointerOver AccentButtonBorderBrushPressed ProgressBarForeground ProgressRingForegroundThemeBrush");
        return result;
    }
}

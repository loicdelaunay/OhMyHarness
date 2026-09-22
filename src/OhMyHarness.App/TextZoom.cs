using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using Windows.System;

namespace OhMyHarness.App;

// Keep the original typography: headings, code and body text retain their proportions.
// Weak references allow old streamed messages and closed windows to be collected.
static class TextZoom
{
    sealed record Metrics(double FontSize, double LineHeight = 0);
    sealed record Dimensions(double Height);
    static readonly ConditionalWeakTable<DependencyObject, Metrics> originals = new();
    static readonly ConditionalWeakTable<Control, Dimensions> dimensions = new();
    static readonly List<WeakReference<FrameworkElement>> roots = [];
    public static int Percent { get; private set; } = 100;
    public static void Set(int percent)
    {
        Percent = Math.Clamp(percent, 80, 150);
        roots.RemoveAll(item => !item.TryGetTarget(out _));
        foreach (var item in roots) if (item.TryGetTarget(out var root)) Apply(root);
    }
    public static void Observe(FrameworkElement root)
    {
        roots.Add(new(root));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        // Coalesce streaming layout changes; new text receives the current scale.
        root.LayoutUpdated += (_, _) => { if (Percent != 100 && !timer.IsEnabled) timer.Start(); };
        root.Loaded += (_, _) => Apply(root);
        root.Unloaded += (_, _) => timer.Stop();
        timer.Tick += (_, _) => { timer.Stop(); Apply(root); };
    }
    public static void Apply(DependencyObject node)
    {
        var factor = Percent / 100d;
        if (node is IconElement) return;
        if (node is Control control && double.IsFinite(control.Height))
        {
            var basis = dimensions.GetValue(control, _ => new(control.Height));
            var height = basis.Height * Math.Max(1, factor);
            if (Math.Abs(control.Height - height) > .01) control.Height = height;
        }
        if (node is TextBlock text)
        {
            if (text.FontFamily.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase) || text.FontFamily.Source.Contains("Fluent Icons", StringComparison.OrdinalIgnoreCase)) return;
            var basis = originals.GetValue(text, _ => new(text.FontSize, text.LineHeight));
            if (Math.Abs(text.FontSize - basis.FontSize * factor) > .01) text.FontSize = basis.FontSize * factor;
            if (basis.LineHeight > 0 && Math.Abs(text.LineHeight - basis.LineHeight * factor) > .01) text.LineHeight = basis.LineHeight * factor;
            foreach (var inline in text.Inlines) ScaleInline(inline, factor);
            return;
        }
        if (node is TextBox input)
        {
            var basis = originals.GetValue(input, _ => new(input.FontSize));
            if (Math.Abs(input.FontSize - basis.FontSize * factor) > .01) input.FontSize = basis.FontSize * factor;
            return; // Its template inherits FontSize; never scale the text twice.
        }
        if (node is PasswordBox password)
        {
            var basis = originals.GetValue(password, _ => new(password.FontSize));
            if (Math.Abs(password.FontSize - basis.FontSize * factor) > .01) password.FontSize = basis.FontSize * factor;
            return;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Apply(VisualTreeHelper.GetChild(node, i));
    }
    static void ScaleInline(Inline inline, double factor)
    {
        // Inherited sizes follow the parent; only explicit heading/code sizes need an override.
        if (inline.ReadLocalValue(TextElement.FontSizeProperty) is double size)
        {
            var basis = originals.GetValue(inline, _ => new(size));
            if (Math.Abs(inline.FontSize - basis.FontSize * factor) > .01) inline.FontSize = basis.FontSize * factor;
        }
        if (inline is Span span) foreach (var child in span.Inlines) ScaleInline(child, factor);
    }
}

public sealed partial class MainWindow
{
    int fontWheelRemainder;
    static bool ControlPressed() => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    void ObserveTextZoom(FrameworkElement target)
    {
        TextZoom.Observe(target);
        target.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (!ControlPressed()) { fontWheelRemainder = 0; return; }
            var point = e.GetCurrentPoint(target);
            if (point.Properties.IsHorizontalMouseWheel || point.Properties.MouseWheelDelta == 0) return;
            e.Handled = true;
            fontWheelRemainder += point.Properties.MouseWheelDelta;
            var steps = fontWheelRemainder / 120;
            fontWheelRemainder %= 120;
            if (steps != 0) await Guard(() => SetFontZoomAsync(TextZoom.Percent + steps * 10));
        }), true);
        target.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(async (_, e) =>
        {
            if (!ControlPressed() || e.Key is not (VirtualKey.Number0 or VirtualKey.NumberPad0)) return;
            e.Handled = true; fontWheelRemainder = 0;
            await Guard(() => SetFontZoomAsync(100));
        }), true);
    }
    async Task SetFontZoomAsync(int percent)
    {
        percent = Math.Clamp(percent, 80, 150);
        if (percent == TextZoom.Percent) return;
        // A font reflow is not a user's scroll gesture.
        chatScrollInputUntil = 0; chatScrollMayResume = false;
        TextZoom.Set(percent);
        var config = FeatureSettings.Read(state.FeaturesJson);
        config.FontZoomPercent = percent; state.FeaturesJson = config.Json();
        await db.SaveChangesAsync();
    }
}

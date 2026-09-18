namespace OhMyHarness.Core;

public sealed record ScreenInfo(int Index, string Name, bool IsPrimary, int X, int Y, int Width, int Height);

public static class ScreenGeometry
{
    public static (int Width, int Height) CalculateScaledDimensions(int currentWidth, int currentHeight, int? maxWidth, int? maxHeight)
    {
        if (currentWidth <= 0 || currentHeight <= 0) return (1, 1);
        var scale = 1.0;
        if (maxWidth.HasValue && maxWidth.Value > 0 && currentWidth > maxWidth.Value)
            scale = Math.Min(scale, (double)maxWidth.Value / currentWidth);
        if (maxHeight.HasValue && maxHeight.Value > 0 && currentHeight > maxHeight.Value)
            scale = Math.Min(scale, (double)maxHeight.Value / currentHeight);
        return (Math.Max(1, (int)Math.Round(currentWidth * scale)), Math.Max(1, (int)Math.Round(currentHeight * scale)));
    }

    public static ScreenInfo? ResolveScreen(IReadOnlyList<ScreenInfo> screens, string? target)
    {
        if (screens.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(target) || target.Equals("primary", StringComparison.OrdinalIgnoreCase))
            return screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        if (int.TryParse(target, out var idx) && idx >= 0 && idx < screens.Count)
            return screens[idx];
        return screens.FirstOrDefault(s => s.Name.Contains(target, StringComparison.OrdinalIgnoreCase))
            ?? screens.FirstOrDefault(s => s.IsPrimary)
            ?? screens[0];
    }

    public static (int X, int Y, int Width, int Height) ResolveRegion(
        IReadOnlyList<ScreenInfo> screens,
        string? screenTarget,
        int? x, int? y, int? width, int? height,
        int virtualX, int virtualY, int virtualWidth, int virtualHeight)
    {
        if (x.HasValue && y.HasValue && width.HasValue && height.HasValue && width.Value > 0 && height.Value > 0)
        {
            return (x.Value, y.Value, width.Value, height.Value);
        }

        if (screenTarget != null && screenTarget.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return (virtualX, virtualY, virtualWidth, virtualHeight);
        }

        var screen = ResolveScreen(screens, screenTarget);
        if (screen != null)
        {
            return (screen.X, screen.Y, screen.Width, screen.Height);
        }

        return (virtualX, virtualY, virtualWidth, virtualHeight);
    }
}

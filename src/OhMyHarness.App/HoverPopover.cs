using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly TextBlock speedPopoverDetails = new() { TextWrapping = TextWrapping.Wrap, Width = 280 };

    void AttachHoverPopover(FrameworkElement anchor, FrameworkElement body, Flyout flyout, Action update)
    {
        // A hover flyout must not steal the pointer/focus from its trigger.
        flyout.ShowMode = FlyoutShowMode.Transient;
        flyout.OverlayInputPassThroughElement = root;
        flyout.Placement = FlyoutPlacementMode.Top;
        // Include the padding in the same hover surface as the content and its buttons.
        body = new Border { Child = body, Padding = new Thickness(12), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        flyout.Content = body;
        flyout.FlyoutPresenterStyle = new Style { TargetType = typeof(FlyoutPresenter) };
        flyout.FlyoutPresenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        bool opened = false;
        var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        static bool Inside(PointerRoutedEventArgs e, FrameworkElement element)
        {
            var p = e.GetCurrentPoint(element).Position;
            return p.X >= 0 && p.Y >= 0 && p.X <= element.ActualWidth && p.Y <= element.ActualHeight;
        }
        void Open()
        {
            dismiss.Stop();
            if (opened) return;
            opened = true; // Set before ShowAt, which can synchronously generate pointer events.
            update();
            flyout.ShowAt(anchor, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });
        }
        void Exit(object sender, PointerRoutedEventArgs e)
        {
            // Opening a popup can raise PointerExited without any physical pointer movement.
            if (Inside(e, anchor) || Inside(e, body)) return;
            dismiss.Stop(); dismiss.Start();
        }
        anchor.PointerEntered += (_, _) => Open();
        anchor.PointerExited += Exit;
        anchor.Tapped += (_, _) => Open();
        body.PointerEntered += (_, _) => dismiss.Stop();
        body.PointerExited += Exit;
        dismiss.Tick += (_, _) => { dismiss.Stop(); flyout.Hide(); };
        refresh.Tick += (_, _) => update();
        flyout.Opened += (_, _) => refresh.Start();
        flyout.Closed += (_, _) => { opened = false; dismiss.Stop(); refresh.Stop(); };
        anchor.Unloaded += (_, _) => { flyout.Hide(); dismiss.Stop(); refresh.Stop(); };
    }

    void AttachSpeedPopover()
    {
        speedStack.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AttachHoverPopover(speedStack, speedPopoverDetails, new Flyout { Content = speedPopoverDetails },
            () => RefreshSpeedTooltip(currentSpeedTracker));
    }
}

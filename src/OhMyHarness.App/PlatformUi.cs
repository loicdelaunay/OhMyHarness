using Microsoft.UI.Xaml;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    static void InitializePicker(object picker, Window window)
    {
#if WINDOWS
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
#endif
        // Uno's desktop pickers obtain the active native window from their platform extension.
    }
}

using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    bool imagePasteInProgress;

    void EnableImagePaste()
    {
        var shortcut = OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V";
        ToolTipService.SetToolTip(composer, WorkflowText("Coller une image avec ", "Paste an image with ") + shortcut);
        // Paste covers the text box context menu on WinUI/Skia. macOS Uno does not
        // currently raise it, so the keyboard shortcut is handled separately below.
        composer.Paste += (_, e) =>
        {
            if (TryStartImagePaste()) e.Handled = true;
        };
        composer.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.V || !ImagePasteShortcutDown()) return;
            if (TryStartImagePaste()) e.Handled = true;
        };
    }

    static bool ImagePasteShortcutDown()
    {
        if (OperatingSystem.IsMacOS())
            return ClipboardImages.MacPasteShortcutDown() ||
                (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows) & CoreVirtualKeyStates.Down) != 0 ||
                (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows) & CoreVirtualKeyStates.Down) != 0;
        return (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
    }

    bool TryStartImagePaste()
    {
        if (imagePasteInProgress) return true;
        byte[]? native;
        DataPackageView? view = null;
        try
        {
            native = ClipboardImages.ReadNative();
            if (native == null)
            {
                view = Clipboard.GetContent();
                if (!ClipboardImages.HasWinRtBitmap(view)) return false;
            }
        }
        catch (Exception ex)
        {
            ShowStatus(WorkflowText("Impossible de lire l’image du presse-papiers : ", "Cannot read clipboard image: ") + ex.Message, StatusKind.Error);
            return false;
        }

        imagePasteInProgress = true;
        _ = Guard(async () =>
        {
            try
            {
                var encoded = native ?? await ClipboardImages.ReadWinRtBitmapAsync(view!);
                var png = await Task.Run(() => ClipboardImages.NormalizePng(encoded));
                AddPendingImage("presse-papiers-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png", "image/png", png);
                composer.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            }
            finally { imagePasteInProgress = false; }
        });
        return true;
    }

    void AddPendingImage(string name, string mime, byte[] bytes)
    {
        if (pendingImages.Count >= 4) throw new InvalidOperationException(WorkflowText("Quatre images maximum par message.", "Maximum four images per message."));
        if (bytes.Length is 0 or > 8 * 1024 * 1024) throw new InvalidOperationException(WorkflowText("Image trop volumineuse (8 Mo maximum).", "Image too large (8 MB maximum)."));
        pendingImages.Add(new Attachment { Name = name, Mime = mime, Data = bytes });
        composerInfoExpanded = true;
        UpdateInfoPanel();
        UpdateAttachments();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Windows.System;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    bool draggingChatScroll, manipulatingChatScroll, chatTailQueued, chatScrollMayResume;
    long chatScrollInputUntil;

    bool IsChatScrollInput(object source)
    {
        // Reasoning and code blocks own their scroll position independently.
        for (var node = source as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer viewer && (viewer == scroll || viewer.ScrollableHeight > 0)) return viewer == scroll;
        return false;
    }

    void SetChatFollow(bool follow)
    {
        followChatTail = follow;
        autoScrollButton.IsChecked = follow;
    }

    void ObserveChatScroll()
    {
        scroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, e) =>
        {
            if (ControlPressed()) return;
            if (!IsChatScrollInput(e.OriginalSource)) return;
            var delta = e.GetCurrentPoint(scroll).Properties.MouseWheelDelta;
            if (e.GetCurrentPoint(scroll).Properties.IsHorizontalMouseWheel || delta == 0) return;
            chatScrollInputUntil = Environment.TickCount64 + 750;
            chatScrollMayResume = delta < 0;
            if (delta > 0) SetChatFollow(false);
        }), true);
        scroll.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (!IsChatScrollInput(e.OriginalSource)) return;
            if (e.Key is not (VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home or VirtualKey.End or VirtualKey.Space)) return;
            chatScrollInputUntil = Environment.TickCount64 + 750;
            bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            chatScrollMayResume = !(e.Key is VirtualKey.Up or VirtualKey.PageUp or VirtualKey.Home || (e.Key == VirtualKey.Space && shift));
            if (!chatScrollMayResume) SetChatFollow(false);
        }), true);
        scroll.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!IsChatScrollInput(e.OriginalSource)) return;
            chatScrollMayResume = false;
            draggingChatScroll = e.Pointer.PointerDeviceType is PointerDeviceType.Touch or PointerDeviceType.Pen;
            for (var node = e.OriginalSource as DependencyObject; node != null && node != scroll; node = VisualTreeHelper.GetParent(node))
                if (node is ScrollBar) draggingChatScroll = true;
        }), true);
        void EndDrag(object sender, PointerRoutedEventArgs e)
        {
            if (draggingChatScroll) chatScrollInputUntil = Environment.TickCount64 + 750;
            draggingChatScroll = false;
        }
        scroll.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(EndDrag), true);
        scroll.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(EndDrag), true);
#if WINDOWS
        scroll.DirectManipulationStarted += (_, _) => manipulatingChatScroll = true;
        scroll.DirectManipulationCompleted += (_, _) => { manipulatingChatScroll = false; chatScrollInputUntil = Environment.TickCount64 + 750; };
        scroll.ViewChanging += (_, e) =>
        {
            if (!(draggingChatScroll || manipulatingChatScroll)) return;
            var delta = e.NextView.VerticalOffset - scroll.VerticalOffset;
            if (Math.Abs(delta) > 1) chatScrollMayResume = delta > 0;
            if (delta < -1) SetChatFollow(false);
        };
#else
        double lastOffset=0;
        scroll.ViewChanged += (_, _) =>
        {
            var delta=scroll.VerticalOffset-lastOffset; lastOffset=scroll.VerticalOffset;
            if (!draggingChatScroll && Environment.TickCount64 >= chatScrollInputUntil) return;
            if (delta < -1) SetChatFollow(false);
            if (Math.Abs(delta)>1) chatScrollMayResume=delta>0;
        };
#endif
        scroll.ViewChanged += async (_, _) =>
        {
            if (chatScrollMayResume && (draggingChatScroll || manipulatingChatScroll || Environment.TickCount64 < chatScrollInputUntil)
                && scroll.ScrollableHeight - scroll.VerticalOffset <= 8) SetChatFollow(true);
            await LoadHistoryAtTopAsync();
        };
        scroll.LayoutUpdated += (_, _) =>
        {
            // Content can grow or shrink without any user gesture (streaming, images, resize).
            if (followChatTail && scroll.ScrollableHeight - scroll.VerticalOffset > 1) QueueChatTail();
        };
    }

    void QueueChatTail()
    {
        if (chatTailQueued) return;
        chatTailQueued = true;
        var target = scroll.Content;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            chatTailQueued = false;
            if (ReferenceEquals(scroll.Content, target) && followChatTail)
                scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
        })) chatTailQueued = false;
    }
}

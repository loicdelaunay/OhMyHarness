# Fedora Linux x64

OhMyHarness 1.9.0 provides GUI and CLI archives for Fedora 44 x64 in the [GUI release](https://github.com/loicdelaunay/OhMyHarness/releases/tag/v1.9.0) and [CLI release](https://github.com/loicdelaunay/OhMyHarness/releases/tag/cli-v1.9.0). Choose the `linux-x64.tar.gz` asset, check its SHA-256 against the attached `SHA256SUMS.txt`, and extract it into a **writable, private** folder:

```bash
mkdir -p ~/Applications/OhMyHarness
tar -xzf OhMyHarness-v1.9.0-linux-x64.tar.gz -C ~/Applications/OhMyHarness
~/Applications/OhMyHarness/OhMyHarness.App
```

For the CLI, extract `OhMyHarness-CLI-v1.9.0-linux-x64.tar.gz` into a folder and run `./omh` from your project directory. Enter `/connect` to configure a provider. Both downloads bundle .NET, Python and the multilingual embedding model. They create their own SQLite database and resources beside the executable; no user data ships in the archive.

The GUI uses Uno's X11 desktop backend. A Fedora Workstation Wayland session can use XWayland. Install system graphics, font, DBus, GTK3 and WebKitGTK packages if absent, for example `sudo dnf install libXrandr libXi mesa-libGL fontconfig dbus-daemon gtk3 webkit2gtk4.1`. Set `GDK_BACKEND=x11` if WebKitGTK does not appear under Wayland. The embedded browser uses the system WebKitGTK runtime. [Uno's Linux desktop guide](https://platform.uno/docs/articles/features/using-skia-desktop.html) and [WebView requirements](https://platform.uno/docs/articles/controls/WebView.html) describe these system components.

Desktop mouse/keyboard automation, window listing and screen capture have no Linux implementation yet. Source, terminal, Git, agent, memory and RAG features are available. Chrome MCP can provide optional browser automation when configured. Linux updates currently require replacing the executable manually after closing it.

API keys are encrypted using a random key in `.linux-key` beside `database.sqlite`. Its permission is restricted to the current user. **Treat the complete folder as sensitive:** a reader of both files can recover provider keys. Copy the database and `.linux-key` together for backup or migration; never publish either. If the file permission is too broad, the app refuses to use it rather than silently weakening the protection.

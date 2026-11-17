<img src="assets/icon-256.png" width="96" alt="G7 Monitor Desktop icon">

# G7 Monitor Desktop

Windows desktop companion of [G7 Monitor](https://github.com/federico-castellani/g7monitor): shows the glucose readings collected by [G7 Monitor Server](https://github.com/federico-castellani/g7monitor-server) on the Windows 11 taskbar, live.

Designed to match the **Windows 11 Fluent Design Language**: WinUI colours for light and dark theme, rounded corners, Segoe UI Variable typography.

> ⚠️ **Not a medical device.** Personal/experimental project: do not use it for treatment decisions.

**[Download the installer](https://github.com/federico-castellani/g7monitor-desktop/releases/latest)**

---

## ✨ Features & Windows 11 Design

* 💊 **Quick-Glance Taskbar Widget**:
  * Sits directly on your Windows 11 taskbar next to the system tray.
  * Shows your **current glucose value**, **trend arrow**, **delta change**, and **status dot** (Emerald for in-target, Coral for low, Amber for high).
  * Seamless Windows 11 taskbar button styling with smooth mouse hover effects.
  * No unwanted notification popups or clutter.
* 📈 **Windows 11 Flyout**:
  * Click the taskbar widget to open a flyout styled like the system ones (Quick Settings, Calendar): WinUI colour tokens for light and dark theme, Segoe UI Variable type ramp, rounded corners, your accent colour.
  * Current value with a vector trend arrow and an "In range / Low / High" badge, the last 3 hours as a time-based chart (one dot per reading coloured by range, target band, clock times; hover a dot to read it), time in range, average and min–max.
  * Footer with connection status, a shortcut to the G7 Monitor website and the settings button; `Esc` or a click outside closes it.
  * Sharp text at any scaling: everything is drawn in 96-DPI units and scaled to the screen DPI; ClearType on the opaque flyout, grayscale anti-aliasing on the transparent taskbar widget.
* ⚙️ **In-Flyout Modern Settings**:
  * Click the `⚙` icon inside the flyout to smoothly toggle the in-line settings panel.
  * Connect to your **G7 Monitor Server**, set **Target Ranges**, widget background opacity, fullscreen behaviour and the **Windows Startup Toggle** directly inside the card—no separate legacy dialogs!
* 📡 **G7 Monitor Server data source**:
  * Enter the server address (e.g. `glicemie.example.com`), username, password and, if enabled, the 2FA code, then press **Connect**.
  * The PC is linked as a read-only device (`POST /api/v1/devices`, `kind: "reader"`): the password is used once and never stored; only the device token is kept, encrypted with Windows DPAPI.
  * Readings arrive in real time over Server-Sent Events (`GET /api/v1/readings/stream`), with automatic fallback to polling (`GET /api/v1/readings`) on older servers and reconnection with backoff (5 s → 5 min).
  * The device appears in the website profile as *G7 Monitor Desktop · PC name* and can be revoked from there, or with **Unlink** in the settings.
  * Readings older than 15 minutes are shown greyed out with their age.
* 🔐 **Credentials**:
  * The password and the 2FA code are sent once, only to the address you typed (HTTPS; plain HTTP only for this PC), and never stored: they are cleared from the form when the settings page closes. Redirects are refused, so a server cannot forward them elsewhere.
  * Only the read-only device token is kept, encrypted with Windows DPAPI (readable only by your Windows account) in `%APPDATA%\G7MonitorDesktop`, a folder accessible only to your account and SYSTEM. Unlink (or uninstalling) removes it; it can also be revoked from the website.
* 🌍 **Languages**: Italian and English. *System* follows the Windows display language; change it in Settings → Language.
* 🛡️ **Clean for antivirus software**:
  * No global hooks, no injected code, no packers or obfuscation, no admin rights (`asInvoker` manifest), full version information in the executable.
  * Network traffic only to the server you configure; settings in `%APPDATA%\G7MonitorDesktop`, autostart through the standard `HKCU\…\Run` value.

---

## 📁 Project Structure

```
G7-Monitor-Desktop/
├── src/
│   ├── Interop/NativeMethods.cs            # Win32: DWM frame, taskbar position, layered window
│   ├── Models/AppConfig.cs                 # Settings (JSON + DPAPI-encrypted token), autostart
│   ├── Models/GlucoseEntry.cs              # One reading
│   ├── Services/G7MonitorSyncService.cs    # Pairing, live stream (SSE) and polling
│   ├── UI/Theme.cs                         # Windows 11 colour tokens, fonts, drawing helpers
│   ├── UI/TaskbarWidget.cs                 # Reading on the taskbar
│   ├── UI/FlyoutWindow.cs                  # Flyout: graph, statistics, settings
│   ├── Properties/AssemblyInfo.cs          # Version and product information
│   ├── app.manifest                        # asInvoker, DPI awareness, Windows 10/11
│   └── Program.cs                          # Entry point
├── assets/                                 # App icon (.ico, PNG)
├── installer/G7MonitorDesktop.iss              # Inno Setup script
├── build.bat                               # Builds bin\G7MonitorDesktop.exe and dist\G7MonitorDesktop-Setup-<version>.exe
└── LICENSE
```

---

## Install

Run `dist\G7MonitorDesktop-Setup-2.0.0.exe`. It installs for the current user only (no administrator rights) into `%LOCALAPPDATA%\Programs\G7 Monitor Desktop`, adds a Start menu entry and, optionally, starts the app with Windows. Uninstall from *Settings → Apps*: it also removes the settings and the device token.

The installer is not code-signed, so SmartScreen may show *"Windows protected your PC"* the first time: choose *More info → Run anyway*.

## Use

* **Left-click the widget**: opens/closes the flyout with the graph.
* **⚙ in the flyout**: connect to your server, change targets and options. For a server on this PC use `http://localhost:8080` (plain `http://` is accepted only for the local machine).
* **Right-click the widget**: menu with *Open* and *Exit*.

---

## Building from Source

Run `build.bat`. It uses the C# compiler included in Windows (.NET Framework 4.x) to build `bin\G7MonitorDesktop.exe`, then, if [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed, the installer in `dist\`. The version comes from `src/Properties/AssemblyInfo.cs`.

## License

[MIT License](LICENSE)

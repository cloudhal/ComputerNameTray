using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TrayInfo;

/// <summary>
///  Runs the app as a tray-only process (no main window). Shows the local
///  computer name in the system tray via a <see cref="NotifyIcon"/>.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly string _computerName = Environment.MachineName;

    // Info rows in the right-click menu; text/value refreshed each time it opens.
    private readonly ToolStripMenuItem _ipItem = new();
    private readonly ToolStripMenuItem _userItem = new();
    private readonly ToolStripMenuItem _osItem = new();
    private readonly ToolStripMenuItem _modelItem = new();
    private readonly ToolStripMenuItem _serialItem = new();
    private readonly ToolStripMenuItem _diskItem = new();
    private readonly ToolStripMenuItem _uptimeItem = new();
    private readonly ToolStripMenuItem _startupItem = new("Start with Windows");

    // Hidden control used to marshal background events (network/display changes)
    // back onto the UI thread.
    private readonly Control _sync = new();

    // These come from WMI/registry (slow-ish) and never change, so cache them.
    private string? _model;
    private string? _serial;
    private string? _os;

    // Tracks the current tray icon and its native HICON so we can free the old
    // one when the icon is regenerated (e.g. on a DPI change).
    private Icon? _currentIcon;
    private IntPtr _iconHandle;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "TrayInfo";

    public TrayApplicationContext()
    {
        // Realize the hidden control's handle on the UI thread so background
        // event handlers can marshal work back to it.
        _ = _sync.Handle;

        var menu = new ContextMenuStrip();

        // Non-clickable header showing the full name.
        var header = new ToolStripMenuItem(_computerName)
        {
            Enabled = false,
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // Click-to-copy info rows.
        foreach (var item in new[] { _ipItem, _userItem, _osItem, _modelItem, _serialItem, _diskItem, _uptimeItem })
        {
            item.Click += (_, _) => CopyItem(item);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy all details", null, (_, _) => CopyAll());
        menu.Items.Add("Copy name", null, (_, _) => CopyName());
        _startupItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        // Refresh dynamic values (IP, uptime, ...) each time the menu opens.
        menu.Opening += (_, _) => RefreshInfo();

        _notifyIcon = new NotifyIcon
        {
            Text = _computerName, // hover tooltip; enriched with IP below
            ContextMenuStrip = menu,
        };
        SetTrayIcon();
        _notifyIcon.Visible = true;

        // Double-click copies the name.
        _notifyIcon.DoubleClick += (_, _) => CopyName();

        UpdateTooltip();

        // Keep the IP tooltip current and re-render the icon crisply if the
        // display scaling (DPI) changes — e.g. when docking to another monitor.
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Ask Windows 11 to keep this icon out of the overflow flyout.
        EnsurePromoted();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => Post(UpdateTooltip);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Post(SetTrayIcon);

    /// <summary>Runs the action on the UI thread (events fire on background threads).</summary>
    private void Post(Action action)
    {
        if (_sync.IsHandleCreated)
        {
            _sync.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>Generates the tray icon and frees the previous native handle.</summary>
    private void SetTrayIcon()
    {
        var (icon, handle) = CreateTextIcon(_computerName);
        _notifyIcon.Icon = icon;

        // NotifyIcon does not own the HICON, so release the previous one now that
        // the new icon is installed.
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
        _currentIcon?.Dispose();
        _currentIcon = icon;
        _iconHandle = handle;
    }

    /// <summary>Recomputes and refreshes every info row's label and copy value.</summary>
    private void RefreshInfo()
    {
        SetInfo(_ipItem, "IP address", GetLocalIPv4());
        SetInfo(_userItem, "User", $"{Environment.UserDomainName}\\{Environment.UserName}");

        _os ??= GetOsVersion();
        SetInfo(_osItem, "OS", _os);

        if (_model is null)
        {
            (_model, _serial) = GetModelAndSerial();
        }
        SetInfo(_modelItem, "Model", _model);
        SetInfo(_serialItem, "Serial", _serial ?? "unknown");

        SetInfo(_diskItem, "Disk", GetFreeDiskSpace());
        SetInfo(_uptimeItem, "Uptime", FormatUptime());

        _startupItem.Checked = IsStartupEnabled();
        UpdateTooltip();
    }

    /// <summary>Copies every field as a labeled block, handy for pasting into a ticket.</summary>
    private void CopyAll()
    {
        RefreshInfo(); // ensure values are current even if invoked programmatically

        var rows = new[] { _ipItem, _userItem, _osItem, _modelItem, _serialItem, _diskItem, _uptimeItem };
        var lines = new List<string> { $"Computer:    {_computerName}" };
        lines.AddRange(rows.Select(r => r.Text ?? string.Empty));

        var text = string.Join(Environment.NewLine, lines);
        Clipboard.SetText(text);
        _notifyIcon.ShowBalloonTip(2000, "All details copied", _computerName, ToolTipIcon.Info);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Adds/removes the HKCU Run entry so the app auto-starts on login.</summary>
    private void ToggleStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (IsStartupEnabled())
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            else if (Environment.ProcessPath is { } exePath)
            {
                key.SetValue(RunValueName, $"\"{exePath}\"");
            }
        }
        catch
        {
            // Registry not writable — leave the checkbox reflecting reality below.
        }

        _startupItem.Checked = IsStartupEnabled();
    }

    /// <summary>Sets a row to "Label:  value" and stashes the raw value for copying.</summary>
    private static void SetInfo(ToolStripMenuItem item, string label, string value)
    {
        item.Text = $"{label}:  {value}";
        item.Tag = value;
    }

    private void CopyItem(ToolStripMenuItem item)
    {
        if (item.Tag is string value && value.Length > 0)
        {
            Clipboard.SetText(value);
            _notifyIcon.ShowBalloonTip(1500, "Copied", value, ToolTipIcon.Info);
        }
    }

    private void UpdateTooltip()
    {
        // NotifyIcon.Text is capped at 127 chars; name + IP fits comfortably.
        _notifyIcon.Text = $"{_computerName}\n{GetLocalIPv4()}";
    }

    private static string GetLocalIPv4()
    {
        // Ask the OS which local address it would use for outbound traffic. No
        // packets are sent for a UDP socket; this just resolves the route.
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                return ep.Address.ToString();
            }
        }
        catch
        {
            // Offline / no route — fall through.
        }

        try
        {
            var addr = Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return addr?.ToString() ?? "unavailable";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static (string model, string serial) GetModelAndSerial()
    {
        var model = "unknown";
        var serial = "unknown";

        try
        {
            string? manufacturer = null, rawModel = null;
            using (var search = new ManagementObjectSearcher(
                "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            {
                foreach (var o in search.Get())
                {
                    manufacturer = (o["Manufacturer"] as string)?.Trim();
                    rawModel = (o["Model"] as string)?.Trim();
                    break;
                }
            }

            // Lenovo stores the machine-type code in Model and the friendly name
            // (e.g. "ThinkPad X1 Carbon Gen 13") in ComputerSystemProduct.Version.
            if (manufacturer is not null &&
                manufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase))
            {
                using var search = new ManagementObjectSearcher(
                    "SELECT Version FROM Win32_ComputerSystemProduct");
                foreach (var o in search.Get())
                {
                    if ((o["Version"] as string)?.Trim() is { Length: > 0 } friendly)
                    {
                        rawModel = friendly;
                    }
                    break;
                }
            }

            var combined = string.Join(" ", new[] { manufacturer, rawModel }
                .Where(s => !string.IsNullOrEmpty(s)));
            if (combined.Length > 0)
            {
                model = combined;
            }
        }
        catch { /* WMI unavailable — keep "unknown" */ }

        try
        {
            using var search = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
            foreach (var o in search.Get())
            {
                if ((o["SerialNumber"] as string)?.Trim() is { Length: > 0 } sn)
                {
                    serial = sn;
                }
                break;
            }
        }
        catch { /* keep "unknown" */ }

        return (model, serial);
    }

    private static string GetOsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null)
            {
                return Environment.OSVersion.VersionString;
            }

            var product = key.GetValue("ProductName") as string ?? "Windows";
            var display = key.GetValue("DisplayVersion") as string; // e.g. "24H2"
            var build = key.GetValue("CurrentBuildNumber") as string;
            var ubr = key.GetValue("UBR") is int u ? u : (int?)null;

            // The registry still reports "Windows 10 …" on Windows 11; the build
            // number is the reliable discriminator (Win11 == build >= 22000).
            if (int.TryParse(build, out var b) && b >= 22000)
            {
                product = product.Replace("Windows 10", "Windows 11");
            }

            var displayText = string.IsNullOrEmpty(display) ? "" : $" {display}";
            var buildText = build is null
                ? ""
                : ubr is null ? $" (Build {build})" : $" (Build {build}.{ubr})";
            return $"{product}{displayText}{buildText}";
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    private static string GetFreeDiskSpace()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            const double gb = 1024d * 1024d * 1024d;
            var freeGb = drive.AvailableFreeSpace / gb;
            var totalGb = drive.TotalSize / gb;
            return $"{freeGb:0} GB free of {totalGb:0} GB ({drive.Name.TrimEnd('\\')})";
        }
        catch
        {
            return "unavailable";
        }
    }

    private static string FormatUptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        }
        return t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m" : $"{t.Minutes}m";
    }

    /// <summary>
    ///  Windows 11 hides new tray icons in the overflow flyout by default. The
    ///  show/hide choice lives in HKCU\Control Panel\NotifyIconSettings, keyed by
    ///  executable path, as an IsPromoted DWORD (1 = always visible). Windows only
    ///  creates our entry after the icon has been shown once, so we poll for it,
    ///  set the flag, then re-create the icon so Explorer picks up the change.
    /// </summary>
    private void EnsurePromoted()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null)
        {
            return;
        }

        // Poll: Windows writes the entry asynchronously after the first show.
        var attempts = 0;
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) =>
        {
            attempts++;
            var result = TryPromote(exePath);
            if (result != PromoteResult.NotFoundYet || attempts >= 10)
            {
                timer.Stop();
                timer.Dispose();
            }

            // If we just flipped it on, re-create the icon so it moves out of
            // the overflow without waiting for an Explorer restart.
            if (result == PromoteResult.Promoted)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Visible = true;
            }
        };
        timer.Start();
    }

    private enum PromoteResult { NotFoundYet, AlreadyPromoted, Promoted, Failed }

    private static PromoteResult TryPromote(string exePath)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                @"Control Panel\NotifyIconSettings", writable: true);
            if (root is null)
            {
                return PromoteResult.Failed;
            }

            foreach (var name in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(name, writable: true);
                if (sub?.GetValue("ExecutablePath") is not string path ||
                    !string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (sub.GetValue("IsPromoted") is int promoted && promoted == 1)
                {
                    return PromoteResult.AlreadyPromoted;
                }

                sub.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                return PromoteResult.Promoted;
            }

            return PromoteResult.NotFoundYet;
        }
        catch
        {
            return PromoteResult.Failed;
        }
    }

    private void CopyName()
    {
        Clipboard.SetText(_computerName);
        _notifyIcon.ShowBalloonTip(1500, "Computer name copied", _computerName, ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
        _sync.Dispose();
        ExitThread();
    }

    /// <summary>
    ///  Builds a tray icon showing the last name segment (e.g. "167" for
    ///  "HPT-LAP-167"). The colored background fills the whole icon and the font
    ///  is auto-scaled to fill it, so the digits are as large as the tray slot
    ///  physically allows.
    /// </summary>
    private static (Icon icon, IntPtr handle) CreateTextIcon(string name)
    {
        var label = LabelFor(name);

        // Render at the actual tray icon size so Windows doesn't downscale (and
        // blur/shrink) the text. Falls back to 32 on odd systems.
        var size = Math.Max(SystemInformation.SmallIconSize.Width, 16);

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Fill the entire icon (rounded square) so text gets the whole canvas
            // and stays readable on both light and dark taskbars.
            using (var background = new SolidBrush(Color.FromArgb(0, 31, 51))) // dark blue
            {
                var radius = size / 12f; // near-square, just barely softened corners
                using var path = RoundedRect(size, radius);
                g.FillPath(background, path);
            }

            // Measure and draw with the SAME typographic format so the fitted
            // font can't overflow and clip.
            var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            using var font = FitFont(g, label, size, format);
            using var textBrush = new SolidBrush(Color.White);
            g.DrawString(label, font, textBrush, new RectangleF(0, 0, size, size), format);
        }

        // Return both the managed Icon and its native HICON; the caller is
        // responsible for freeing the handle (via DestroyIcon) when replacing it.
        var hicon = bitmap.GetHicon();
        return (Icon.FromHandle(hicon), hicon);
    }

    /// <summary>Largest bold Segoe UI font whose text fits within the icon.</summary>
    private static Font FitFont(Graphics g, string text, int size, StringFormat format)
    {
        // Step down from the icon height until the text fits within the canvas.
        for (var pt = size; pt >= 6; pt--)
        {
            var font = new Font("Segoe UI", pt, FontStyle.Bold, GraphicsUnit.Pixel);
            var m = g.MeasureString(text, font, new SizeF(size * 4f, size * 4f), format);
            if (m.Width <= size * 0.90f && m.Height <= size * 0.98f)
            {
                return font;
            }
            font.Dispose();
        }
        return new Font("Segoe UI", 6, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static GraphicsPath RoundedRect(int size, float radius)
    {
        var d = radius * 2;
        // Inset by 1px: valid pixels are 0..size-1, so keep the right/bottom arcs
        // inside the bitmap or they clip and those corners look squared off.
        float max = size - 1;
        var path = new GraphicsPath();
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(max - d, 0, d, d, 270, 90);
        path.AddArc(max - d, max - d, d, d, 0, 90);
        path.AddArc(0, max - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    ///  Picks the last '-', '_' or '.' separated segment of the name (up to 4
    ///  chars), which is typically the unique part. Falls back to the first two
    ///  alphanumerics, then "PC".
    /// </summary>
    private static string LabelFor(string name)
    {
        var lastSegment = name
            .Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(s => s.Any(char.IsLetterOrDigit));

        if (lastSegment is not null)
        {
            return new string(lastSegment.Where(char.IsLetterOrDigit).Take(4).ToArray())
                .ToUpperInvariant();
        }

        var fallback = new string(name.Where(char.IsLetterOrDigit).Take(2).ToArray())
            .ToUpperInvariant();
        return fallback.Length > 0 ? fallback : "PC";
    }
}

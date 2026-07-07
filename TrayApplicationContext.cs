using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Microsoft.Win32;

namespace ComputerNameTray;

/// <summary>
///  Runs the app as a tray-only process (no main window). Shows the local
///  computer name in the system tray via a <see cref="NotifyIcon"/>.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly string _computerName = Environment.MachineName;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        // Non-clickable header showing the full name.
        var header = new ToolStripMenuItem(_computerName) { Enabled = false };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Copy name", null, (_, _) => CopyName());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTextIcon(_computerName),
            // Tooltip is capped at 63 chars by Windows; machine names are <= 15.
            Text = _computerName,
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Double-click copies the name.
        _notifyIcon.DoubleClick += (_, _) => CopyName();

        // Ask Windows 11 to keep this icon out of the overflow flyout.
        EnsurePromoted();
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
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    /// <summary>
    ///  Builds a 32x32 tray icon showing the last name segment (e.g. "167" for
    ///  "HPT-LAP-167") — usually the most distinctive part of a machine name.
    /// </summary>
    private static Icon CreateTextIcon(string name)
    {
        var label = LabelFor(name);

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(0, 120, 215)); // Windows accent blue
            g.FillEllipse(background, 0, 0, 31, 31);

            // Shrink the font as the label gets longer so it still fits.
            var fontSize = label.Length switch { <= 2 => 15f, 3 => 12f, _ => 9f };
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(label, font, textBrush, new RectangleF(0, 0, 32, 32), format);
        }

        // Convert to an Icon handle; the caller keeps this alive via NotifyIcon.
        return Icon.FromHandle(bitmap.GetHicon());
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

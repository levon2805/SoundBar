using System.Diagnostics;
using System.Runtime.InteropServices;
using SoundBar.Shared;

namespace SoundBarTaskbar
{
    /// <summary>
    /// The indicator on the taskbar, and the hidden window that runs it.
    ///
    /// The indicator is a see-through child window of the taskbar itself (like the one in Explorer's own
    /// notification area). Living inside the taskbar means it hides with it for full-screen apps and auto-hide,
    /// moves with it, and never needs to fight other windows to stay on top.
    ///
    /// Because the window is a child of Explorer's taskbar, this thread shares input with it. A stall here
    /// would freeze the user's taskbar, so this thread never waits on anything slow: audio and taskbar scans run
    /// on their own threads and hand results back with PostMessage. (When debugging, don't stop on a breakpoint
    /// on this thread for long: the taskbar freezes until you continue.)
    /// </summary>
    internal sealed unsafe class IndicatorWindow : IDisposable
    {
        public const string ClassName = "SoundBarTaskbarIndicator";

        private const uint WM_APP_AUDIO = Native.WM_APP + 1;
        private const uint WM_APP_SCAN = Native.WM_APP + 2;
        private static readonly UIntPtr TickTimer = new(1);
        private const uint TickMilliseconds = 1000;

        private const int Cmd_MuteSound = 1;
        private const int Cmd_MuteMic = 2;
        private const int Cmd_OpenSoundBar = 3;
        private const int Cmd_Hide = 4;
        private const int Cmd_MoveFirst = 100;

        // Segoe Fluent Icons / Segoe MDL2 Assets
        private const string GlyphMute = "\uE74F";
        private const string GlyphHeadphones = "\uE7F6";
        private const string GlyphVolume0 = "\uE992";
        private const string GlyphVolume1 = "\uE993";
        private const string GlyphVolume2 = "\uE994";
        private const string GlyphVolume3 = "\uE995";
        private const string GlyphVolume = "\uE767"; // For older Windows 10 icon fonts without the levels above
        private const string GlyphMic = "\uE720";
        private const string GlyphMicOffFluent = "\uF12E"; // The one SoundBar uses
        private const string GlyphMicOffMdl2 = "\uEC54";

        // The window procedures must stay alive as long as the windows do, so they're kept here.
        private static Native.WndProc? s_hostProc;
        private static Native.WndProc? s_childProc;
        private static IndicatorWindow? s_current;

        private readonly IntPtr _instance = Native.GetModuleHandleW(null);
        private readonly HelperSettings _settings;
        private readonly uint _taskbarCreatedMessage;
        private readonly Canvas _canvas = new();

        private IntPtr _host, _child, _tooltip, _taskbar;
        private AudioMonitor? _audio;
        private TaskbarScanner? _scanner;
        private int _audioPosted, _scanPosted;
        private volatile TaskbarScan? _latestScan;
        private TaskbarScan? _scan;

        // Drawing
        private uint _dpi;
        private IntPtr _iconFont, _textFont;
        private string _micOffGlyph = GlyphMicOffFluent;
        private bool _hasVolumeLevels = true;
        private bool _lightTheme;
        private bool _hover, _pressed;
        private int _wheelRemainder;
        private Visual _visual;
        private string _tooltipText = "";

        // Where we are, so nothing is redone unless it changed
        private (int X, int Width, int Height, bool Visible) _placement;
        private int _spare;
        private Native.RECT _lastTaskbarRect, _lastTrayRect;
        private long _nextScanTick, _nextAudioRetryTick, _nextMonitorCheckTick;
        private int _lastMinute = -1;
        private long _lastAudioChangeTick, _allocatedAtLastTidy;

        public IndicatorWindow(HelperSettings settings)
        {
            _settings = settings;
            _taskbarCreatedMessage = Native.RegisterWindowMessageW("TaskbarCreated");
        }

        /// <summary>Creates everything and runs until told to close. Returns the exit code.</summary>
        public int Run()
        {
            s_current = this;
            s_hostProc = (h, m, w, l) => s_current?.HostProc(h, m, w, l) ?? Native.DefWindowProcW(h, m, w, l);
            s_childProc = (h, m, w, l) => s_current?.ChildProc(h, m, w, l) ?? Native.DefWindowProcW(h, m, w, l);

            RegisterClass(TaskbarHelperInfo.HostWindowClass, s_hostProc);
            RegisterClass(ClassName, s_childProc);

            // Hidden and never shown. It receives Windows' broadcasts (Explorer restarting, theme and display
            // changes), owns the menu and tooltip, and is how SoundBar asks us to close.
            _host = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW, TaskbarHelperInfo.HostWindowClass, "SoundBar Taskbar",
                Native.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_host == IntPtr.Zero) return 1;

            var icc = new Native.INITCOMMONCONTROLSEX { dwSize = (uint)sizeof(Native.INITCOMMONCONTROLSEX), dwICC = Native.ICC_WIN95_CLASSES };
            Native.InitCommonControlsEx(ref icc);
            _tooltip = Native.CreateWindowExW(Native.WS_EX_TOPMOST, "tooltips_class32", null,
                Native.WS_POPUP | Native.TTS_NOPREFIX | Native.TTS_ALWAYSTIP, 0, 0, 0, 0, _host, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_tooltip != IntPtr.Zero)
                Native.SendMessageW(_tooltip, Native.TTM_SETMAXTIPWIDTH, IntPtr.Zero, new IntPtr(400));

            ApplyTheme();

            _audio = new AudioMonitor(() => PostOnce(ref _audioPosted, WM_APP_AUDIO));
            _scanner = new TaskbarScanner(scan => { _latestScan = scan; PostOnce(ref _scanPosted, WM_APP_SCAN); });

            Attach();
            Native.SetTimer(_host, TickTimer, TickMilliseconds, IntPtr.Zero);

            while (Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }
            return 0;
        }

        private void RegisterClass(string name, Native.WndProc proc)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = _instance,
                hCursor = Native.LoadCursorW(IntPtr.Zero, new IntPtr(Native.IDC_ARROW)),
                lpszClassName = name,
            };
            Native.RegisterClassExW(ref wc);
        }

        private void PostOnce(ref int flag, uint message)
        {
            // Many changes in a row (dragging a volume slider) become one message
            if (Interlocked.Exchange(ref flag, 1) == 0)
                Native.PostMessageW(_host, message, IntPtr.Zero, IntPtr.Zero);
        }

        // ------------------------------------------------------------------
        // Finding a taskbar and putting the indicator on it
        // ------------------------------------------------------------------

        private readonly record struct Taskbar(IntPtr Handle, string Device, bool IsPrimary);

        /// <summary>Every visible horizontal taskbar, main one first.</summary>
        private static List<Taskbar> FindTaskbars()
        {
            var found = new List<Taskbar>();
            Native.EnumWindows((hWnd, _) =>
            {
                string cls = Native.ClassNameOf(hWnd);
                bool primary = cls == "Shell_TrayWnd";
                if ((primary || cls == "Shell_SecondaryTrayWnd") && Native.IsWindowVisible(hWnd))
                    found.Add(new Taskbar(hWnd, DeviceOf(hWnd), primary));
                return true;
            }, IntPtr.Zero);
            found.Sort((a, b) => b.IsPrimary.CompareTo(a.IsPrimary));
            return found;
        }

        private static string DeviceOf(IntPtr hWnd)
        {
            var info = new Native.MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<Native.MONITORINFOEXW>(), szDevice = "" };
            IntPtr monitor = Native.MonitorFromWindow(hWnd, Native.MONITOR_DEFAULTTONEAREST);
            return monitor != IntPtr.Zero && Native.GetMonitorInfoW(monitor, ref info) ? info.szDevice ?? "" : "";
        }

        /// <summary>The taskbar on the chosen monitor, or the main one if that monitor isn't there.</summary>
        private IntPtr ChooseTaskbar(List<Taskbar> taskbars)
        {
            if (!string.IsNullOrEmpty(_settings.Monitor))
            {
                foreach (var t in taskbars)
                    if (string.Equals(t.Device, _settings.Monitor, StringComparison.OrdinalIgnoreCase)) return t.Handle;
            }
            foreach (var t in taskbars)
                if (t.IsPrimary) return t.Handle;
            return taskbars.Count > 0 ? taskbars[0].Handle : IntPtr.Zero;
        }

        private void Attach()
        {
            Detach();
            IntPtr taskbar = ChooseTaskbar(FindTaskbars());
            if (taskbar == IntPtr.Zero) return; // Explorer isn't up yet. The timer tries again.

            _child = Native.CreateWindowExW(Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE, ClassName, "SoundBar",
                Native.WS_CHILD | Native.WS_CLIPSIBLINGS, 0, 0, 1, 1, taskbar, IntPtr.Zero, _instance, IntPtr.Zero);
            if (_child == IntPtr.Zero)
            {
                Debug.WriteLine($"Couldn't create the indicator window ({Marshal.GetLastWin32Error()}).");
                return;
            }

            _taskbar = taskbar;
            _scan = null;
            _placement = default;
            _lastTaskbarRect = default;
            _lastTrayRect = default;
            AddTooltip();
            UpdateDpi(force: true);
            RequestScan();
        }

        private void Detach()
        {
            if (_child != IntPtr.Zero)
            {
                RemoveTooltip();
                IntPtr child = _child;
                _child = IntPtr.Zero;
                if (Native.IsWindow(child)) Native.DestroyWindow(child);
            }
            _taskbar = IntPtr.Zero;
            _scan = null;
            _hover = _pressed = false;
        }

        private void RequestScan()
        {
            if (_taskbar == IntPtr.Zero) return;
            _scanner?.Request(_taskbar);
            _nextScanTick = Environment.TickCount64 + ScanInterval();
        }

        /// <summary>
        /// How often to look at the taskbar again for new app buttons. Often when space is tight (so a new app
        /// doesn't sit under us for long), rarely when there's lots of room.
        /// </summary>
        private int ScanInterval()
        {
            if (_scan == null || !_placement.Visible) return 2000;
            return _spare < Px(150) ? 2000 : 5000; // About three app buttons' worth of room
        }

        /// <summary>Where the tray starts on the main taskbar, from Windows' own tray window (relative to the taskbar).</summary>
        private Native.RECT TrayRect()
        {
            IntPtr tray = Native.FindWindowExW(_taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out var r) || !Native.GetWindowRect(_taskbar, out var bar))
                return default;
            return new Native.RECT(r.Left - bar.Left, r.Top - bar.Top, r.Right - bar.Left, r.Bottom - bar.Top);
        }

        /// <summary>Works out where the indicator goes and moves it there (or hides it if there's no room).</summary>
        private void Place()
        {
            if (_child == IntPtr.Zero || _scan == null) return;
            int scale100 = (int)_dpi;
            int margin = 4 * scale100 / 96;

            int? x = TaskbarLayout.FindSlot(_scan.Width, _scan.Height, _scan.Elements, TrayRect().Left, _visual.Width, margin, out _spare);
            var placement = x.HasValue ? (x.Value, _visual.Width, _scan.Height, true) : (0, 0, 0, false);

            if (placement != _placement)
            {
                _placement = placement;
                if (placement.Item4)
                {
                    // The scan measured from the taskbar's outer edge. Convert to its client area.
                    var origin = new Native.RECT();
                    Native.MapWindowPoints(_taskbar, IntPtr.Zero, ref origin, 2);
                    Native.GetWindowRect(_taskbar, out var bar);
                    int offsetX = origin.Left - bar.Left, offsetY = origin.Top - bar.Top;

                    Render();
                    Native.SetWindowPos(_child, Native.HWND_TOP, placement.Item1 - offsetX, -offsetY, placement.Item2, placement.Item3,
                        Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
                }
                else
                {
                    Native.SetWindowPos(_child, IntPtr.Zero, 0, 0, 0, 0,
                        Native.SWP_NOACTIVATE | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_HIDEWINDOW);
                    _hover = _pressed = false;
                }
            }
        }

        // ------------------------------------------------------------------
        // The once-a-second check
        // ------------------------------------------------------------------

        private void Tick()
        {
            long now = Environment.TickCount64;

            // Explorer restarted, or the taskbar on our monitor went away
            if (_child == IntPtr.Zero || !Native.IsWindow(_child) || !Native.IsWindow(_taskbar))
            {
                Attach();
                return;
            }

            // The chosen monitor came back (plugged in again, or the taskbar was turned back on there)
            if (now >= _nextMonitorCheckTick)
            {
                _nextMonitorCheckTick = now + 5000;
                IntPtr wanted = ChooseTaskbar(FindTaskbars());
                if (wanted != IntPtr.Zero && wanted != _taskbar) { Attach(); return; }
            }

            UpdateDpi(force: false);

            // Size changes, or the tray growing or shrinking, mean it's time to look again
            Native.GetWindowRect(_taskbar, out var bar);
            var size = new Native.RECT(0, 0, bar.Width, bar.Height);
            var tray = TrayRect();
            int minute = DateTime.Now.Minute; // The clock can change width when the time changes
            if (!size.Equals(_lastTaskbarRect) || !tray.Equals(_lastTrayRect) || minute != _lastMinute || now >= _nextScanTick)
            {
                _lastTaskbarRect = size;
                _lastTrayRect = tray;
                _lastMinute = minute;
                RequestScan();
            }

            // Stay on top of the taskbar's own content
            if (_placement.Visible && Native.GetWindow(_child, Native.GW_HWNDPREV) != IntPtr.Zero)
                Native.SetWindowPos(_child, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            // The audio service wasn't ready (straight after signing in, for example). Try again.
            if (_audio != null && _audio.NeedsRetry && now >= _nextAudioRetryTick)
            {
                _nextAudioRetryTick = now + 5000;
                _audio.RequestRefresh();
            }

            TidyMemory(now);
        }

        /// <summary>
        /// Each volume change leaves a few hundred bytes of short-lived objects behind. .NET would normally let
        /// several megabytes build up before clearing them, which is fine for most apps but not for one that
        /// sits in the background all day. So once things have gone quiet after a burst of changes (dragging a
        /// slider, say), clear them. It takes well under a millisecond with so little memory in use.
        /// </summary>
        private void TidyMemory(long now)
        {
            long allocated = GC.GetTotalAllocatedBytes();
            if (allocated - _allocatedAtLastTidy < 1_000_000 || now - _lastAudioChangeTick < 2000) return;
            GC.Collect(1, GCCollectionMode.Forced, blocking: true, compacting: true);
            _allocatedAtLastTidy = GC.GetTotalAllocatedBytes();
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        /// <summary>What to draw, worked out once per change rather than on every repaint.</summary>
        private struct Visual
        {
            public string OutputGlyph;
            public uint OutputGlyphColor;
            public string Text;
            public uint TextColor;
            public string? MicGlyph;
            public uint MicColor;
            public int OutputGlyphX, TextX, MicX, Width;
            public int OutputGlyphY, TextY, MicY;
        }

        private float Scale => _dpi / 96f;
        private int Px(float value) => (int)Math.Round(value * Scale);

        private void UpdateDpi(bool force)
        {
            uint dpi = _taskbar != IntPtr.Zero ? Native.GetDpiForWindow(_taskbar) : 96;
            if (dpi == 0) dpi = 96;
            if (!force && dpi == _dpi) return;
            _dpi = dpi;

            FreeFonts();
            _iconFont = CreateFont(16, Native.FW_NORMAL, "Segoe Fluent Icons", "Segoe MDL2 Assets");
            _textFont = CreateFont(14, Native.FW_NORMAL, "Segoe UI Variable Text", "Segoe UI");
            // Windows 10 has Segoe MDL2 Assets instead of Segoe Fluent Icons, and early versions of it lack a few icons
            _micOffGlyph = HasGlyph(_iconFont, GlyphMicOffFluent) ? GlyphMicOffFluent
                : HasGlyph(_iconFont, GlyphMicOffMdl2) ? GlyphMicOffMdl2 : GlyphMic; // Still shown in red when muted
            _hasVolumeLevels = HasGlyph(_iconFont, GlyphVolume0) && HasGlyph(_iconFont, GlyphVolume3);

            Compose();
            _placement = default; // Re-place at the new size
            Place();
        }

        private IntPtr CreateFont(float size, int weight, string face, string fallback)
        {
            IntPtr font = MakeFont(size, weight, face);
            if (FaceOf(font).StartsWith(face, StringComparison.OrdinalIgnoreCase)) return font;
            Native.DeleteObject(font);
            return MakeFont(size, weight, fallback);
        }

        private IntPtr MakeFont(float size, int weight, string face) =>
            Native.CreateFontW(-Px(size), 0, 0, 0, weight, 0, 0, 0, Native.DEFAULT_CHARSET, Native.OUT_TT_PRECIS,
                Native.CLIP_DEFAULT_PRECIS, Native.ANTIALIASED_QUALITY, Native.DEFAULT_PITCH, face);

        private string FaceOf(IntPtr font)
        {
            IntPtr old = Native.SelectObject(_canvas.MeasureDc, font);
            char* name = stackalloc char[64];
            int length = Native.GetTextFaceW(_canvas.MeasureDc, 64, name);
            Native.SelectObject(_canvas.MeasureDc, old);
            return length > 1 ? new string(name, 0, length - 1) : "";
        }

        private bool HasGlyph(IntPtr font, string glyph)
        {
            IntPtr old = Native.SelectObject(_canvas.MeasureDc, font);
            ushort index = 0xFFFF;
            Native.GetGlyphIndicesW(_canvas.MeasureDc, glyph, 1, &index, Native.GGI_MARK_NONEXISTING_GLYPHS);
            Native.SelectObject(_canvas.MeasureDc, old);
            return index != 0xFFFF;
        }

        private void FreeFonts()
        {
            if (_iconFont != IntPtr.Zero) { Native.DeleteObject(_iconFont); _iconFont = IntPtr.Zero; }
            if (_textFont != IntPtr.Zero) { Native.DeleteObject(_textFont); _textFont = IntPtr.Zero; }
        }

        // Windows' own text colours on the taskbar: a soft near-black in light mode, white in dark mode
        private uint Foreground => _lightTheme ? 0xE4000000 : 0xFFFFFFFF;
        private uint DimForeground => _lightTheme ? 0x8A000000 : 0x99FFFFFF;
        // The same reds SoundBar uses for "muted"
        private uint MuteColor => _lightTheme ? 0xFFC42B1C : 0xFFFF6B6B;

        /// <summary>Works out the glyphs, colours and positions for the current state.</summary>
        private void Compose()
        {
            if (_iconFont == IntPtr.Zero) return;
            var state = _audio?.State ?? AudioState.Empty;
            var v = new Visual();

            if (!state.HasOutput)
            {
                v.OutputGlyph = GlyphMute;
                v.OutputGlyphColor = DimForeground;
                v.Text = "";
            }
            else if (state.OutputMuted)
            {
                v.OutputGlyph = GlyphMute;
                v.OutputGlyphColor = MuteColor;
                v.Text = $"{state.Volume}%";
                v.TextColor = DimForeground;
            }
            else
            {
                v.OutputGlyph = state.IsHeadphones ? GlyphHeadphones
                    : !_hasVolumeLevels ? GlyphVolume
                    : state.Volume == 0 ? GlyphVolume0 : state.Volume < 34 ? GlyphVolume1 : state.Volume < 67 ? GlyphVolume2 : GlyphVolume3;
                v.OutputGlyphColor = Foreground;
                v.Text = $"{state.Volume}%";
                v.TextColor = Foreground;
            }

            if (state.HasMic)
            {
                v.MicGlyph = state.MicMuted ? _micOffGlyph : GlyphMic;
                v.MicColor = state.MicMuted ? MuteColor : Foreground;
            }

            int height = _scan?.Height ?? Px(48);
            int padding = Px(8), iconGap = Px(5), itemGap = Px(12);
            var glyphSize = _canvas.Measure(v.OutputGlyph, _iconFont);
            int x = padding;
            v.OutputGlyphX = x;
            v.OutputGlyphY = (height - glyphSize.Height) / 2;
            x += glyphSize.Width;

            if (v.Text.Length > 0)
            {
                var textSize = _canvas.Measure(v.Text, _textFont);
                x += iconGap;
                v.TextX = x;
                v.TextY = (height - textSize.Height) / 2;
                x += textSize.Width;
            }

            if (v.MicGlyph != null)
            {
                var micSize = _canvas.Measure(v.MicGlyph, _iconFont);
                x += itemGap;
                v.MicX = x;
                v.MicY = (height - micSize.Height) / 2;
                x += micSize.Width;
            }

            v.Width = x + padding;
            _visual = v;
            UpdateTooltip(state);
        }

        private void Render()
        {
            if (_child == IntPtr.Zero || _iconFont == IntPtr.Zero || _placement.Height <= 0) return;
            int width = _placement.Width, height = _placement.Height;
            _canvas.Begin(width, height);

            if (_hover)
            {
                // Like the tray's own buttons: a soft rounded highlight
                uint fill = _lightTheme ? (_pressed ? 0x0A000000u : 0x12000000u) : (_pressed ? 0x0CFFFFFFu : 0x16FFFFFFu);
                int inset = Px(4);
                _canvas.FillRoundedRect(0, inset, width, height - inset, Px(4), fill);
            }

            var v = _visual;
            _canvas.DrawText(v.OutputGlyph, _iconFont, v.OutputGlyphX, v.OutputGlyphY, v.OutputGlyphColor);
            if (v.Text.Length > 0) _canvas.DrawText(v.Text, _textFont, v.TextX, v.TextY, v.TextColor);
            if (v.MicGlyph != null) _canvas.DrawText(v.MicGlyph, _iconFont, v.MicX, v.MicY, v.MicColor);
            _canvas.Present(_child);
        }

        private void ApplyTheme()
        {
            _lightTheme = ReadLightTaskbar();

            // Classic menus and tooltips are light unless asked otherwise. Match the taskbar.
            if (Environment.OSVersion.Version.Build >= 18362)
            {
                try
                {
                    Native.SetPreferredAppMode(_lightTheme ? 3 /* ForceLight */ : 2 /* ForceDark */);
                    Native.FlushMenuThemes();
                }
                catch (EntryPointNotFoundException) { }
            }
            if (_tooltip != IntPtr.Zero)
                Native.SetWindowTheme(_tooltip, _lightTheme ? "Explorer" : "DarkMode_Explorer", null);
        }

        /// <summary>The taskbar follows Windows' mode (not the apps' mode), so read that one.</summary>
        private static bool ReadLightTaskbar()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Tooltip
        // ------------------------------------------------------------------

        private Native.TOOLINFOW ToolInfo(IntPtr text) => new()
        {
            cbSize = (uint)sizeof(Native.TOOLINFOW),
            uFlags = Native.TTF_IDISHWND | Native.TTF_SUBCLASS,
            hwnd = _host,
            uId = (UIntPtr)(ulong)_child,
            lpszText = text,
        };

        private void AddTooltip()
        {
            if (_tooltip == IntPtr.Zero || _child == IntPtr.Zero) return;
            IntPtr text = Marshal.StringToHGlobalUni(_tooltipText);
            try
            {
                var info = ToolInfo(text);
                Native.SendMessageW(_tooltip, Native.TTM_ADDTOOLW, IntPtr.Zero, ref info);
            }
            finally
            {
                Marshal.FreeHGlobal(text);
            }
        }

        private void RemoveTooltip()
        {
            if (_tooltip == IntPtr.Zero || _child == IntPtr.Zero) return;
            var info = ToolInfo(IntPtr.Zero);
            Native.SendMessageW(_tooltip, (uint)(Native.WM_USER + 51) /* TTM_DELTOOLW */, IntPtr.Zero, ref info);
        }

        private void UpdateTooltip(AudioState state)
        {
            string output = !state.HasOutput ? "No speakers or headphones"
                : state.OutputMuted ? $"{state.OutputName}: muted ({state.Volume}%)"
                : $"{state.OutputName}: {state.Volume}%";
            string text = state.HasMic
                ? $"{output}\n{state.MicName}: {(state.MicMuted ? "muted" : "on")}\nScroll to change the volume. Right-click for options."
                : $"{output}\nScroll to change the volume. Right-click for options.";
            if (text == _tooltipText) return;
            _tooltipText = text;

            if (_tooltip == IntPtr.Zero || _child == IntPtr.Zero) return;
            IntPtr buffer = Marshal.StringToHGlobalUni(text);
            try
            {
                var info = ToolInfo(buffer);
                Native.SendMessageW(_tooltip, Native.TTM_UPDATETIPTEXTW, IntPtr.Zero, ref info);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // ------------------------------------------------------------------
        // Clicks and the menu
        // ------------------------------------------------------------------

        private void ShowMenu()
        {
            var state = _audio?.State ?? AudioState.Empty;
            var taskbars = FindTaskbars();
            var others = taskbars.Where(t => t.Handle != _taskbar).ToList();

            IntPtr menu = Native.CreatePopupMenu();
            if (menu == IntPtr.Zero) return;
            try
            {
                Native.AppendMenuW(menu, Native.MF_STRING | (state.OutputMuted ? Native.MF_CHECKED : 0) | (state.HasOutput ? 0 : Native.MF_GRAYED),
                    (UIntPtr)Cmd_MuteSound, "Mute Sound");
                Native.AppendMenuW(menu, Native.MF_STRING | (state.MicMuted ? Native.MF_CHECKED : 0) | (state.HasMic ? 0 : Native.MF_GRAYED),
                    (UIntPtr)Cmd_MuteMic, "Mute Mic");
                Native.AppendMenuW(menu, Native.MF_SEPARATOR, UIntPtr.Zero, null);
                Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)Cmd_OpenSoundBar, "Open SoundBar");
                Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)Cmd_Hide, "Hide from Taskbar");

                // Only when there's another taskbar to move to (Windows' "Show my taskbar on all displays")
                if (others.Count == 1)
                {
                    Native.AppendMenuW(menu, Native.MF_STRING, (UIntPtr)Cmd_MoveFirst, "Move to Other Monitor");
                }
                else if (others.Count > 1)
                {
                    IntPtr sub = Native.CreatePopupMenu(); // Owned by the menu once appended, so freed with it
                    for (int i = 0; i < others.Count; i++)
                        Native.AppendMenuW(sub, Native.MF_STRING, (UIntPtr)(Cmd_MoveFirst + i), MonitorLabel(others[i]));
                    Native.AppendMenuW(menu, Native.MF_POPUP, (UIntPtr)(ulong)sub, "Move to Other Monitor");
                }

                Native.GetCursorPos(out var point);
                // Windows needs our window in front for the menu to close when you click elsewhere
                Native.SetForegroundWindow(_host);
                int command = Native.TrackPopupMenuEx(menu, Native.TPM_RETURNCMD | Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN | Native.TPM_NONOTIFY,
                    point.X, point.Y, _host, IntPtr.Zero);
                Native.PostMessageW(_host, Native.WM_NULL, IntPtr.Zero, IntPtr.Zero);

                switch (command)
                {
                    case Cmd_MuteSound: _audio?.ToggleOutputMute(); break;
                    case Cmd_MuteMic: _audio?.ToggleMicMute(); break;
                    case Cmd_OpenSoundBar: OpenSoundBar(); break;
                    case Cmd_Hide: HideFromTaskbar(); break;
                    case >= Cmd_MoveFirst when command - Cmd_MoveFirst < others.Count:
                        MoveTo(others[command - Cmd_MoveFirst]);
                        break;
                }
            }
            finally
            {
                Native.DestroyMenu(menu);
            }
        }

        private static string MonitorLabel(Taskbar taskbar)
        {
            // "\\.\DISPLAY2" becomes "Display 2"
            string digits = new(taskbar.Device.Where(char.IsDigit).ToArray());
            string name = digits.Length > 0 ? $"Display {digits}" : "Other display";
            return taskbar.IsPrimary ? $"{name} (main)" : name;
        }

        private void MoveTo(Taskbar taskbar)
        {
            _settings.Monitor = taskbar.IsPrimary ? null : taskbar.Device;
            _settings.Save();
            Attach();
        }

        private static void OpenSoundBar()
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "SoundBar.exe");
            if (!File.Exists(exe)) return;
            try
            {
                // Already open: bring it forward ourselves. Windows only lets the program you just clicked
                // change which window is in front, so asking SoundBar to do it wouldn't work.
                IntPtr window = FindSoundBarWindow(exe);
                if (window != IntPtr.Zero)
                {
                    if (Native.IsIconic(window)) Native.ShowWindow(window, Native.SW_RESTORE);
                    Native.SetForegroundWindow(window);
                    return;
                }

                // Not running: start it, and let it come to the front
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                using var _ = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Couldn't open SoundBar: {ex.Message}");
            }
        }

        /// <summary>SoundBar's main window, if this copy of SoundBar is running.</summary>
        private static IntPtr FindSoundBarWindow(string exe)
        {
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows((hWnd, _) =>
            {
                if (Native.IsWindowVisible(hWnd) && Native.TitleOf(hWnd) == "SoundBar" &&
                    Native.ClassNameOf(hWnd) == "WinUIDesktopWin32WindowClass" &&
                    TaskbarHelperInfo.SamePath(Native.ProgramOf(hWnd), exe))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>
        /// Turns the taskbar info off: removes our startup shortcut and closes. SoundBar's Settings can turn it
        /// back on. A shortcut belonging to another copy of SoundBar is left alone.
        /// </summary>
        private void HideFromTaskbar()
        {
            try
            {
                string shortcut = TaskbarHelperInfo.GetStartupShortcutPath();
                if (File.Exists(shortcut))
                {
                    string? target = ShortcutFile.ReadTarget(shortcut);
                    if (string.IsNullOrEmpty(target) || !File.Exists(target) || TaskbarHelperInfo.SamePath(target, Environment.ProcessPath))
                        File.Delete(shortcut);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Couldn't remove the startup shortcut: {ex.Message}");
            }
            Native.DestroyWindow(_host);
        }

        // ------------------------------------------------------------------
        // Window procedures
        // ------------------------------------------------------------------

        private IntPtr HostProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (msg)
                {
                    case Native.WM_TIMER when wParam == (IntPtr)(ulong)TickTimer:
                        Tick();
                        return IntPtr.Zero;

                    case WM_APP_AUDIO:
                        Interlocked.Exchange(ref _audioPosted, 0);
                        _lastAudioChangeTick = Environment.TickCount64;
                        Compose();
                        // A new width (say 9% to 10%) may move us, or let us fit again if we were hidden for lack of room
                        if (_placement.Width != _visual.Width) Place();
                        else Render();
                        return IntPtr.Zero;

                    case WM_APP_SCAN:
                        Interlocked.Exchange(ref _scanPosted, 0);
                        var scan = _latestScan;
                        if (scan != null && scan.Taskbar == _taskbar)
                        {
                            bool heightChanged = _scan?.Height != scan.Height;
                            _scan = scan;
                            if (heightChanged) Compose();
                            Place();
                        }
                        return IntPtr.Zero;

                    case Native.WM_SETTINGCHANGE:
                        // Light/dark mode, taskbar alignment and so on
                        bool wasLight = _lightTheme;
                        ApplyTheme();
                        if (wasLight != _lightTheme) { Compose(); Render(); }
                        RequestScan();
                        return IntPtr.Zero;

                    case Native.WM_DISPLAYCHANGE:
                        _nextMonitorCheckTick = 0;
                        RequestScan();
                        return IntPtr.Zero;

                    case Native.WM_QUERYENDSESSION:
                        return new IntPtr(1);

                    case Native.WM_CLOSE:
                        Native.DestroyWindow(hWnd);
                        return IntPtr.Zero;

                    case Native.WM_DESTROY:
                        if (hWnd == _host)
                        {
                            Native.KillTimer(_host, TickTimer);
                            Native.PostQuitMessage(0);
                        }
                        return IntPtr.Zero;
                }

                if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
                {
                    // Explorer restarted: the old taskbar (and our window in it) is gone
                    Attach();
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Host window: {ex}");
            }
            return Native.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        private IntPtr ChildProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (msg)
                {
                    case Native.WM_MOUSEACTIVATE:
                        return new IntPtr(Native.MA_NOACTIVATE);

                    case Native.WM_MOUSEMOVE:
                        if (!_hover)
                        {
                            _hover = true;
                            var track = new Native.TRACKMOUSEEVENT
                            {
                                cbSize = (uint)sizeof(Native.TRACKMOUSEEVENT),
                                dwFlags = Native.TME_LEAVE,
                                hwndTrack = hWnd,
                            };
                            Native.TrackMouseEvent(ref track);
                            Render();
                        }
                        return IntPtr.Zero;

                    case Native.WM_MOUSELEAVE:
                        _hover = _pressed = false;
                        Render();
                        return IntPtr.Zero;

                    case Native.WM_LBUTTONDOWN:
                        _pressed = true;
                        Render();
                        return IntPtr.Zero;

                    case Native.WM_LBUTTONUP:
                        if (_pressed)
                        {
                            _pressed = false;
                            Render();
                            OpenSoundBar();
                        }
                        return IntPtr.Zero;

                    case Native.WM_RBUTTONUP:
                        ShowMenu();
                        return IntPtr.Zero;

                    case Native.WM_MOUSEWHEEL:
                        // One notch = 2%, like scrolling on Windows' own volume slider
                        _wheelRemainder += Native.HiWord(wParam);
                        int notches = _wheelRemainder / 120;
                        if (notches != 0)
                        {
                            _wheelRemainder -= notches * 120;
                            _audio?.ChangeVolume(notches * 2);
                        }
                        return IntPtr.Zero;

                    case Native.WM_DESTROY:
                        // Our parent (the taskbar) is going away, usually because Explorer restarted.
                        // The timer and TaskbarCreated put us back.
                        if (hWnd == _child)
                        {
                            RemoveTooltip();
                            _child = IntPtr.Zero;
                        }
                        return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Indicator window: {ex}");
            }
            return Native.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            Detach();
            _scanner?.Dispose();
            _audio?.Dispose();
            if (_tooltip != IntPtr.Zero && Native.IsWindow(_tooltip)) Native.DestroyWindow(_tooltip);
            if (_host != IntPtr.Zero && Native.IsWindow(_host)) Native.DestroyWindow(_host);
            FreeFonts();
            _canvas.Dispose();
            s_current = null;
        }
    }
}

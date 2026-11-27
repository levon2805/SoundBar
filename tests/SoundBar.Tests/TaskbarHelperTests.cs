extern alias taskbar;

using System;
using System.Collections.Generic;
using System.IO;
using SoundBar.Models;
using SoundBar.Services;
using SoundBar.Shared;
using SoundBar.ViewModels;
using Moq;
using Xunit;
using Element = taskbar::SoundBarTaskbar.TaskbarElement;
using Layout = taskbar::SoundBarTaskbar.TaskbarLayout;
using Rect = taskbar::SoundBarTaskbar.Native.RECT;
using HelperSettings = taskbar::SoundBarTaskbar.HelperSettings;

namespace SoundBar.Tests
{
    /// <summary>Where the indicator sits on the taskbar. Windows 11 layouts are taken from real taskbars. Windows 10 ones are modelled on how its taskbar is built (its window classes), not captured.</summary>
    public class TaskbarLayoutTests
    {
        private const int Width = 100;
        private const int Margin = 4;

        private static Element Button(int left, int width, string cls = "Taskbar.TaskListButtonAutomationPeer", string id = "") =>
            new(new Rect(left, 0, left + width, 48), cls, id);

        private static Element Tray(int left, int width, string cls = "SystemTray.NormalButton") =>
            new(new Rect(left, 0, left + width, 48), cls, "SystemTrayIcon");

        /// <summary>A 1920 wide main taskbar: Start, Search, eleven apps, then the tray from x=1738.</summary>
        private static List<Element> MainTaskbar(int apps = 11)
        {
            var list = new List<Element>
            {
                new(new Rect(0, 0, 1920, 48), "Taskbar.TaskbarFrameAutomationPeer", "TaskbarFrame"),
                new(new Rect(0, 0, 1920, 48), "Windows.UI.Input.InputSite.WindowClass", ""),
                Button(0, 55, "ToggleButton", "StartButton"),
                Button(55, 106, "ToggleButton", "SearchButton"),
                new(new Rect(98, 16, 135, 33), "TextBlock", "SearchBoxTextBlock"),
            };
            for (int i = 0; i < apps; i++) list.Add(Button(161 + i * 44, 44));
            list.Add(Tray(1738, 32));
            list.Add(Tray(1774, 24, "SystemTray.AccentButton"));
            list.Add(Tray(1834, 70, "SystemTray.OmniButton"));
            list.Add(Tray(1908, 12, "SystemTray.ShowDesktopButton"));
            list.Add(new(new Rect(1738, 0, 1920, 48), "TrayNotifyWnd", ""));
            list.Add(new(new Rect(55, 0, 425, 48), "ReBarWindow32", "40965"));
            return list;
        }

        [Fact]
        public void SitsJustLeftOfTheTray()
        {
            int? x = Layout.FindSlot(1920, 48, MainTaskbar(), 1738, Width, Margin, out int spare);

            Assert.Equal(1738 - Margin - Width, x);
            Assert.True(spare > 900); // Lots of room between the apps and the tray
        }

        [Fact]
        public void OnASecondTaskbar_SitsLeftOfTheClock()
        {
            // Second monitors have no tray, just the clock at the far right
            var elements = new List<Element>
            {
                Button(0, 55, "ToggleButton", "StartButton"),
                Button(161, 44),
                Tray(1842, 70, "SystemTray.OmniButton"),
            };

            Assert.Equal(1842 - Margin - Width, Layout.FindSlot(1920, 48, elements, 0, Width, Margin, out _));
        }

        [Fact]
        public void HidesRatherThanCoveringAppButtons()
        {
            // 35 app buttons reach x=1701, leaving 37 pixels before the tray: not enough for 100
            Assert.Null(Layout.FindSlot(1920, 48, MainTaskbar(apps: 35), 1738, Width, Margin, out int spare));
            Assert.Equal(0, spare);
        }

        [Fact]
        public void FitsExactlyWhenThereIsJustEnoughRoom()
        {
            // The last app ends at 1630, so 1630 + 4 + 100 + 4 = 1738, the tray's edge
            var elements = new List<Element> { Button(0, 1630), Tray(1738, 182) };

            Assert.Equal(1634, Layout.FindSlot(1920, 48, elements, 1738, Width, Margin, out int spare));
            Assert.Equal(0, spare);
            Assert.Null(Layout.FindSlot(1920, 48, new List<Element> { Button(0, 1631), Tray(1738, 182) }, 1738, Width, Margin, out _));
        }

        [Fact]
        public void StepsAroundAButtonNextToTheTray()
        {
            // Something else (like the Widgets button) sits right next to the tray
            var elements = MainTaskbar();
            elements.Add(new(new Rect(1650, 0, 1734, 48), "ToggleButton", "WidgetsButton"));

            Assert.Equal(1650 - Margin - Width, Layout.FindSlot(1920, 48, elements, 1738, Width, Margin, out _));
        }

        [Fact]
        public void UsesTheTrayWindow_WhenTheScanFoundNoTrayItems()
        {
            var elements = new List<Element> { Button(0, 55, "ToggleButton", "StartButton") };

            Assert.Equal(1700 - Margin - Width, Layout.FindSlot(1920, 48, elements, 1700, Width, Margin, out _));
            // And the taskbar's right edge when there's nothing better
            Assert.Equal(1920 - Margin - Width, Layout.FindSlot(1920, 48, elements, 0, Width, Margin, out _));
        }

        [Fact]
        public void IgnoresContainersAndItsOwnWindow()
        {
            var elements = MainTaskbar();
            elements.Add(new(new Rect(1634, 0, 1734, 48), "SoundBarTaskbarIndicator", ""));
            elements.Add(new(new Rect(-1000, 0, 3000, 48), "WorkerW", ""));

            Assert.Equal(1738 - Margin - Width, Layout.FindSlot(1920, 48, elements, 1738, Width, Margin, out _));
        }

        [Fact]
        public void VerticalTaskbarsAreNotSupported()
        {
            Assert.Null(Layout.FindSlot(48, 1032, new List<Element>(), 0, Width, Margin, out _));
        }

        /// <summary>
        /// A 1920 wide Windows 10 main taskbar, 40 high: Start, the search box, Task View, then the app list
        /// (a window that spans to the tray, with the buttons inside it), then the tray from x=1600.
        /// </summary>
        private static List<Element> Windows10Taskbar(int apps, string buttonClass)
        {
            var list = new List<Element>
            {
                new(new Rect(0, 0, 48, 40), "Start", ""),
                new(new Rect(48, 0, 392, 40), "TrayDummySearchControl", ""),
                new(new Rect(392, 0, 440, 40), "TrayButton", ""), // Task View: a "tray" class, but on the left
                new(new Rect(444, 0, 1600, 40), "ReBarWindow32", ""),
                new(new Rect(444, 0, 1600, 40), "MSTaskSwWClass", ""),
                new(new Rect(444, 0, 1600, 40), "MSTaskListWClass", ""),
                new(new Rect(1600, 0, 1920, 40), "TrayNotifyWnd", ""),
                new(new Rect(1606, 0, 1646, 40), "TrayButton", ""), // Touch keyboard
                new(new Rect(1650, 0, 1750, 40), "ToolbarWindow32", ""),
                new(new Rect(1650, 0, 1674, 40), "", ""),
                new(new Rect(1800, 0, 1880, 40), "TrayClockWClass", ""),
                new(new Rect(1915, 0, 1920, 40), "TrayShowDesktopButtonWClass", ""),
            };
            for (int i = 0; i < apps; i++) list.Add(new(new Rect(444 + i * 48, 0, 492 + i * 48, 40), buttonClass, ""));
            return list;
        }

        [Theory]
        [InlineData("")]                 // App buttons with no class of their own
        [InlineData("MSTaskListWClass")] // App buttons reporting the class of the window that holds them
        public void OnWindows10_SitsJustLeftOfTheTray(string buttonClass)
        {
            int? x = Layout.FindSlot(1920, 40, Windows10Taskbar(8, buttonClass), 1600, Width, Margin, out int spare);

            Assert.Equal(1600 - Margin - Width, x);
            Assert.True(spare > 600);
        }

        [Theory]
        [InlineData("")]
        [InlineData("MSTaskListWClass")]
        public void OnWindows10_HidesRatherThanCoveringAppButtons(string buttonClass)
        {
            // 23 buttons reach x=1548, leaving 52 pixels before the tray: not enough for 100
            Assert.Null(Layout.FindSlot(1920, 40, Windows10Taskbar(23, buttonClass), 1600, Width, Margin, out _));
        }

        [Fact]
        public void OnWindows10_KeepsClearOfToolbarsBesideTheTray()
        {
            // A Links toolbar the user added, just left of the tray
            var elements = Windows10Taskbar(3, "");
            elements.Add(new(new Rect(1500, 0, 1596, 40), "ToolbarWindow32", ""));

            Assert.Equal(1500 - Margin - Width, Layout.FindSlot(1920, 40, elements, 1600, Width, Margin, out _));
        }

        [Fact]
        public void OnAWindows10SecondTaskbar_SitsLeftOfTheClock()
        {
            var elements = new List<Element>
            {
                new(new Rect(0, 0, 48, 40), "Start", ""),
                new(new Rect(48, 0, 1880, 40), "WorkerW", ""),
                new(new Rect(48, 0, 1880, 40), "MSTaskListWClass", ""),
                new(new Rect(48, 0, 96, 40), "", ""),
                new(new Rect(1800, 0, 1880, 40), "ClockButton", ""),
            };

            Assert.Equal(1800 - Margin - Width, Layout.FindSlot(1920, 40, elements, 0, Width, Margin, out _));
        }
    }

    /// <summary>Turning the taskbar info on and off from SoundBar.</summary>
    [Collection("StartupFolder")] // Shares MainViewModel.StartupFolderOverride with MainViewModelTests
    public class TaskbarHelperServiceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"test_taskbar_{Guid.NewGuid():N}");
        private readonly string _appFolder;
        private readonly string _startupFolder;
        private bool _running;
        private int _starts, _stops;

        public TaskbarHelperServiceTests()
        {
            _appFolder = Path.Combine(_root, "app");
            _startupFolder = Path.Combine(_root, "startup");
            Directory.CreateDirectory(_appFolder);
            Directory.CreateDirectory(_startupFolder);
            // Opening SoundBar checks its own startup shortcut, so keep that out of the real Startup folder too
            MainViewModel.StartupFolderOverride = _startupFolder;
        }

        public void Dispose()
        {
            MainViewModel.StartupFolderOverride = null;
            try { Directory.Delete(_root, true); } catch { }
        }

        private TaskbarHelperService Service(string? appFolder = null) => new(
            appFolder ?? _appFolder, _startupFolder,
            isRunning: () => _running,
            start: _ => { _starts++; _running = true; },
            stop: () => { _stops++; _running = false; });

        private string ShortcutPath => TaskbarHelperInfo.GetStartupShortcutPath(_startupFolder);

        private void InstallHelper(string? folder = null) =>
            File.WriteAllText(Path.Combine(folder ?? _appFolder, TaskbarHelperInfo.ExeName), "");

        [Fact]
        public void Enable_StartsItNowAndAtSignIn()
        {
            InstallHelper();
            var service = Service();

            Assert.True(service.Enable());

            Assert.True(File.Exists(ShortcutPath));
            Assert.True(TaskbarHelperInfo.SamePath(ShortcutFile.ReadTarget(ShortcutPath), service.HelperPath));
            Assert.True(service.IsEnabled);
            Assert.Equal(1, _starts);
        }

        [Fact]
        public void Enable_DoesNotStartASecondCopy()
        {
            InstallHelper();
            _running = true;

            Service().Enable();

            Assert.Equal(0, _starts);
        }

        [Fact]
        public void Enable_DoesNothingWhenTheHelperIsMissing()
        {
            var service = Service();

            Assert.False(service.IsAvailable);
            Assert.False(service.Enable());
            Assert.False(File.Exists(ShortcutPath));
            Assert.Equal(0, _starts);
        }

        [Fact]
        public void Disable_RemovesTheShortcutAndClosesIt()
        {
            InstallHelper();
            var service = Service();
            service.Enable();

            service.Disable();

            Assert.False(File.Exists(ShortcutPath));
            Assert.False(service.IsEnabled);
            Assert.Equal(1, _stops);
        }

        [Fact]
        public void Disable_LeavesAnotherCopysShortcutAlone()
        {
            // The installed copy has it turned on. A test build elsewhere mustn't switch that off.
            string installed = Path.Combine(_root, "installed");
            Directory.CreateDirectory(installed);
            InstallHelper(installed);
            InstallHelper();
            Service(installed).Enable();

            var testBuild = Service();
            Assert.False(testBuild.IsEnabled);
            testBuild.Disable();

            Assert.True(File.Exists(ShortcutPath));
        }

        [Fact]
        public void Disable_RemovesAShortcutToAHelperThatNoLongerExists()
        {
            string gone = Path.Combine(_root, "gone");
            Directory.CreateDirectory(gone);
            InstallHelper(gone);
            Service(gone).Enable();
            File.Delete(Path.Combine(gone, TaskbarHelperInfo.ExeName));

            Service().Disable();

            Assert.False(File.Exists(ShortcutPath));
        }

        [Fact]
        public void EnsureRunning_StartsItOnlyWhenTurnedOnAndNotRunning()
        {
            InstallHelper();
            var service = Service();

            service.EnsureRunningIfEnabled();
            Assert.Equal(0, _starts); // Turned off

            service.Enable();
            _running = false; // Closed from Task Manager, say
            service.EnsureRunningIfEnabled();
            Assert.Equal(2, _starts);

            service.EnsureRunningIfEnabled();
            Assert.Equal(2, _starts); // Already running
        }

        [Theory]
        [InlineData(false, true, true)]   // Updated from any older version, or a new install
        [InlineData(true, true, false)]   // Already seen it
        [InlineData(false, false, false)] // This copy has no helper
        public void TheOfferIsShownOnceToEveryone(bool hasSeenOffer, bool helperAvailable, bool expected)
        {
            Assert.Equal(expected, TaskbarHelperService.ShouldOfferTaskbarInfo(hasSeenOffer, helperAvailable));
        }

        [Fact]
        public void OlderSettingsFiles_HaveNotSeenTheOffer()
        {
            // A config.json from v4.4.0, which knew nothing about the taskbar info
            string path = Path.Combine(_root, "config.json");
            File.WriteAllText(path, "{\"RunAtStartup\":true,\"HasCreatedDesktopShortcut\":true}");

            var settings = new SettingsService(path);

            Assert.False(settings.Settings.HasSeenTaskbarOffer);
            settings.Settings.HasSeenTaskbarOffer = true;
            settings.SaveSettings();
            Assert.True(new SettingsService(path).Settings.HasSeenTaskbarOffer);
        }

        [Fact]
        public void TheSettingsSwitch_TurnsTheHelperOnAndOff()
        {
            InstallHelper();
            var service = Service();
            var audio = new Mock<IAudioMixerService>();
            audio.Setup(a => a.GetActiveAudioSessions()).Returns(new List<AudioSessionData>());
            audio.Setup(a => a.GetAudioDevices()).Returns(new List<AudioDeviceModel>());
            audio.Setup(a => a.GetInputDevices()).Returns(new List<AudioDeviceModel>());
            using var viewModel = new MainViewModel(new SettingsService(Path.Combine(_root, "vm.json")), audio.Object, taskbarHelper: service);

            Assert.False(viewModel.ShowOnTaskbar);
            viewModel.ShowOnTaskbar = true;
            Assert.True(service.IsEnabled);
            Assert.True(_running);

            // Turned off from the taskbar itself while SoundBar was open
            File.Delete(ShortcutPath);
            viewModel.RefreshShowOnTaskbar();
            Assert.False(viewModel.ShowOnTaskbar);
        }

        [Fact]
        public void TheSettingsSwitch_StaysOffWhenTheHelperIsMissing()
        {
            var audio = new Mock<IAudioMixerService>();
            audio.Setup(a => a.GetActiveAudioSessions()).Returns(new List<AudioSessionData>());
            audio.Setup(a => a.GetAudioDevices()).Returns(new List<AudioDeviceModel>());
            audio.Setup(a => a.GetInputDevices()).Returns(new List<AudioDeviceModel>());
            using var viewModel = new MainViewModel(new SettingsService(Path.Combine(_root, "vm.json")), audio.Object, taskbarHelper: Service());

            Assert.False(viewModel.CanShowOnTaskbar);
            viewModel.ShowOnTaskbar = true;
            Assert.False(viewModel.ShowOnTaskbar);
            Assert.Equal(0, _starts);
        }

        [Fact]
        public void HelperSettings_RememberTheMonitor_AndSurviveABrokenFile()
        {
            string path = Path.Combine(_root, "taskbar.json");
            new HelperSettings { Monitor = @"\\.\DISPLAY2" }.Save(path);
            Assert.Equal(@"\\.\DISPLAY2", HelperSettings.Load(path).Monitor);
            Assert.False(File.Exists(path + ".tmp"));

            File.WriteAllText(path, "{ not json");
            Assert.Null(HelperSettings.Load(path).Monitor);
        }
    }

    /// <summary>The script that installs updates has to deal with the taskbar helper too.</summary>
    public class UpdateScriptTests
    {
        private static string Script(bool helperRunning) => UpdateService.BuildUpdateScript(
            @"C:\Temp\SoundBarUpdate\extracted", @"C:\SoundBar", @"C:\Temp\SoundBarUpdate", @"C:\SoundBar\SoundBar.exe", helperRunning);

        [Fact]
        public void SwitchesToUtf8_BeforeUsingAnyPath()
        {
            // The update unpacks under the user's Temp folder, so a name like Zoë ends up in the paths.
            // Command Prompt starts in the old code page and wouldn't find those folders without this.
            string script = UpdateService.BuildUpdateScript(
                @"C:\Users\Zoë\AppData\Local\Temp\SoundBarUpdate\extracted", @"C:\SoundBar", @"C:\Users\Zoë\AppData\Local\Temp\SoundBarUpdate",
                @"C:\SoundBar\SoundBar.exe", restartTaskbarHelper: false);

            int utf8 = script.IndexOf("chcp 65001", StringComparison.Ordinal);
            Assert.True(utf8 > 0 && utf8 < script.IndexOf("Zoë", StringComparison.Ordinal));
        }

        [Fact]
        public void WaitsForTheHelperToClose_BeforeCopyingFiles()
        {
            string script = Script(helperRunning: true);

            int waitForHelper = script.IndexOf("IMAGENAME eq SoundBarTaskbar.exe", StringComparison.Ordinal);
            int forceClose = script.IndexOf("taskkill /F /IM SoundBarTaskbar.exe", StringComparison.Ordinal);
            int copy = script.IndexOf("robocopy", StringComparison.Ordinal);
            Assert.True(waitForHelper > 0 && forceClose > waitForHelper && copy > forceClose);
        }

        [Fact]
        public void RestartsTheHelper_OnlyIfItWasRunning()
        {
            const string start = "start \"\" \"C:\\SoundBar\\SoundBarTaskbar.exe\"";

            string running = Script(helperRunning: true);
            Assert.Contains(start, running);
            Assert.True(running.IndexOf(start, StringComparison.Ordinal) > running.IndexOf("robocopy", StringComparison.Ordinal));

            Assert.DoesNotContain(start, Script(helperRunning: false));
        }

        [Fact]
        public void StillRestartsSoundBar()
        {
            Assert.Contains("start \"\" \"C:\\SoundBar\\SoundBar.exe\"", Script(helperRunning: false));
        }
    }
}

using System.Runtime.InteropServices;

namespace SoundBarTaskbar
{
    /// <summary>What a scan of one taskbar found. Element rectangles are relative to the taskbar.</summary>
    internal sealed record TaskbarScan(IntPtr Taskbar, int Width, int Height, IReadOnlyList<TaskbarElement> Elements);

    /// <summary>
    /// Finds where the taskbar's buttons, tray and clock are, using Windows' accessibility interface
    /// (UI Automation). That's the only reliable way on Windows 11, whose taskbar draws its buttons itself.
    ///
    /// It runs on its own thread and only when asked. UI Automation calls go into Explorer, and making them from
    /// the window thread can deadlock with the taskbar we live in.
    /// </summary>
    internal sealed unsafe class TaskbarScanner : IDisposable
    {
        private static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
        private static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

        private const int CLSCTX_INPROC_SERVER = 1;
        private const int TreeScope_Descendants = 4;
        private const int AutomationElementMode_None = 0;
        private const int UIA_BoundingRectanglePropertyId = 30001;
        private const int UIA_AutomationIdPropertyId = 30011;
        private const int UIA_ClassNamePropertyId = 30012;

        // Positions in each interface's table of functions, from UIAutomationClient.h in the Windows SDK
        private const int Slot_Release = 2;
        private const int Automation_ElementFromHandle = 6;
        private const int Automation_CreateCacheRequest = 20;
        private const int Automation_CreateTrueCondition = 21;
        private const int Element_FindAllBuildCache = 8;
        private const int Element_get_CachedAutomationId = 61;
        private const int Element_get_CachedClassName = 62;
        private const int Element_get_CachedBoundingRectangle = 75;
        private const int CacheRequest_AddProperty = 3;
        private const int CacheRequest_put_AutomationElementMode = 11;
        private const int Array_get_Length = 3;
        private const int Array_GetElement = 4;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int context, in Guid iid, out IntPtr instance);

        private readonly AutoResetEvent _wake = new(false);
        private readonly Thread _thread;
        private readonly Action<TaskbarScan> _done;
        private volatile bool _stopping;
        private IntPtr _requested;

        /// <param name="done">Called on the scanner thread with each finished scan. Keep it quick.</param>
        public TaskbarScanner(Action<TaskbarScan> done)
        {
            _done = done;
            _thread = new Thread(Run) { IsBackground = true, Name = "SoundBarTaskbar scanner" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        /// <summary>Asks for a scan of this taskbar. Requests made while one is running are merged into one.</summary>
        public void Request(IntPtr taskbar)
        {
            Interlocked.Exchange(ref _requested, taskbar);
            _wake.Set();
        }

        private void Run()
        {
            IntPtr automation = IntPtr.Zero, cacheRequest = IntPtr.Zero, trueCondition = IntPtr.Zero;
            try
            {
                while (true)
                {
                    _wake.WaitOne();
                    if (_stopping) return;

                    IntPtr taskbar = Interlocked.Exchange(ref _requested, IntPtr.Zero);
                    if (taskbar == IntPtr.Zero || !Native.IsWindow(taskbar)) continue;

                    try
                    {
                        if (automation == IntPtr.Zero)
                            (automation, cacheRequest, trueCondition) = CreateAutomation();

                        var scan = Scan(automation, cacheRequest, trueCondition, taskbar);
                        if (scan != null && !_stopping) _done(scan);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Taskbar scan failed: {ex.Message}");
                        // Start fresh next time, in case Explorer restarted underneath us
                        ReleaseAll(ref automation, ref cacheRequest, ref trueCondition);

                        // On the main taskbar, Windows' own tray window still tells us where the tray starts,
                        // so the indicator can show there even without the scan. (Other monitors' taskbars
                        // have no such window, and guessing could cover the clock, so they wait for a scan.)
                        if (!_stopping && Native.ClassNameOf(taskbar) == "Shell_TrayWnd" && Native.GetWindowRect(taskbar, out var bar))
                            _done(new TaskbarScan(taskbar, bar.Width, bar.Height, Array.Empty<TaskbarElement>()));
                    }
                }
            }
            finally
            {
                ReleaseAll(ref automation, ref cacheRequest, ref trueCondition);
            }
        }

        private static (IntPtr automation, IntPtr cacheRequest, IntPtr trueCondition) CreateAutomation()
        {
            IntPtr automation = IntPtr.Zero, cacheRequest = IntPtr.Zero, trueCondition = IntPtr.Zero;
            try
            {
                Check(CoCreateInstance(CLSID_CUIAutomation, IntPtr.Zero, CLSCTX_INPROC_SERVER, IID_IUIAutomation, out automation));
                Check(Call(automation, Automation_CreateCacheRequest, out cacheRequest));
                Check(Call(automation, Automation_CreateTrueCondition, out trueCondition));

                var addProperty = (delegate* unmanaged<IntPtr, int, int>)Slot(cacheRequest, CacheRequest_AddProperty);
                Check(addProperty(cacheRequest, UIA_BoundingRectanglePropertyId));
                Check(addProperty(cacheRequest, UIA_ClassNamePropertyId));
                Check(addProperty(cacheRequest, UIA_AutomationIdPropertyId));
                // Only fetch the cached values, not live links to every element (much cheaper)
                var setMode = (delegate* unmanaged<IntPtr, int, int>)Slot(cacheRequest, CacheRequest_put_AutomationElementMode);
                Check(setMode(cacheRequest, AutomationElementMode_None));

                return (automation, cacheRequest, trueCondition);
            }
            catch
            {
                ReleaseAll(ref automation, ref cacheRequest, ref trueCondition);
                throw;
            }
        }

        private static TaskbarScan? Scan(IntPtr automation, IntPtr cacheRequest, IntPtr trueCondition, IntPtr taskbar)
        {
            if (!Native.GetWindowRect(taskbar, out var bar)) return null;

            IntPtr root = IntPtr.Zero, array = IntPtr.Zero;
            try
            {
                var fromHandle = (delegate* unmanaged<IntPtr, IntPtr, IntPtr*, int>)Slot(automation, Automation_ElementFromHandle);
                Check(fromHandle(automation, taskbar, &root));

                var findAll = (delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, IntPtr*, int>)Slot(root, Element_FindAllBuildCache);
                Check(findAll(root, TreeScope_Descendants, trueCondition, cacheRequest, &array));
                if (array == IntPtr.Zero) return new TaskbarScan(taskbar, bar.Width, bar.Height, Array.Empty<TaskbarElement>());

                int length = 0;
                var getLength = (delegate* unmanaged<IntPtr, int*, int>)Slot(array, Array_get_Length);
                Check(getLength(array, &length));

                var getElement = (delegate* unmanaged<IntPtr, int, IntPtr*, int>)Slot(array, Array_GetElement);
                var elements = new List<TaskbarElement>(length);
                for (int i = 0; i < length; i++)
                {
                    IntPtr element = IntPtr.Zero;
                    if (getElement(array, i, &element) < 0 || element == IntPtr.Zero) continue;
                    try
                    {
                        Native.RECT rect;
                        var getRect = (delegate* unmanaged<IntPtr, Native.RECT*, int>)Slot(element, Element_get_CachedBoundingRectangle);
                        if (getRect(element, &rect) < 0) continue;

                        string className = ReadString(element, Element_get_CachedClassName);
                        string automationId = ReadString(element, Element_get_CachedAutomationId);
                        var relative = new Native.RECT(rect.Left - bar.Left, rect.Top - bar.Top, rect.Right - bar.Left, rect.Bottom - bar.Top);
                        elements.Add(new TaskbarElement(relative, className, automationId));
                    }
                    finally
                    {
                        Release(element);
                    }
                }
                return new TaskbarScan(taskbar, bar.Width, bar.Height, elements);
            }
            finally
            {
                Release(array);
                Release(root);
            }
        }

        private static string ReadString(IntPtr element, int slot)
        {
            IntPtr bstr = IntPtr.Zero;
            var get = (delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(element, slot);
            if (get(element, &bstr) < 0 || bstr == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringBSTR(bstr); }
            finally { Marshal.FreeBSTR(bstr); }
        }

        private static IntPtr Slot(IntPtr comObject, int index) => (*(IntPtr**)comObject)[index];

        private static int Call(IntPtr comObject, int slot, out IntPtr result)
        {
            IntPtr value = IntPtr.Zero;
            int hr = ((delegate* unmanaged<IntPtr, IntPtr*, int>)Slot(comObject, slot))(comObject, &value);
            result = value;
            return hr;
        }

        private static void Release(IntPtr comObject)
        {
            if (comObject != IntPtr.Zero)
                ((delegate* unmanaged<IntPtr, uint>)Slot(comObject, Slot_Release))(comObject);
        }

        private static void ReleaseAll(ref IntPtr automation, ref IntPtr cacheRequest, ref IntPtr trueCondition)
        {
            Release(trueCondition); trueCondition = IntPtr.Zero;
            Release(cacheRequest); cacheRequest = IntPtr.Zero;
            Release(automation); automation = IntPtr.Zero;
        }

        private static void Check(int hr)
        {
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        public void Dispose()
        {
            _stopping = true;
            _wake.Set();
            // A scan is normally ~10 ms. If Explorer is hung, don't hold up exiting.
            if (_thread.Join(TimeSpan.FromSeconds(2)))
                _wake.Dispose();
        }
    }
}

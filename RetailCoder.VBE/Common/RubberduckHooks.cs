using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Rubberduck.Common.WinAPI;

namespace Rubberduck.Common
{
    public class RubberduckHooks : IRubberduckHooks
    {
        private readonly IntPtr _mainWindowHandle;

        private readonly IntPtr _oldWndPointer;
        private readonly User32.WndProc _oldWndProc;
        private User32.WndProc _newWndProc;

        private readonly ITimerHook _timerHook;


        private const int WA_INACTIVE = 0;
        private const int WA_ACTIVE = 1;

        public RubberduckHooks(IntPtr mainWindowHandle, ITimerHook timerHook)
        {
            _mainWindowHandle = mainWindowHandle;
            _oldWndProc = WindowProc;
            _newWndProc = WindowProc;
            _oldWndPointer = User32.SetWindowLong(_mainWindowHandle, (int)WindowLongFlags.GWL_WNDPROC, _newWndProc);
            _oldWndProc = (User32.WndProc)Marshal.GetDelegateForFunctionPointer(_oldWndPointer, typeof(User32.WndProc));

            _timerHook = timerHook;
            _timerHook.Tick += timerHook_Tick;
        }

        private readonly IList<IHook> _hooks = new List<IHook>(); 
        public IEnumerable<IHook> Hooks { get { return _hooks; } }

        public void AddHook<THook>(THook hook) where THook : IHook
        {
            _hooks.Add(hook);
        }

        public event EventHandler<HookEventArgs> MessageReceived;

        public void OnMessageReceived()
        {
            throw new NotImplementedException();
        }

        private void OnMessageReceived(object sender, HookEventArgs args)
        {
            var handler = MessageReceived;
            if (handler != null)
            {
                handler.Invoke(sender, args);
            }
        }
        
        public bool IsAttached { get; private set; }

        public void Attach()
        {
            if (IsAttached)
            {
                return;
            }

            foreach (var hook in Hooks)
            {
                hook.MessageReceived += hook_MessageReceived;
                hook.Attach();
            }

            IsAttached = true;
        }

        public void Detach()
        {
            if (!IsAttached)
            {
                return;
            }

            foreach (var hook in Hooks)
            {
                hook.MessageReceived -= hook_MessageReceived;
                hook.Detach();
            }

            IsAttached = false;
        }

        private void hook_MessageReceived(object sender, HookEventArgs e)
        {
            OnMessageReceived(sender, e);
        }

        private void timerHook_Tick(object sender, EventArgs e)
        {
            if (User32.GetForegroundWindow() == _mainWindowHandle && !IsAttached)
            {
                Attach();
            }
            else
            {
                Detach();
            }
        }

        public void Dispose()
        {
            _timerHook.Tick -= timerHook_Tick;
            _timerHook.Detach();

            Detach();
        }

        private IntPtr WindowProc(IntPtr hWnd, int uMsg, int wParam, int lParam)
        {
            try
            {
                var processed = false;
                if (hWnd == _mainWindowHandle)
                {
                    switch ((WM)uMsg)
                    {
                        case WM.HOTKEY:
                            if (GetWindowThread(User32.GetForegroundWindow()) == GetWindowThread(_mainWindowHandle))
                            {
                                var hook = Hooks.OfType<IHotKeyHook>().SingleOrDefault(k => k.HookInfo.HookId == (IntPtr)wParam);
                                if (hook != null)
                                {
                                    hook.OnMessageReceived();
                                    processed = true;
                                }
                            }
                            break;

                        case WM.ACTIVATEAPP:
                            switch (LoWord(wParam))
                            {
                                case WA_ACTIVE:
                                    Attach();
                                    break;

                                case WA_INACTIVE:
                                    Detach();
                                    break;
                            }

                            break;
                    }
                }

                if (!processed)
                {
                    return User32.CallWindowProc(_oldWndProc, hWnd, uMsg, wParam, lParam);
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }

            return User32.CallWindowProc(_oldWndProc, hWnd, uMsg, wParam, lParam);
        }

        /// <summary>
        /// Gets the integer portion of a word
        /// </summary>
        private static int LoWord(int dw)
        {
            return (dw & 0x8000) != 0
                ? 0x8000 | (dw & 0x7FFF)
                : dw & 0xFFFF;
        }

        private IntPtr GetWindowThread(IntPtr hWnd)
        {
            uint hThread;
            User32.GetWindowThreadProcessId(hWnd, out hThread);

            return (IntPtr)hThread;
        }
    }
}
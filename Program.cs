using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace FileSender;

class Program
{
    private const string MutexName = "FilanShare_SingleInstance_App_Mutex_48b29f";
    private const string EventName = "FilanShare_SingleInstance_App_Wakeup_48b29f";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _wakeUpEvent;
    private static volatile bool _isDisposed;

    [STAThread]
    public static void Main(string[] args)
    {
        bool isFirstInstance = false;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            // Another instance is already running!
            // Signal the running instance to restore and focus its window, then exit.
            Console.WriteLine("[FilanShare] Aplikasi sudah berjalan di latar belakang/system tray. Mengaktifkan jendela...");
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var wakeEvent = EventWaitHandle.OpenExisting(EventName);
                    wakeEvent.Set();
                }
                catch
                {
                }
            }
            return;
        }

        // We are the first instance: start wake-up event listener
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _wakeUpEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                var wakeThread = new Thread(ListenForWakeUp)
                {
                    IsBackground = true,
                    Name = "FilanShareWakeUpListener"
                };
                wakeThread.Start();
            }
            catch
            {
                // Proceed normally
            }
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _isDisposed = true;
            try { _wakeUpEvent?.Dispose(); } catch { }
            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                try { _singleInstanceMutex.Dispose(); } catch { }
            }
        }
    }

    private static void ListenForWakeUp()
    {
        while (!_isDisposed && _wakeUpEvent != null)
        {
            try
            {
                if (_wakeUpEvent.WaitOne())
                {
                    if (_isDisposed) break;

                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow mainWindow)
                            {
                                mainWindow.RestoreFromTray();
                            }
                        }
                        catch { }
                    });
                }
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/*
   Copyright 2022 Nora Beda

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using ProcessMonitor.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Terminal.Gui;

namespace ProcessMonitor
{
    internal static class Program
    {
        private static bool sGuiInitialized;
        private static readonly Dictionary<int, ProcessWindow> sProcessWindows;

        private static Action? sReloadProcessList;
        static Program()
        {
            sGuiInitialized = false;
            sProcessWindows = new Dictionary<int, ProcessWindow>();
            sReloadProcessList = null;
        }

        public static void Quit()
        {
            sProcessWindows.ToList().ForEach(pair => pair.Value.Close());
            Application.RequestStop();
        }

        public static void OpenProcess(Process process)
        {
            if (process.HasExited)
            {
                MessageBox.Query("Process exited", $"The process {process.ProcessName} (PID: {process.Id}) has already exited", "OK");
                ReloadProcessList();

                return;
            }

            if (sProcessWindows.ContainsKey(process.Id))
            {
                MessageBox.Query("Process already open", $"The process {process.ProcessName} (PID: {process.Id}) has already been opened", "OK");
                return;
            }

            var window = new ProcessWindow(process);
            window.OnClosed += OnWindowClosed;

            Application.Top.Add(window);
        }

        private static void OnWindowClosed(ProcessWindow window)
        {
            Application.Top.Remove(window);
            sProcessWindows.Remove(window.ProcessID);

            try
            {
                window.Dispose();
            }
            catch (Exception)
            {
                // should be fine(?)
            }

            ReloadProcessList();
        }

        private static void QuitHandler()
        {
            int result = MessageBox.Query("Quit", "Are you sure you want to quit?", "Yes", "No");
            if (result == 0)
            {
                Quit();
            }
        }

        public static void OpenURL(string url)
        {
            var startInfo = new ProcessStartInfo
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/c start {url}";
            }
            else
            {
                startInfo.Arguments = url;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    startInfo.FileName = "xdg-open";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    startInfo.FileName = "open";
                }
                else
                {
                    throw new PlatformNotSupportedException();
                }
            }

            using var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();
        }

        public static void ReloadProcessList() => sReloadProcessList?.Invoke();
        private static int RunApp(IReadOnlyList<string> args)
        {
            Thread.CurrentThread.Name = "UI thread";
            Monitor.StartWatching();

            Console.OutputEncoding = Encoding.Default;
            if (Debugger.IsAttached)
            {
                CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            }

            if (args.Count > 0 && args.Contains("--use-system-console"))
            {
                Application.UseSystemConsole = true;
            }

            Application.Init();
            sGuiInitialized = true;

            var processView = new ProcessView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(0),
                Height = Dim.Fill(1)
            };

            sReloadProcessList = processView.ReloadProcessList;
            var statusBar = new StatusBar(new StatusItem[]
            {
                new StatusItem(Key.CtrlMask | Key.Q, "~^Q~ Quit", QuitHandler),
                new StatusItem(Key.CtrlMask | Key.R, "~^R~ Reload process list", sReloadProcessList),
                new StatusItem(Key.Null, Process.GetCurrentProcess().Id.ToString(), null),
                new StatusItem(Key.Null, "Report an issue", () => OpenURL("https://github.com/yodasoda1219/process-monitor/issues"))
            });

            var top = Application.Top;
            top.Add(processView, statusBar);

            Application.Run();
            Application.Shutdown();
            sGuiInitialized = false;

            if (!Monitor.StopWatching())
            {
                throw new Exception("The process monitor stopped before the end of the program!");
            }

            return 0;
        }

        private static void PrintException(Exception ex, TextWriter output, int tabs)
        {
            string tabString = string.Empty;
            for (int i = 0; i < tabs; i++)
            {
                tabString += '\t';
            }

            if (ex.StackTrace != null)
            {
                var lines = ex.StackTrace.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    output.WriteLine($"{tabString}{line}");
                }
            }

            output.WriteLine($"{tabString}Message: {ex.Message}");
            if (ex.Source != null)
            {
                output.WriteLine($"{tabString}Source: {ex.Source}");
            }

            var inner = ex.InnerException;
            if (inner != null)
            {
                var type = inner.GetType();
                output.WriteLine($"{tabString}Inner exception: {type.FullName ?? type.Name}");

                PrintException(inner, output, tabs + 1);
            }
        }

        private static void PrintException(Exception ex)
        {
            var output = Console.Error;

            var type = ex.GetType();
            output.WriteLine($"Exception caught: {type.FullName ?? type.Name}");

            PrintException(ex, output, 0);
        }

        public static int Main(string[] args)
        {
            try
            {
                return RunApp(args);
            }
            catch (Exception ex)
            {
                if (sGuiInitialized)
                {
                    Application.Shutdown();
                    sGuiInitialized = false;
                }

                if (Monitor.IsWatching)
                {
                    Monitor.StopWatching();
                }

                PrintException(ex);
                return 1;
            }
        }
    }
}
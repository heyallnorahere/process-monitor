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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ProcessMonitor
{
    public static class Monitor
    {
        private static readonly Dictionary<int, Process> sProcesses;
        private static bool sStopWatching;
        private static Thread? sWatcherThread;

        static Monitor()
        {
            sProcesses = new Dictionary<int, Process>();
            sStopWatching = false;
            sWatcherThread = null;
        }

        public static event Action<Process>? OnNewProcess;
        public static event Action<int>? OnProcessStopped;

        public static bool IsWatching => sWatcherThread != null;
        public static IEnumerable<Process> KnownProcesses => sProcesses.Values;

        public static bool StartWatching()
        {
            if (sWatcherThread != null)
            {
                return false;
            }

            sWatcherThread = new Thread(WatchProcesses)
            {
                Name = "Process watcher"
            };

            sWatcherThread.Start();
            return true;
        }

        public static bool StopWatching()
        {
            if (sWatcherThread == null)
            {
                return false;
            }

            sStopWatching = true;
            sWatcherThread.Join();

            return true;
        }

        public const int SleepInterval = 100;
        private static void WatchProcesses()
        {
            while (!sStopWatching)
            {
                var processes = Process.GetProcesses();

                var newProcesses = new List<Process>();
                var stoppedProcesses = new List<int>();

                foreach (var process in processes)
                {
                    if (!sProcesses.ContainsKey(process.Id))
                    {
                        newProcesses.Add(process);
                        OnNewProcess?.Invoke(process);
                    }
                }

                foreach (var id in sProcesses.Keys)
                {
                    if (Array.Find(processes, process => process.Id == id) == null)
                    {
                        stoppedProcesses.Add(id);
                        OnProcessStopped?.Invoke(id);
                    }
                }

                newProcesses.ForEach(process => sProcesses.Add(process.Id, process));
                stoppedProcesses.ForEach(id => sProcesses.Remove(id));

                Thread.Sleep(SleepInterval);
            }

            sStopWatching = false;
        }
    }
}
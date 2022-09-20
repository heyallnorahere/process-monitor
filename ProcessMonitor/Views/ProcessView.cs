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
using System.Linq;
using Terminal.Gui;

namespace ProcessMonitor.Views
{
    public sealed class ProcessView : FrameView
    {
        private sealed class ListedProcess : IComparable<ListedProcess>
        {
            public ListedProcess(Process process)
            {
                ProcessObject = process;

                Name = ProcessObject.ProcessName;
                ID = ProcessObject.Id;
            }

            public override string ToString() => $"{Name} | {ID}";
            public int CompareTo(ListedProcess? process) => ToString().CompareTo(process?.ToString());

            public string Name { get; }
            public int ID { get; }

            public Process ProcessObject { get; }
        }

        public ProcessView() : base("Active processes")
        {
            mProcesses = new List<Process>(Monitor.KnownProcesses);
            mListedProcesses = new List<ListedProcess>();

            mProcessListChanged = true;
            UpdateListedProcesses();

            var listView = new ListView(mListedProcesses)
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(0),
                Height = Dim.Fill(0),
                AllowsMarking = false,
                AllowsMultipleSelection = false,
                CanFocus = false
            };

            listView.OpenSelectedItem += OpenProcess;
            Add(listView);

            Monitor.OnNewProcess += AddProcess;
            Monitor.OnProcessStopped += RemoveProcess;
        }

        protected override void Dispose(bool disposing)
        {
            Monitor.OnNewProcess -= AddProcess;
            Monitor.OnProcessStopped -= RemoveProcess;

            lock (mProcesses)
            {
                mProcesses.Clear();
                mProcessListChanged = true;
            }

            UpdateListedProcesses();
            base.Dispose(disposing);
        }

        private void AddProcess(Process process)
        {
            lock (mProcesses)
            {
                mProcesses.Add(process);
                mProcessListChanged = true;
            }

            Application.MainLoop.Invoke(ReloadProcessList);
        }

        private void RemoveProcess(int id)
        {
            lock (mProcesses)
            {
                mProcesses.RemoveAll(process => process.Id == id);
                mProcessListChanged = true;
            }

            Application.MainLoop.Invoke(ReloadProcessList);
        }

        private void OpenProcess(ListViewItemEventArgs args)
        {
            ListedProcess process;
            if (args.Value is ListedProcess listedProcess)
            {
                process = listedProcess;
            }
            else if (args.Item >= 0 && args.Item < mListedProcesses.Count)
            {
                process = mListedProcesses[args.Item];
            }
            else
            {
                throw new ArgumentException("Invalid item event arguments!");
            }

            var result = MessageBox.Query("Open process", $"Open process {process.Name}? (PID: {process.ID})", "Confirm", "Cancel");
            if (result == 0)
            {
                Program.OpenProcess(process.ProcessObject);
            }
        }

        private void UpdateListedProcesses()
        {
            lock (mProcesses)
            {
                if (mProcessListChanged)
                {
                    // i hate 100+ character lines i hate 100+ character lines
                    var newProcesses = mProcesses.Where(process => mListedProcesses.Find(listed => listed.ID == process.Id) == null).Select(process => new ListedProcess(process));
                    var stoppedProcesses = mListedProcesses.Where(listed => mProcesses.Find(process => listed.ID == process.Id) == null).Select(listed => listed.ID);

                    mListedProcesses.RemoveAll(listed => stoppedProcesses.Contains(listed.ID));
                    mListedProcesses.AddRange(newProcesses);

                    mListedProcesses.Sort();
                    mProcessListChanged = false;
                }
            }
        }

        public void ReloadProcessList()
        {
            UpdateListedProcesses();
            Application.Refresh();
        }

        private bool mProcessListChanged;
        private readonly List<ListedProcess> mListedProcesses;
        private readonly List<Process> mProcesses;
    }
}
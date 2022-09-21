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

using ProcessMonitor.Diagnostics;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Terminal.Gui;

namespace ProcessMonitor.Views
{
    public sealed class ProcessWindow : Window
    {
        private const string TitleSuffix = " - Exited";
        public ProcessWindow(Process process)
        {
            Width = 100;
            Height = 30;
            CanFocus = true;

            ProcessObject = process;
            ProcessName = ProcessObject.ProcessName;
            ProcessID = ProcessObject.Id;

            mExited = ProcessObject.HasExited;
            Title = $"Process: {ProcessName} (PID: {ProcessID})";

            if (mExited)
            {
                Title += TitleSuffix;
                Program.ReloadProcessList();
            }

            var assembly = Assembly.GetExecutingAssembly();
            var types = assembly.GetTypes();

            // using all attribute data sets in the assembly for now
            mDataSet = new ProcessDataSet(ProcessObject);
            foreach (var type in types)
            {
                var interfaces = type.GetInterfaces();
                if (!interfaces.Contains(typeof(IAttributeDataSet)))
                {
                    continue;
                }

                var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Array.Empty<Type>());
                if (constructor == null)
                {
                    continue;
                }

                var instance = (IAttributeDataSet)constructor.Invoke(null);
                mDataSet.AddAttributeDataSet(instance);
            }

            mDataSetView = null;
            AddContent();

            ProcessObject.EnableRaisingEvents = true;
            ProcessObject.Exited += OnExited;

            if (!mExited)
            {
                mDataSet.StartRecording();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (mDataSet.IsRecording)
            {
                mDataSet.StopRecording();
            }

            ProcessObject.Exited -= OnExited;
            mDataSetView?.Dispose();

            base.Dispose(disposing);
        }

        private void OnExited(object? sender, EventArgs args)
        {
            if (mExited)
            {
                return;
            }

            if (mDataSet.IsRecording)
            {
                mDataSet.StopRecording();
            }

            Title += TitleSuffix;
            mExited = true;

            UpdateStatusLabel();
            Program.ReloadProcessList();
        }

        private void AddContent()
        {
            var contentView = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false
            };

            contentView.Add(new Label
            {
                X = 0,
                Y = 0,
                Text = $"Name: {ProcessName}"
            });

            contentView.Add(new Label
            {
                X = 0,
                Y = 1,
                Text = $"PID: {ProcessID}"
            });

            mStatusLabel = new Label
            {
                X = 0,
                Y = 2
            };

            UpdateStatusLabel();
            contentView.Add(mStatusLabel);

            contentView.Add(mDataSetView = new DataSetView(mDataSet)
            {
                X = 0,
                Y = 4,
                Width = Dim.Fill(),
                Height = Dim.Fill(2),
                CanFocus = false
            });

            const int padding = 2;
            const string closeButtonText = "Close";

            var closeButton = new Button
            {
                X = Pos.Center(),
                Y = Pos.Bottom(contentView) - 1,
                Width = closeButtonText.Length + padding * 2,
                Height = 1,
                Text = closeButtonText
            };

            closeButton.Clicked += Close;
            contentView.Add(closeButton);

            Add(contentView);
        }

        private void UpdateStatusLabel()
        {
            mStatusLabel!.Text = "Status: " + (mExited ? "stopped" : "running");
        }

        public event Action<ProcessWindow>? OnClosed;
        public void Close()
        {
            if (mDataSet.IsRecording)
            {
                mDataSet.StopRecording();
            }

            SaveQuery();
            OnClosed?.Invoke(this);
        }

        private void SaveQuery()
        {
            // todo: ask if the user wants to save data
        }

        public Process ProcessObject { get; }
        public string ProcessName { get; }
        public int ProcessID { get; }
        public bool HasProcessExited => mExited;

        private bool mExited;
        private Label? mStatusLabel;

        private readonly ProcessDataSet mDataSet;
        private DataSetView? mDataSetView;
    }
}
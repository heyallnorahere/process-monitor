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
using System.Diagnostics;
using Terminal.Gui;

namespace ProcessMonitor.Views
{
    public sealed class ProcessWindow : Window
    {
        private const string TitleSuffix = " - Exited";
        public ProcessWindow(Process process)
        {
            Width = Dim.Percent(50);
            Height = Dim.Percent(75);
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

            AddContent();

            ProcessObject.EnableRaisingEvents = true;
            ProcessObject.Exited += OnExited;
        }

        protected override void Dispose(bool disposing)
        {
            ProcessObject.Exited -= OnExited;

            base.Dispose(disposing);
        }

        private void OnExited(object? sender, EventArgs args)
        {
            if (mExited)
            {
                return;
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
            OnClosed?.Invoke(this);
        }

        public Process ProcessObject { get; }
        public string ProcessName { get; }
        public int ProcessID { get; }
        public bool HasProcessExited => mExited;

        private bool mExited;
        private Label? mStatusLabel;
    }
}
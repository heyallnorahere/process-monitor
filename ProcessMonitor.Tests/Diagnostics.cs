using ProcessMonitor.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Xunit;

namespace ProcessMonitor.Tests
{
    public sealed class Diagnostics
    {
        private static int sInstanceCount;
        private static readonly object sLock;

        private static readonly Type[] sDataSetTypes;
        static Diagnostics()
        {
            sInstanceCount = 0;
            sLock = new object();

            sDataSetTypes = new Type[]
            {
                typeof(ProcessCPUDataSet),
                typeof(ProcessMemoryDataSet)
            };
        }

        public Diagnostics()
        {
            lock (sLock)
            {
                if (sInstanceCount == 0)
                {
                    Monitor.StartWatching();
                }

                sInstanceCount++;
            }
        }

        ~Diagnostics()
        {
            lock (sLock)
            {
                sInstanceCount--;
                if (sInstanceCount == 0)
                {
                    Monitor.StopWatching();
                }
            }
        }

        private static void RunCommand(string command, out Process process)
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var startInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd" : "/bin/bash",
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };

            startInfo.ArgumentList.Add(isWindows ? "/c" : "-c");
            startInfo.ArgumentList.Add(command);

            process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();
        }

        [Fact]
        public void MonitorBasic()
        {
            // just a test command that won't exit by itself
            Process? startedProcess = null;
            var autoResetEvent = new AutoResetEvent(false);

            var onNew = (Process process) =>
            {
                if (startedProcess == null)
                {
                    return;
                }

                lock (startedProcess)
                {
                    if (process.Id == startedProcess.Id)
                    {
                        autoResetEvent.Set();
                    }
                }
            };

            var onStopped = (int pid) =>
            {
                if (startedProcess == null)
                {
                    return;
                }

                lock (startedProcess)
                {
                    if (pid == startedProcess.Id)
                    {
                        autoResetEvent.Set();
                    }
                }
            };

            Monitor.OnNewProcess += onNew;
            Monitor.OnProcessStopped += onStopped;

            RunCommand("python3", out startedProcess);

            Assert.True(Monitor.IsWatching);
            Assert.True(autoResetEvent.WaitOne(Monitor.SleepInterval * 5), "OnNewProcess did not trigger");

            lock (startedProcess)
            {
                startedProcess.Kill();
                startedProcess.WaitForExit();
            }

            Assert.True(autoResetEvent.WaitOne(Monitor.SleepInterval * 5), "OnProcessStopped did not trigger");

            Monitor.OnNewProcess -= onNew;
            Monitor.OnProcessStopped -= onStopped;

            try
            {
                startedProcess.Dispose();
            }
            catch (Exception)
            {
                // what do i do here lmao
            }
        }

        private static ProcessDataSet RecordFrames(string command, int frameCount = 1, int delay = 1000)
        {
            RunCommand(command, out var process);
            using var startedProcess = process;

            var dataSet = new ProcessDataSet(startedProcess);
            foreach (var type in sDataSetTypes)
            {
                var interfaces = type.GetInterfaces();
                if (!interfaces.Contains(typeof(IAttributeDataSet)))
                {
                    throw new ArgumentException("Invalid data set type!");
                }

                var constructor = type.GetConstructor(Array.Empty<Type>());
                if (constructor == null)
                {
                    throw new ArgumentException("Could not find a valid constructor!");
                }

                var instance = (IAttributeDataSet)constructor.Invoke(null);
                if (!dataSet.AddAttributeDataSet(instance))
                {
                    throw new ArgumentException("Duplicate attribute data set type!");
                }
            }

            for (int i = 0; i < frameCount; i++)
            {
                if (startedProcess.HasExited)
                {
                    string output = startedProcess.StandardOutput.ReadToEnd();
                    throw new Exception("Process already exited with output:\n" + output);
                }

                if (i > 0)
                {
                    Thread.Sleep(delay);
                }

                if (!dataSet.Record())
                {
                    throw new Exception("Failed to record process data!");
                }
            }

            startedProcess.Kill();
            return dataSet;
        }

        [Fact]
        public void ProcessDataSetBasic()
        {
            var dataSet = RecordFrames("python3");

            var data = dataSet.Compile();
            Assert.NotEqual(0, data.Count);
        }

        [Fact]
        public void ProcessDataSetExporting()
        {
            var script = new StringBuilder();
            script.AppendLine("i = 0");
            script.AppendLine("while True:");
            script.AppendLine("\ti += 1");

            var dataSet = RecordFrames($"python3 -c \"{script.ToString().Replace("\"", "\\\"")}\"", 5);
            var paths = new List<string?>();

            var exporters = DataExporterAttribute.FindAll();
            foreach (var type in exporters.Keys)
            {
                var exporter = DataExporterAttribute.Instantiate(type);
                paths.Add(dataSet.Export(exporter));
            }

            Assert.NotEmpty(paths);
            Assert.DoesNotContain(null, paths);

            paths.ForEach(path => File.Delete(path!));
        }
    }
}
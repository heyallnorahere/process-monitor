using ProcessMonitor.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Xunit;

namespace ProcessMonitor.Tests
{
    public sealed class Diagnostics
    {
        private static int sInstanceCount;
        private static readonly object sLock;
        static Diagnostics()
        {
            sInstanceCount = 0;
            sLock = new object();
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

        private static Process RunCommand(string command)
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

            var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();
            return process;
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

            startedProcess = RunCommand("python");

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

        private static ProcessDataSet RecordSingleFrame(string command, params Type[] dataSetTypes)
        {
            using var process = RunCommand(command);

            var dataSet = new ProcessDataSet(process);
            foreach (var type in dataSetTypes)
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

            if (!dataSet.Record())
            {
                throw new Exception("Failed to record process data!");
            }

            process.Kill();
            return dataSet;
        }

        [Fact]
        public void ProcessDataSetBasic()
        {
            var dataSet = RecordSingleFrame("python", typeof(ProcessMemoryDataSet));

            var data = dataSet.Compile();
            Assert.Equal(1, data.Count);
        }

        [Fact]
        public void ProcessDataSetExporting()
        {
            var dataSet = RecordSingleFrame("python", typeof(ProcessMemoryDataSet));

            var exporters = DataExporterAttribute.FindAll();
            var paths = new List<string?>();

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
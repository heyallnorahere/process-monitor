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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ProcessMonitor.Diagnostics
{
    public interface IAttributeDataSet
    {
        public void Clear();
        public bool Remove(DateTime time);
        public void Record(DateTime time, Process process);

        public double this[DateTime time] { get; }
        public string DataName { get; }
    }

    public interface IDataExporter
    {
        public void Reset();
        public bool AddDataPoint(DateTime time, string key, double value);
        public string? Export();

        public string Extension { get; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DataExporterAttribute : Attribute
    {
        public static IDataExporter Instantiate(Type exporterType)
        {
            string typeName = exporterType.FullName ?? exporterType.Name;

            var interfaces = exporterType.GetInterfaces();
            if (!interfaces.Contains(typeof(IDataExporter)))
            {
                throw new ArgumentException($"Type {typeName} does not implement IDataExporter!");
            }

            var constructor = exporterType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, Array.Empty<Type>());
            if (constructor == null)
            {
                throw new ArgumentException($"Type {typeName} does not have a public constructor with no parameters!");
            }

            return (IDataExporter)constructor.Invoke(null);
        }

        public DataExporterAttribute()
        {
            DisplayName = string.Empty;
        }

        public string DisplayName { get; set; }

        public static IReadOnlyDictionary<Type, string> FindAll()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var types = assembly.GetTypes();

            var result = new Dictionary<Type, string>();
            foreach (var type in types)
            {
                if (!type.GetInterfaces().Contains(typeof(IDataExporter)))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<DataExporterAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                string displayName = attribute.DisplayName;
                if (displayName.Length == 0)
                {
                    displayName = type.Name;
                }

                result.Add(type, displayName);
            }

            return result;
        }
    }

    public sealed class ProcessDataSet
    {
        private struct ThreadData
        {
            public Thread ThreadObject { get; set; }
            public Dictionary<int, Func<bool>> Callbacks { get; set; }
        }

        private static int sDataSetCount;
        private static readonly Dictionary<int, ThreadData> sThreadData;

        static ProcessDataSet()
        {
            sDataSetCount = 0;
            sThreadData = new Dictionary<int, ThreadData>();
        }

        private static void WatchProcess(Process process)
        {
            int pid = process.Id;

            bool recording = false;
            while (true)
            {
                int interval;
                if (recording)
                {
                    lock (sThreadData)
                    {
                        var threadData = sThreadData[pid];
                        if (threadData.Callbacks.Count == 0)
                        {
                            sThreadData.Remove(pid);
                            return;
                        }

                        var callbacks = threadData.Callbacks.Values;
                        var tasks = callbacks.Select(Task.Run);

                        var task = Task.WhenAll(tasks);
                        task.Wait();

                        if (task.Result.Contains(false))
                        {
                            throw new ArgumentException("One or more data sets failed to record!");
                        }
                    }

                    // placeholder value
                    interval = 500;
                }
                else
                {
                    lock (sThreadData)
                    {
                        if (sThreadData[pid].Callbacks.Count > 0)
                        {
                            recording = true;
                        }
                    }

                    interval = recording ? 0 : 1;
                }

                Thread.Sleep(interval);
            }
        }

        private static void StartThread(Process process)
        {
            var data = new ThreadData
            {
                Callbacks = new Dictionary<int, Func<bool>>(),
                ThreadObject = new Thread(() => WatchProcess(process))
                {
                    Name = $"PID {process.Id} watcher"
                }
            };

            sThreadData.Add(process.Id, data);
        }

        public ProcessDataSet(Process process)
        {
            mID = sDataSetCount++;
            mProcess = process;

            mLock = new object();
            mDataSetKeys = new List<DateTime>();
            mAttributeDataSets = new Dictionary<Type, IAttributeDataSet>();
        }

        public bool StartRecording()
        {
            int pid = mProcess.Id;
            lock (sThreadData)
            {
                if (!sThreadData.ContainsKey(pid))
                {
                    StartThread(mProcess);
                }
                else if (sThreadData[pid].Callbacks.ContainsKey(mID))
                {
                    return false;
                }

                sThreadData[pid].Callbacks.Add(mID, Record);
                return true;
            }
        }

        public bool StopRecording()
        {
            int pid = mProcess.Id;
            lock (sThreadData)
            {
                if (!sThreadData.ContainsKey(pid) || !sThreadData[pid].Callbacks.ContainsKey(mID))
                {
                    return false;
                }

                sThreadData[pid].Callbacks.Remove(mID);
                return true;
            }
        }

        public event Action? OnDataRecorded;
        public bool Record()
        {
            if (mProcess.HasExited)
            {
                return true;
            }

            lock (mLock)
            {
                if (mAttributeDataSets.Count == 0)
                {
                    return false;
                }


                bool succeeded = true;
                var recorded = new List<IAttributeDataSet>();

                var now = DateTime.Now;
                foreach (var set in mAttributeDataSets.Values)
                {
                    try
                    {
                        set.Record(now, mProcess);
                        recorded.Add(set);
                    }
                    catch (Exception)
                    {
                        succeeded = false;
                        break;
                    }
                }

                if (succeeded)
                {
                    mDataSetKeys.Add(now);
                    OnDataRecorded?.Invoke();

                    return true;
                }
                else
                {
                    recorded.ForEach(set => set.Remove(now));
                    return false;
                }
            }
        }

        public void Clear()
        {
            lock (mLock)
            {
                mDataSetKeys.Clear();
                foreach (var set in mAttributeDataSets.Values)
                {
                    set.Clear();
                }
            }
        }

        private string? GetExportedData(IDataExporter exporter)
        {
            exporter.Reset();

            var data = Compile();
            foreach (var time in data.Keys)
            {
                var frame = data[time];
                foreach (var key in frame.Keys)
                {
                    double value = frame[key];
                    if (!exporter.AddDataPoint(time, key, value))
                    {
                        exporter.Reset();
                        return null;
                    }
                }
            }

            var exportedData = exporter.Export();
            exporter.Reset();

            return exportedData;
        }

        private string? GetExportPath(string extension)
        {
            if (mDataSetKeys.Count == 0)
            {
                return null;
            }

            string firstFrameTime = mDataSetKeys[0].ToString("yyyy_MM_dd_HH_mm_ss");
            string filename = $"{firstFrameTime}.export{extension}";
            string directory = Path.Join(Directory.GetCurrentDirectory(), "exports");

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return Path.Join(directory, filename);
        }

        public string? Export(IDataExporter exporter)
        {
            var data = GetExportedData(exporter);
            if (data == null)
            {
                return null;
            }

            var path = GetExportPath(exporter.Extension);
            if (path == null)
            {
                return null;
            }

            using Stream stream = new FileStream(path, FileMode.Create);
            using TextWriter writer = new StreamWriter(stream);

            writer.Write(data);
            writer.Flush();

            return path;
        }

        public async Task<string?> ExportAsync(IDataExporter exporter)
        {
            lock (mLock)
            {
                if (mDataSetKeys.Count == 0)
                {
                    return null;
                }
            }

            var data = await Task.Run(() => GetExportedData(exporter));
            if (data == null)
            {
                return null;
            }

            var path = GetExportPath(exporter.Extension);
            if (path == null)
            {
                return null;
            }

            using Stream stream = new FileStream(path, FileMode.Create);
            using TextWriter writer = new StreamWriter(stream);

            await writer.WriteAsync(path);
            await writer.FlushAsync();

            return path;
        }

        public IReadOnlyDictionary<DateTime, IReadOnlyDictionary<string, double>> Compile()
        {
            lock (mLock)
            {
                var result = new Dictionary<DateTime, IReadOnlyDictionary<string, double>>();
                foreach (var frameTime in mDataSetKeys)
                {
                    var frame = new Dictionary<string, double>();
                    foreach (var type in mAttributeDataSets.Keys)
                    {
                        var set = mAttributeDataSets[type];
                        string key = set.DataName;

                        try
                        {
                            double value = set[frameTime];
                            frame.Add(key, value);
                        }
                        catch (IndexOutOfRangeException)
                        {
                            continue;
                        }
                    }

                    result.Add(frameTime, frame);
                }

                return result;
            }
        }

        public bool AddAttributeDataSet(IAttributeDataSet dataSet)
        {
            lock (mLock)
            {
                var type = dataSet.GetType();
                if (mAttributeDataSets.ContainsKey(type))
                {
                    return false;
                }

                dataSet.Clear();
                mAttributeDataSets.Add(type, dataSet);

                return true;
            }
        }

        public IReadOnlyList<IAttributeDataSet> AttributeDataSets
        {
            get
            {
                lock (mLock)
                {
                    return new List<IAttributeDataSet>(mAttributeDataSets.Values);
                }
            }
        }

        public Process ProcessObject => mProcess;

        private readonly Process mProcess;
        private readonly int mID;

        private readonly object mLock;
        private readonly List<DateTime> mDataSetKeys;
        private readonly Dictionary<Type, IAttributeDataSet> mAttributeDataSets;
    }
}

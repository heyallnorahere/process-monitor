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
using Terminal.Gui.Graphs;

namespace ProcessMonitor.Diagnostics
{
    public sealed class ProcessMemoryDataSet : IAttributeDataSet
    {
        public ProcessMemoryDataSet()
        {
            mRecord = new Dictionary<DateTime, long>();
        }

        public void Clear() => mRecord.Clear();
        public bool Remove(DateTime time) => mRecord.Remove(time);

        public void Record(DateTime time, Process process)
        {
            long value = process.VirtualMemorySize64;
            if (mRecord.ContainsKey(time))
            {
                mRecord[time] = value;
            }
            else
            {
                mRecord.Add(time, value);
            }
        }

        public double this[DateTime time] => mRecord[time];
        public string DataName => "Memory usage";

        public void ConfigureAxis(ref VerticalAxis axis)
        {
            // nothing
        }

        private readonly Dictionary<DateTime, long> mRecord;
    }
}

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

namespace ProcessMonitor.Diagnostics
{
    public sealed class ProcessCPUDataSet : IAttributeDataSet
    {
        private struct DataPoint
        {
            public double Value { get; set; }
            public TimeSpan Usage { get; set; }
        }

        public ProcessCPUDataSet()
        {
            mRecord = new Dictionary<DateTime, DataPoint>();
            mLastRecordedTime = null;
        }

        public void Clear()
        {
            mLastRecordedTime = null;
            mRecord.Clear();
        }

        public bool Remove(DateTime time)
        {
            bool removed = mRecord.Remove(time);
            if (time == mLastRecordedTime)
            {
                mLastRecordedTime = null;
                foreach (var key in mRecord.Keys)
                {
                    if (mLastRecordedTime == null || key > mLastRecordedTime.Value)
                    {
                        mLastRecordedTime = key;
                    }
                }
            }

            return removed;
        }

        public void Record(DateTime time, Process process)
        {
            var totalProcessorTime = process.TotalProcessorTime;

            DateTime lastSampleTime;
            TimeSpan lastSampleData;

            if (mLastRecordedTime != null)
            {
                lastSampleTime = mLastRecordedTime.Value;
                lastSampleData = mRecord[lastSampleTime].Usage;
            }
            else
            {
                lastSampleTime = time;
                lastSampleData = totalProcessorTime;
            }

            var timeProcessorUsed = (totalProcessorTime - lastSampleData).TotalMilliseconds;
            var timeElapsed = (time - lastSampleTime).TotalMilliseconds;

            var value = (timeProcessorUsed / (Environment.ProcessorCount * timeElapsed)) * 100;
            
            mLastRecordedTime = time;
            mRecord.Add(time, new DataPoint
            {
                Value = value,
                Usage = totalProcessorTime
            });
        }

        public double this[DateTime time] => mRecord[time].Value;
        public string DataName => "CPU usage";

        private readonly Dictionary<DateTime, DataPoint> mRecord;
        private DateTime? mLastRecordedTime;
    }
}
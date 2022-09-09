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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessMonitor.Diagnostics.Exporters
{
    [DataExporter(DisplayName = "JSON")]
    public sealed class JsonExporter : IDataExporter
    {
        public JsonExporter()
        {
            mData = new Dictionary<string, Dictionary<string, double>>();
        }

        public void Reset() => mData.Clear();

        public bool AddDataPoint(DateTime time, string key, double value)
        {
            if (!mData.ContainsKey(key))
            {
                mData.Add(key, new Dictionary<string, double>());
            }

            var data = mData[key];
            string timeString = time.ToString("yyyy-MM-dd HH:mm:ss");

            if (data.ContainsKey(timeString))
            {
                return false;
            }

            data.Add(timeString, value);
            return true;
        }

        public string? Export() => JsonConvert.SerializeObject(mData, Formatting.Indented);
        
        public string Extension => ".json";

        private readonly Dictionary<string, Dictionary<string, double>> mData;
    }
}

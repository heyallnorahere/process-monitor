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
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using Terminal.Gui.Graphs;

namespace ProcessMonitor.Views
{
    public sealed class DataSetView : FrameView
    {
        private class GraphData
        {
            public GraphData(IAttributeDataSet dataSet)
            {
                DataSet = dataSet;
                View = new GraphView
                {
                    MarginBottom = 2,
                    MarginLeft = 3,
                    CellSize = new PointF(2f, 1000f)
                };

                View.AxisX.Increment = 30;
                View.AxisX.ShowLabelsEvery = 1;
                View.AxisX.LabelGetter = increment => $"{increment.Value}s";
                View.AxisX.Text = "Time ->";

                var yAxis = View.AxisY;
                dataSet.ConfigureAxis(ref yAxis);
                View.AxisY = yAxis;

                if (yAxis.Text == null)
                {
                    yAxis.Text = $"{dataSet.DataName}↑";
                }

                Points = new ScatterSeries
                {
                    Fill = new GraphCellToRender('x', Application.Driver.MakeAttribute(Color.BrightRed, Color.Black))
                };

                Line = new PathAnnotation
                {
                    LineColor = Application.Driver.MakeAttribute(Color.Magenta, Color.Black),
                    BeforeSeries = true
                };

                View.Series.Add(Points);
                View.Annotations.Add(Line);
            }

            public GraphView View { get; }
            public ScatterSeries Points { get; }
            public PathAnnotation Line { get; }

            public IAttributeDataSet DataSet { get; }
        }

        public DataSetView(ProcessDataSet dataSet) : base("Process metrics")
        {
            CanFocus = false;

            mDataSet = dataSet;
            mDataSet.OnDataRecorded += Update;

            mGraphs = new Dictionary<string, GraphData>();
            AddGraphs();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                mDataSet.OnDataRecorded -= Update;
            }
        }

        private void AddGraphs()
        {
            var attributeDataSets = mDataSet.AttributeDataSets.ToList();
            if (attributeDataSets.Count == 0)
            {
                throw new ArgumentException("The passed data set cannot record any data!");
            }

            // temporary
            {
                var dataSet = attributeDataSets.Find(set => set.GetType() == typeof(ProcessMemoryDataSet));
                if (dataSet == null)
                {
                    throw new ArgumentException("Temporary - will fix later");
                }

                var data = new GraphData(dataSet);
                data.View.X = data.View.Y = 0;
                data.View.Width = data.View.Height = Dim.Fill();

                Add(data.View);
                mGraphs.Add(dataSet.DataName, data);
            }

            TranslateData();
        }

        private void Update()
        {
            Application.MainLoop.Invoke(() =>
            {
                TranslateData();
                Application.Refresh();
            });
        }

        private void TranslateData()
        {
            var compiledData = mDataSet.Compile();

            var startTime = mDataSet.StartTime;
            if (startTime == null)
            {
                return;
            }

            foreach (string key in mGraphs.Keys)
            {
                var points = new List<PointF>();
                foreach (var time in compiledData.Keys)
                {
                    double y = compiledData[time][key];
                    double x = (time - startTime.Value).TotalSeconds;

                    points.Add(new PointF((float)x, (float)y));
                }

                points = points.OrderBy(point => point.X).ToList();
                var data = mGraphs[key];

                data.Points.Points = points;
                data.Line.Points = points;
            }
        }

        private readonly ProcessDataSet mDataSet;
        private readonly Dictionary<string, GraphData> mGraphs;
    }
}
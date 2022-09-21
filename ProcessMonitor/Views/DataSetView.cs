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
using System.Data;
using System.Linq;
using Terminal.Gui;
using Terminal.Gui.Graphs;

namespace ProcessMonitor.Views
{
    public sealed class DataSetView : View
    {
        private class GraphData
        {
            public GraphData(IAttributeDataSet dataSet)
            {
                DataSet = dataSet;
                Row = null;

                View = new GraphView
                {
                    MarginBottom = 2,
                    MarginLeft = 3,
                    CellSize = new PointF(2f, 1000f),
                    CanFocus = false
                };

                View.AxisX.Increment = 1;
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
            public DataRow? Row { get; set; }
        }

        private const string AttributeColumnName = "Attribute";
        private const string ValueColumnName = "Value";

        public DataSetView(ProcessDataSet dataSet)
        {
            mDataSet = dataSet;
            mDataSet.OnDataRecorded += Update;

            mLock = new object();
            mGraphs = new Dictionary<string, GraphData>();
            mStats = new DataTable
            {
                TableName = "Process stats"
            };

            mStats.Columns.Add(new DataColumn
            {
                DataType = typeof(string),
                ColumnName = AttributeColumnName,
                AutoIncrement = false,
                ReadOnly = true,
                Unique = true
            });

            mStats.Columns.Add(new DataColumn
            {
                DataType = typeof(double),
                ColumnName = ValueColumnName,
                AutoIncrement = false,
                ReadOnly = false,
                Unique = false
            });

            AddContent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                mDataSet.OnDataRecorded -= Update;
            }
        }

        private void AddContent()
        {
            const int tableWidth = 30;
            var graphView = new FrameView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill() - tableWidth,
                Height = Dim.Fill(),
                CanFocus = false,
                Title = "Process metrics"
            };

            AddGraphs(graphView);
            Add(graphView);

            Add(new TableView
            {
                Table = mStats,
                X = Pos.Right(this) - tableWidth,
                Y = 0,
                Width = tableWidth,
                Height = Dim.Fill(),
                CanFocus = false
            });

            TranslateData();
        }

        private void AddGraphs(View graphView)
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

                graphView.Add(data.View);
                mGraphs.Add(dataSet.DataName, data);
            }
        }

        private void Update(DateTime now)
        {
            lock (mLock)
            {
                mLatestRecorded = now;
            }

            Application.MainLoop.Invoke(() =>
            {
                TranslateData();
                Application.Refresh();
            });
        }

        private void TranslateData()
        {
            TranslateGraphData();
            TranslateTableData();
        }

        private void TranslateGraphData()
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

        private void TranslateTableData()
        {
            DateTime? latestRecorded;
            lock (mLock)
            {
                latestRecorded = mLatestRecorded;
            }

            foreach (var dataName in mGraphs.Keys)
            {
                var data = mGraphs[dataName];
                if (data.Row == null)
                {
                    data.Row = mStats.NewRow();

                    data.Row[AttributeColumnName] = dataName;
                    data.Row[ValueColumnName] = 0;

                    mStats.Rows.Add(data.Row);
                }

                if (latestRecorded.HasValue)
                {
                    var value = data.DataSet[latestRecorded.Value];

                    data.Row[ValueColumnName] = value;
                    data.Row.AcceptChanges();
                }
            }
        }

        private readonly ProcessDataSet mDataSet;
        private readonly Dictionary<string, GraphData> mGraphs;
        
        private readonly DataTable mStats;

        private DateTime? mLatestRecorded;
        private readonly object mLock;
    }
}
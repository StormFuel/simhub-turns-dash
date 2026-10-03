using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TurnTelemetry.Core.Turns;

namespace TurnTelemetryHost
{
    /// <summary>
    /// Review-and-number list for turn candidates: AC sections, detected corners, or the turns already loaded.
    /// Pick the first turn, auto-number, adjust, save. Nothing is used until saved.
    /// </summary>
    internal sealed class TurnEditor : StackPanel
    {
        private readonly TurnTelemetry _plugin;
        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();
        private readonly DataGrid _grid;
        private readonly TextBlock _status;

        public TurnEditor(TurnTelemetry plugin)
        {
            _plugin = plugin;

            Children.Add(new TextBlock
            {
                Text = "Load candidates, untick anything that isn't a turn, select the first turn and auto-number. "
                       + "Edit any number (e.g. 7A) or name, then save. Saved turns drive NEXT/CURRENT and the lap strip.",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6),
            });

            var load = new WrapPanel();
            load.Children.Add(Button("Load from track map", () => Show(_plugin.Engine.MapCandidates(out var m), m)));
            load.Children.Add(Button("Load AC sections", () => Show(_plugin.Engine.AcSectionCandidates(out var m), m)));
            load.Children.Add(Button("Detect corners from driving", () => Show(_plugin.Engine.DetectCorners(out var m), m)));
            load.Children.Add(Button("Load current turns", () =>
            {
                var list = _plugin.Engine.CurrentTurnCandidates();
                Show(list, $"{list.Count} turns loaded");
            }));
            Children.Add(load);

            _grid = new DataGrid
            {
                ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = true,
                SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column,
                MaxHeight = 360, Margin = new Thickness(0, 4, 0, 4),
            };
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Turn?", Binding = TwoWay(nameof(Row.Include)) });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Number", Binding = TwoWay(nameof(Row.Label)), Width = 80 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = TwoWay(nameof(Row.Name)), Width = 220 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Start", Binding = new Binding(nameof(Row.StartText)), IsReadOnly = true });
            _grid.Columns.Add(new DataGridTextColumn { Header = "End", Binding = new Binding(nameof(Row.EndText)), IsReadOnly = true });
            Children.Add(_grid);

            var act = new WrapPanel();
            act.Children.Add(Button("Auto-number from selected", AutoNumber));
            act.Children.Add(Button("Save turns", Save));
            act.Children.Add(Button("Clear list", () => Show(new List<TurnCandidate>(), "")));
            Children.Add(act);

            _status = new TextBlock { Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
            Children.Add(_status);
        }

        private void Show(List<TurnCandidate> candidates, string message)
        {
            _rows.Clear();
            foreach (var c in candidates.OrderBy(c => c.Start)) _rows.Add(new Row(c));
            _status.Text = message;
        }

        private void AutoNumber()
        {
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            if (_rows.Count == 0)
            {
                _status.Text = "load candidates first";
                return;
            }
            var first = Math.Max(0, _grid.SelectedIndex);
            var candidates = _rows.Select(r => r.Candidate).ToList();
            TurnNumbering.AutoNumber(candidates, first);
            foreach (var row in _rows) row.Refresh();
            _status.Text = $"numbered from \"{_rows[first].Name}\" as turn 1";
        }

        private void Save()
        {
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            try
            {
                _status.Text = _plugin.Engine.SaveTurns(_rows.Select(r => r.Candidate));
            }
            catch (Exception ex)
            {
                _status.Text = "save failed: " + ex.Message;
                SimHub.Logging.Current.Error("Turn Telemetry: saving turns failed", ex);
            }
        }

        private static Binding TwoWay(string path) =>
            new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

        private static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 4, 10, 4) };
            button.Click += (s, a) => action();
            return button;
        }

        private sealed class Row : INotifyPropertyChanged
        {
            public Row(TurnCandidate candidate) => Candidate = candidate;

            public TurnCandidate Candidate { get; }

            public bool Include
            {
                get => Candidate.Include;
                set { Candidate.Include = value; Changed(nameof(Include)); }
            }

            public string Label
            {
                get => Candidate.Label;
                set { Candidate.Label = value ?? ""; Changed(nameof(Label)); }
            }

            public string Name
            {
                get => Candidate.Name;
                set { Candidate.Name = value ?? ""; Changed(nameof(Name)); }
            }

            public string StartText => Candidate.Start.ToString("P1");
            public string EndText => Candidate.End.ToString("P1");

            public void Refresh()
            {
                Changed(nameof(Include));
                Changed(nameof(Label));
                Changed(nameof(Name));
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

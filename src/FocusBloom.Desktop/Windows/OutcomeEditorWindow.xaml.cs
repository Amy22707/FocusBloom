using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using FocusBloom.Core.Focus;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class OutcomeEditorWindow : Window
{
    private readonly MainViewModel _main;
    private readonly Guid _id;
    public OutcomeEditorWindow(MainViewModel main, Guid id)
    {
        InitializeComponent(); WindowAppearance.Attach(this); _main = main; _id = id;
        var record = main.GetRecord(id)!;
        var row = main.Records.Single(row => row.Id == id);
        RecordHeading.Text = $"{row.Project}" + (row.Todo.Length > 0 ? $" · {row.Todo}" : "") + $"\n{row.Started} · 有效专注 {row.Duration}";
        TimingDetails.Text = $"{row.Started} — {row.Ended}\n{row.Mode} · {row.RestrictionMode}模式 · {row.Reason}" + (record.Strict ? $"\n拦截次数：{row.Interceptions}" : "");
        var outcome = main.GetOutcome(id) ?? new FocusOutcome(id, NextStep: record.TodoId is { } todoId ? main.Todos.Items.FirstOrDefault(item => item.Id == todoId)?.Item.ResumeNote ?? "" : "");
        SummaryInput.Text = outcome.Summary; QuantityInput.Text = outcome.Quantity?.ToString(CultureInfo.CurrentCulture) ?? "";
        UnitInput.Text = outcome.Unit; NextInput.Text = outcome.NextStep;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        double? quantity = null;
        if (!string.IsNullOrWhiteSpace(QuantityInput.Text))
        {
            if (!double.TryParse(QuantityInput.Text, out var value) || !double.IsFinite(value) || value < 0) { ValidationMessage.Text = "数量须为非负数，也可以留空。"; return; }
            quantity = value;
        }
        if (_main.SaveOutcome(new(_id, SummaryInput.Text, quantity, UnitInput.Text, NextInput.Text))) DialogResult = true;
        else ValidationMessage.Text = _main.StatusMessage;
    }
}

using System.Windows;
using ClothingStore.Core.Localization;
using ClothingStore.Desktop.ViewModels.Dialogs;
using ClothingStore.Desktop.Views;
using Microsoft.Win32;

namespace ClothingStore.Desktop.Infrastructure;

public interface IDialogService
{
    void Info(string message, string? title = null);
    void Warning(string message, string? title = null);
    void Error(string message, Exception? ex = null);
    bool Confirm(string message, string? title = null);

    /// <summary>A question with two named answers; true for <paramref name="yesText"/>, false for the other (or closing it).</summary>
    bool Choose(string message, string title, string yesText, string noText);

    /// <summary>A short notice that disappears by itself (for confirmations that need no answer).</summary>
    void Toast(string message, ToastKind kind = ToastKind.Success);

    bool ShowDialog(IDialogViewModel viewModel);
    string? Prompt(string title, string message, string? initialValue = null);
    decimal? PromptDecimal(string title, string message, decimal? initialValue = null);
    int? PromptInt(string title, string message, int? initialValue = null);
    string? SaveFile(string title, string filter, string defaultFileName);
    string? OpenFile(string title, string filter);
}

public sealed class DialogService : IDialogService
{
    // Never the customer display or the screen numbers: a dialog would open on the customer's monitor (or close with them).
    private static Window? Owner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && !CustomerDisplay.IsDisplayWindow(w))
        ?? (Application.Current.MainWindow is { IsVisible: true } main && !CustomerDisplay.IsDisplayWindow(main) ? main : null);

    public void Info(string message, string? title = null) =>
        ShowDialog(new MessageDialogViewModel(this, MessageKind.Info, title ?? Loc.T("Common.Information"), message));

    public void Warning(string message, string? title = null) =>
        ShowDialog(new MessageDialogViewModel(this, MessageKind.Warning, title ?? Loc.T("Common.PleaseCheck"), message));

    public void Error(string message, Exception? ex = null) =>
        ShowDialog(new MessageDialogViewModel(this, MessageKind.Error, Loc.T("Common.Error"), message, ex?.GetBaseException().Message));

    public bool Confirm(string message, string? title = null) =>
        ShowDialog(new MessageDialogViewModel(this, MessageKind.Question, title ?? Loc.T("Common.PleaseConfirm"), message));

    public bool Choose(string message, string title, string yesText, string noText) =>
        ShowDialog(new MessageDialogViewModel(this, MessageKind.Question, title, message) { YesText = yesText, NoText = noText });

    public void Toast(string message, ToastKind kind = ToastKind.Success)
    {
        // Toasts live in the main window; before it exists (sign-in), fall back to a dialog.
        if (Application.Current.MainWindow is { IsVisible: true } and Views.MainWindow) ToastHost.Instance.Show(message, kind);
        else Info(message);
    }

    public bool ShowDialog(IDialogViewModel viewModel)
    {
        var window = new DialogWindow(viewModel);
        var owner = Owner;
        if (owner is not null && owner != window) window.Owner = owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window.ShowDialog() == true;
    }

    public string? Prompt(string title, string message, string? initialValue = null)
    {
        var vm = new PromptViewModel(this, title, message, initialValue ?? "", PromptKind.Text);
        return ShowDialog(vm) ? vm.Value : null;
    }

    public decimal? PromptDecimal(string title, string message, decimal? initialValue = null)
    {
        var vm = new PromptViewModel(this, title, message, initialValue?.ToString("0.##") ?? "", PromptKind.Decimal);
        return ShowDialog(vm) ? vm.DecimalValue : null;
    }

    public int? PromptInt(string title, string message, int? initialValue = null)
    {
        var vm = new PromptViewModel(this, title, message, initialValue?.ToString() ?? "", PromptKind.Integer);
        return ShowDialog(vm) ? (int?)vm.DecimalValue : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName, AddExtension = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}

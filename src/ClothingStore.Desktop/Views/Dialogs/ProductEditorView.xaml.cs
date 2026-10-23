using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClothingStore.Desktop.ViewModels.Dialogs;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class ProductEditorView : UserControl
{
    public ProductEditorView()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ProductEditorViewModel old) old.FocusNameRequested -= OnFocusNameRequested;
            if (e.NewValue is ProductEditorViewModel vm) vm.FocusNameRequested += OnFocusNameRequested;
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnFocusNameRequested(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });

    /// <summary>Ctrl+Enter: save and go on with the next product (a cell still being typed in is kept first).</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control || DataContext is not ProductEditorViewModel vm) return;
        e.Handled = true;
        if (Keyboard.FocusedElement is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        VariantsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (vm.SaveAndNewCommand.CanExecute(null)) vm.SaveAndNewCommand.Execute(null);
    }
}

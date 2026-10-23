using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class CustomerEditorView : UserControl
{
    public CustomerEditorView()
    {
        InitializeComponent();
        Loaded += (_, _) => FirstBox.Focus();
    }
}

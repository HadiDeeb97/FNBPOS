using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class PromptView : UserControl
{
    public PromptView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }
}

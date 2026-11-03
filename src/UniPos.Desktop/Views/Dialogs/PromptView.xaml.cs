using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

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

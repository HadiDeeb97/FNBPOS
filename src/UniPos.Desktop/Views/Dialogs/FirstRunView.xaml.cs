using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

public partial class FirstRunView : UserControl
{
    public FirstRunView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }
}

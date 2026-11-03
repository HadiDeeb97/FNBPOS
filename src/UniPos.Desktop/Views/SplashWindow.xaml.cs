using System.Windows;
using UniPos.Desktop.Infrastructure;

namespace UniPos.Desktop.Views;

/// <summary>Shown straight away while the app connects to the database, so starting never looks frozen.</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        WindowAppearance.Apply(this);
    }

    /// <summary>What the app is doing right now ("Backing up the database...").</summary>
    public void SetStatus(string text) => StatusText.Text = text;
}

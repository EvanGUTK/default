using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace BlueyDesktop.App;

/// <summary>
/// Bluey's home: a small borderless, transparent window that stays on top of everything.
/// There is no menu bar to dock him in on Windows, so he lives near a corner of the screen and can be dragged.
/// </summary>
public partial class MainWindow : Window
{
    private const double ScreenMargin = 24;  // device-independent px from the work area's corner

    public MainWindow()
    {
        InitializeComponent();

        // Start in the bottom-right corner of the usable screen (above the taskbar).
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - ScreenMargin;
        Top = area.Bottom - Height - ScreenMargin;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            DragMove();
        }
        catch (InvalidOperationException ex)
        {
            // WPF throws this when the button was already released by the time the drag started.
            Debug.WriteLine($"Bluey drag skipped: {ex.Message}");
        }
    }

    private void OnQuit(object sender, RoutedEventArgs e) => Close();
}

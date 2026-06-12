using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace Fluid.App;

public partial class WindowPickerDialog : Window
{
    public string? SelectedTitle { get; private set; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    private const int DWMWA_CLOAKED = 14;

    public WindowPickerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PopulateWindowList();
    }

    private void PopulateWindowList()
    {
        var titles = new List<string>();
        var myHwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        var sb = new StringBuilder(256);

        EnumWindows((hwnd, _) =>
        {
            if (hwnd == myHwnd) return true;
            if (!IsWindowVisible(hwnd)) return true;
            if (IsIconic(hwnd)) return true;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            sb.Clear();
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(title) && title != "Program Manager")
                titles.Add(title);
            return true;
        }, IntPtr.Zero);

        titles.Sort(StringComparer.OrdinalIgnoreCase);
        WindowList.ItemsSource = titles;
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        AddBtn.IsEnabled = WindowList.SelectedItem != null;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        SelectedTitle = WindowList.SelectedItem as string;
        DialogResult = true;
        Close();
    }
}

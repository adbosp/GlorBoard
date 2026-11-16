using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace GlorBoard;

public partial class MainWindow
{
    private void AppearanceClick(object sender, RoutedEventArgs e)
    {
        var title = T("Giao diện", "Appearance");
        StackPanel body = new();
        TextBlock label = Paragraph(T("Chủ đề giao diện", "Appearance theme"));
        UniformGrid choices = new() { Columns = 3, Margin = new Thickness(-3, 0, -3, 0) };
        AutomationProperties.SetLabeledBy(choices, label);
        Style segment = (Style)FindResource("ThemeChoice");
        var options = new[] { T("Tối", "Dark"), T("Sáng", "Light"), T("Theo Windows", "Follow Windows") };
        for (int index = 0; index < options.Length; index++)
        {
            var choice = new RadioButton
            {
                Style = segment, Content = options[index], GroupName = "GlorBoardAppearance",
                IsChecked = (int)app.Settings.Theme == index,
            };
            choices.Children.Add(choice);
        }
        body.Children.Add(label);
        body.Children.Add(choices);
        ShowDialog(title, body, T("Áp dụng", "Apply"), () =>
        {
            for (int index = 0; index < choices.Children.Count; index++)
                if (choices.Children[index] is RadioButton { IsChecked: true }) app.ChangeTheme((UiTheme)index);
            return true;
        });
    }

    private void MethodOptionsClick(object sender, RoutedEventArgs e)
    {
        StackPanel content = new();
        if (app.Settings.Method == InputMethod.Telex)
        {
            Option(content, T("Gõ w ở đầu từ để nhập ư", "Convert initial w to ư"),
                app.Settings.StandaloneW, value => app.Settings.StandaloneW = value);
            Option(content, T("Gõ [ và ] để nhập ư và ơ", "Use [ and ] for ư and ơ"),
                app.Settings.BracketKeys, value => app.Settings.BracketKeys = value);
        }
        else
        {
            content.Children.Add(Paragraph(T("1–5 đặt dấu thanh · 6 thêm mũ · 7 thêm móc\n8 thêm dấu trăng · 9 tạo đ · 0 xóa dấu thanh",
                "1–5 set the tone · 6 adds circumflex · 7 adds horn\n8 adds breve · 9 makes đ · 0 removes the tone")));
        }
        ShowDialog(T("Tùy chọn kiểu gõ", "Input method options"), content, T("Đóng", "Close"));
    }

    private void GuideClick(object sender, RoutedEventArgs e)
    {
        StackPanel content = new();
        content.Children.Add(Paragraph(T("Chọn Telex hoặc VNI rồi gõ trong ứng dụng đang mở. Dùng Ctrl + Shift hoặc Alt + Z để chuyển giữa EN và VI.",
            "Choose Telex or VNI, then type in another app. Use Ctrl + Shift or Alt + Z to switch between EN and VI.")));
        content.Children.Add(Paragraph("Telex    tieengs Vieetj  →  tiếng Việt\nVNI       tie6ng1 Vie6t5  →  tiếng Việt"));
        content.Children.Add(Paragraph(T("Gõ tắt bung nội dung khi nhập dấu cách, dấu câu, Enter hoặc Tab. Nhấp đúp biểu tượng ở khay để mở lại GlorBoard.",
            "Text shortcuts expand on Space, punctuation, Enter, or Tab. Double-click the tray icon to reopen GlorBoard.")));
        content.Children.Add(Paragraph(T("Nếu đang dùng bộ gõ tiếng Việt khác, hãy tắt chế độ tiếng Việt ở bộ gõ đó trước khi bật GlorBoard.",
            "Turn off Vietnamese input in other keyboard tools before enabling GlorBoard.")));
        content.Children.Add(Paragraph(T("Để gõ trong ứng dụng chạy với quyền quản trị, mở GlorBoard với cùng mức quyền.",
            "For typing into elevated apps, run GlorBoard at the same privilege level.")));
        Button manual = new() { Content = T("Xem bảng phím Telex và VNI", "See the Telex and VNI key tables"),
            HorizontalAlignment = HorizontalAlignment.Left };
        manual.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://www.unikey.org/support/ukmanual.html") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception error) { MessageBox.Show(error.Message, "GlorBoard"); }
        };
        content.Children.Add(manual);
        ShowDialog(T("Hướng dẫn", "Help"), content, T("Đóng", "Close"));
    }

    private void InfoClick(object sender, RoutedEventArgs e)
    {
        StackPanel content = new();
        content.Children.Add(Paragraph($"GlorBoard {typeof(App).Assembly.GetName().Version?.ToString(3)}"));
        content.Children.Add(Paragraph(T("Bộ gõ tiếng Việt Telex và VNI dành cho Windows.", "A Vietnamese Telex and VNI input tool for Windows.")));
        ShowDialog(T("Thông tin", "About"), content, T("Đóng", "Close"));
    }

    private void DefaultsClick(object sender, RoutedEventArgs e)
    {
        StackPanel content = new();
        content.Children.Add(Paragraph(T("Khôi phục các tùy chọn mặc định và xóa bảng gõ tắt cùng trạng thái đã lưu theo ứng dụng?",
            "Restore default options and clear saved text shortcuts and per-app modes?")));
        ShowDialog(T("Khôi phục mặc định", "Restore defaults"), content, T("Khôi phục", "Restore"), app.ResetSettings);
    }

    private void MacroClick(object sender, RoutedEventArgs e)
    {
        ObservableCollection<MacroRow> rows = new(
            app.Settings.Macros.OrderBy(entry => entry.Key).Select(entry => new MacroRow { Key = entry.Key, Text = entry.Value }));
        if (rows.Count == 0) rows.Add(new());
        DataGrid grid = new()
        {
            ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            CanUserReorderColumns = false, SelectionMode = DataGridSelectionMode.Single, Height = 205,
            RowHeaderWidth = 0, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Style = (Style)FindResource("GlassGrid"),
        };
        AutomationProperties.SetName(grid, T("Bảng gõ tắt", "Text shortcuts"));
        var displayStyle = (Style)FindResource("GlassCellText");
        var editorStyle = (Style)FindResource("GlassEditor");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Phím tắt", "Shortcut"), Binding = new Binding(nameof(MacroRow.Key)), Width = 106,
            ElementStyle = displayStyle, EditingElementStyle = editorStyle,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = T("Nội dung thay thế", "Expanded text"), Binding = new Binding(nameof(MacroRow.Text)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = displayStyle, EditingElementStyle = editorStyle,
        });
        grid.SizeChanged += (_, _) => grid.Clip = new RectangleGeometry(new Rect(grid.RenderSize), 11, 11);
        StackPanel panel = new();
        panel.Children.Add(Paragraph(T("Dùng chữ cái A–Z hoặc số cho phím tắt. Ví dụ  dc → địa chỉ.", "Use A–Z letters or digits in each shortcut. Example  addr → address.")));
        panel.Children.Add(grid);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        Button add = new() { Content = T("Thêm dòng", "Add row"), Margin = new Thickness(0, 0, 7, 0) };
        Button remove = new() { Content = T("Xóa dòng", "Delete row") };
        add.Click += (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            MacroRow row = new();
            rows.Add(row);
            grid.UpdateLayout();
            grid.ScrollIntoView(row);
            grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]);
            grid.SelectedItem = row;
            grid.Focus();
            grid.BeginEdit();
        };
        remove.Click += (_, _) => { if (grid.SelectedItem is MacroRow row) rows.Remove(row); };
        actions.Children.Add(add);
        actions.Children.Add(remove);
        panel.Children.Add(actions);
        TextBlock error = Paragraph("");
        error.SetResourceReference(TextBlock.ForegroundProperty, "Attention");
        error.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(error);
        ShowDialog(T("Bảng gõ tắt", "Text shortcuts"), panel, T("Lưu", "Save"), () =>
        {
            if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return false;
            Dictionary<string, string> updated = new(StringComparer.OrdinalIgnoreCase);
            foreach (MacroRow row in rows)
            {
                string key = row.Key.Trim();
                if (key.Length == 0 && row.Text.Length == 0) continue;
                if (key.Length is 0 or > 96 || !key.All(char.IsAsciiLetterOrDigit) || row.Text.Length is 0 or > 2048 ||
                    !updated.TryAdd(key, row.Text))
                {
                    error.Text = T("Mỗi phím tắt phải khác nhau, dài 1–96 ký tự. Nội dung dài 1–2048 ký tự.",
                        "Use unique shortcuts of 1–96 characters and replacement text of 1–2048 characters.");
                    return false;
                }
            }
            app.Settings.Macros = updated;
            app.OptionsChanged();
            return true;
        });
    }

    public sealed class MacroRow
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
    }

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22, Margin = new Thickness(0, 0, 0, 11),
    };

    private void ShowDialog(string title, FrameworkElement contents, string action, Func<bool>? save = null)
    {
        Window dialog = new()
        {
            Owner = this, Title = title, Width = Math.Min(506, ActualWidth), SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            MaxHeight = Math.Min(660, SystemParameters.WorkArea.Height - 20),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = FontFamily, FontSize = 14, UseLayoutRounding = true,
        };
        dialog.SetResourceReference(Window.ForegroundProperty, "Text");
        DockPanel body = new() { Margin = new Thickness(19) };
        TextBlock heading = new() { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        heading.MouseLeftButtonDown += (_, args) =>
        {
            if (args.LeftButton == System.Windows.Input.MouseButtonState.Pressed) dialog.DragMove();
        };
        DockPanel.SetDock(heading, Dock.Top);
        body.Children.Add(heading);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        if (save != null)
            buttons.Children.Add(new Button { Content = T("Hủy", "Cancel"), IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) });
        Button done = new() { Content = action, Style = (Style)FindResource("Primary"), MinWidth = 85, IsDefault = true, IsCancel = save == null };
        done.Click += (_, _) => { if (save == null || save()) dialog.DialogResult = true; };
        buttons.Children.Add(done);
        DockPanel.SetDock(buttons, Dock.Bottom);
        body.Children.Add(buttons);
        body.Children.Add(new ScrollViewer
        {
            Content = contents, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        Border sheet = new()
        {
            Style = (Style)FindResource("GlassCard"), Margin = new Thickness(8), Child = body,
        };
        dialog.Content = new ContentControl { Style = (Style)FindResource("GlassWindowSurface"), Content = sheet };
        WindowGlass.Attach(dialog);
        dialog.ShowDialog();
    }
}

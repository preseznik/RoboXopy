using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RoboXopy.Controls;

/// <summary>Edits a list of wildcard patterns or paths as removable chips plus an input box.</summary>
public partial class PatternListEditor : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<string>), typeof(PatternListEditor));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PatternListEditor), new PropertyMetadata("Add a pattern"));

    public static readonly DependencyProperty AllowBrowseFoldersProperty =
        DependencyProperty.Register(nameof(AllowBrowseFolders), typeof(bool), typeof(PatternListEditor));

    public static readonly DependencyProperty BrowseRootProperty =
        DependencyProperty.Register(nameof(BrowseRoot), typeof(string), typeof(PatternListEditor));

    public PatternListEditor()
    {
        InitializeComponent();
    }

    public ObservableCollection<string>? Items
    {
        get => (ObservableCollection<string>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool AllowBrowseFolders
    {
        get => (bool)GetValue(AllowBrowseFoldersProperty);
        set => SetValue(AllowBrowseFoldersProperty, value);
    }

    public string? BrowseRoot
    {
        get => (string?)GetValue(BrowseRootProperty);
        set => SetValue(BrowseRootProperty, value);
    }

    /// <summary>Splits user input such as "*.tmp; *.bak" into individual patterns.</summary>
    public static IEnumerable<string> SplitPatterns(string text) =>
        text.Split([';', '\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim('"').Trim())
            .Where(p => p.Length > 0);

    private void AddFromInput()
    {
        if (Items is null) return;
        foreach (var pattern in SplitPatterns(Input.Text))
        {
            Add(pattern);
        }

        Input.Clear();
        Input.Focus();
    }

    private void Add(string pattern)
    {
        if (Items is null) return;
        if (!Items.Contains(pattern, StringComparer.OrdinalIgnoreCase)) Items.Add(pattern);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddFromInput();
            e.Handled = true;
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddFromInput();

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string pattern }) Items?.Remove(pattern);
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose folders to exclude", Multiselect = true };
        if (!string.IsNullOrWhiteSpace(BrowseRoot) && Directory.Exists(BrowseRoot)) dialog.InitialDirectory = BrowseRoot;
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var folder in dialog.FolderNames) Add(folder);
    }
}

using System.Windows;

namespace RoboXopy.Controls;

/// <summary>Attached properties used by the app's control templates.</summary>
public static class Ui
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.RegisterAttached("IsSelected", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.RegisterAttached("Description", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static bool GetIsSelected(DependencyObject d) => (bool)d.GetValue(IsSelectedProperty);
    public static void SetIsSelected(DependencyObject d, bool value) => d.SetValue(IsSelectedProperty, value);

    public static string? GetIcon(DependencyObject d) => (string?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string? value) => d.SetValue(IconProperty, value);

    public static string? GetDescription(DependencyObject d) => (string?)d.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject d, string? value) => d.SetValue(DescriptionProperty, value);
}

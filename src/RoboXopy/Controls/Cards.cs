using System.Windows;
using System.Windows.Controls;

namespace RoboXopy.Controls;

/// <summary>A Windows 11 settings-style card: icon, header, description and a control on the right (or below).</summary>
public class SettingsCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingsCard));

    public static readonly DependencyProperty IsContentBelowProperty =
        DependencyProperty.Register(nameof(IsContentBelow), typeof(bool), typeof(SettingsCard));

    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsContentBelow
    {
        get => (bool)GetValue(IsContentBelowProperty);
        set => SetValue(IsContentBelowProperty, value);
    }
}

public enum InfoBarSeverity
{
    Informational,
    Success,
    Warning,
    Error,
}

/// <summary>An inline status message in the style of the WinUI InfoBar. Content is shown as an action area.</summary>
public class InfoBar : ContentControl
{
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar), new PropertyMetadata(InfoBarSeverity.Informational));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(InfoBar));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(InfoBar));

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}

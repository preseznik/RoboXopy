using System.Windows;
using RoboXopy.Core.Models;

namespace RoboXopy.Services;

public static class ThemeService
{
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is null) return;
        Application.Current.ThemeMode = theme switch
        {
            AppTheme.Light => ThemeMode.Light,
            AppTheme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }
}

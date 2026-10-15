using System.Windows;
using RoboXopy.ViewModels;

namespace RoboXopy.Views;

public partial class HistoryRunDetailWindow : Window
{
    private readonly HistoryRunDetailViewModel _viewModel;

    public HistoryRunDetailWindow(HistoryRunDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.LoadAsync();
    }
}

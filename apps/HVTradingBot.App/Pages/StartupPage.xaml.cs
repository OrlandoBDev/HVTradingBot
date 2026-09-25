using System.ComponentModel;
using HVTradingBot.App.ViewModels;

namespace HVTradingBot.App.Pages;

public partial class StartupPage : ContentPage
{
    private readonly StartupViewModel viewModel;

    public StartupPage(StartupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.PropertyChanged += OnViewModelChanged;
        // First show, and back from Settings after a failure: (re)run the startup sequence. StartAsync handles errors.
        await viewModel.StartAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;
        base.OnDisappearing();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartupViewModel.LogText) && viewModel.IsLogExpanded)
        {
            // Follow the output like a terminal.
            Dispatcher.Dispatch(() => _ = LogScroll.ScrollToAsync(LogLabel, ScrollToPosition.End, animated: false));
        }
    }
}

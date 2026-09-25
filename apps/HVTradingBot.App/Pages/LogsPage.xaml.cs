using HVTradingBot.App.ViewModels;

namespace HVTradingBot.App.Pages;

public partial class LogsPage : ContentPage
{
    private readonly LogsViewModel viewModel;

    public LogsPage(LogsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.RefreshAsync(); // handles its own errors
    }
}

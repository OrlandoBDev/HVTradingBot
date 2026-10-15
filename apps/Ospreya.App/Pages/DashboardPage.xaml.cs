using Ospreya.App.ViewModels;

namespace Ospreya.App.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        Dashboard.Source = new UrlWebViewSource { Url = viewModel.DashboardUrl };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        viewModel.ReloadRequested += OnReloadRequested;
        await viewModel.ExplainQuitOnceAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.ReloadRequested -= OnReloadRequested;
        base.OnDisappearing();
    }

    private void OnReloadRequested(object? sender, EventArgs e) => Dashboard.Reload();
}

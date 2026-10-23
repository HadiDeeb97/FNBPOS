using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace ClothingStore.Desktop.Infrastructure;

public interface INavigationService
{
    IPageViewModel? CurrentPage { get; }
    Task NavigateToAsync<TPage>(Func<TPage, Task>? initialize = null) where TPage : IPageViewModel;
    event EventHandler? Navigated;
}

public sealed partial class NavigationService(IServiceProvider services) : ObservableObject, INavigationService
{
    [ObservableProperty]
    public partial IPageViewModel? CurrentPage { get; private set; }

    public event EventHandler? Navigated;

    /// <summary>
    /// True while the page being opened refreshes its data and that takes noticeably long. A kept page still shows
    /// what it had last time; the window covers it meanwhile so nobody reads or acts on old figures.
    /// </summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    private int _visit;
    private int _loadingVisit;

    /// <summary>
    /// Pages opened in this sign-in are kept, so going back to one shows it straight away (its data is refreshed by
    /// <see cref="IPageViewModel.OnNavigatedToAsync"/>) instead of building it again.
    /// </summary>
    private readonly Dictionary<Type, IPageViewModel> _pages = [];

    public IPageViewModel GetPage(Type pageType)
    {
        if (!_pages.TryGetValue(pageType, out var page))
            _pages[pageType] = page = (IPageViewModel)services.GetRequiredService(pageType);
        return page;
    }

    public async Task NavigateToAsync<TPage>(Func<TPage, Task>? initialize = null) where TPage : IPageViewModel
    {
        var page = (TPage)GetPage(typeof(TPage));
        var visit = ++_visit;
        _loadingVisit = visit;
        CurrentPage = page;
        Navigated?.Invoke(this, EventArgs.Empty);
        _ = ShowLoadingIfSlowAsync(visit);
        try
        {
            await page.OnNavigatedToAsync();
        }
        finally
        {
            if (_loadingVisit == visit)
            {
                _loadingVisit = 0;
                IsLoading = false;
            }
        }
        if (initialize is not null) await initialize(page);
    }

    /// <summary>A quick refresh shows nothing; only one still running after a moment covers the page.</summary>
    private async Task ShowLoadingIfSlowAsync(int visit)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        if (_loadingVisit == visit) IsLoading = true;
    }

    /// <summary>Signing out or rebuilding the window: forget the pages (another user, another language).</summary>
    public void Reset()
    {
        CurrentPage = null;
        _loadingVisit = 0;
        IsLoading = false;
        _pages.Clear();
    }
}

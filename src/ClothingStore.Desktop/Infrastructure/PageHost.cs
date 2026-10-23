using System.Windows;
using System.Windows.Controls;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Shows the current page and keeps the screens already built, so switching back to a page doesn't build it again
/// (building a screen with tables is the slow part of opening a page). Screens can also be built ahead of time while
/// the app is idle (<see cref="Prepare"/>), so even the first visit opens quickly.
/// </summary>
public sealed class PageHost : ContentControl
{
    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(object), typeof(PageHost), new PropertyMetadata(null, (d, e) => ((PageHost)d).Show(e.NewValue)));

    private readonly Dictionary<object, FrameworkElement> _views = new(ReferenceEqualityComparer.Instance);

    /// <summary>The page's view model; its screen comes from the DataTemplate for its type.</summary>
    public object? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    /// <summary>
    /// Builds the screen for <paramref name="page"/> now (without showing it). It is connected to its page only when
    /// shown: connecting it would let its bindings start the page's searches for a page nobody opened.
    /// </summary>
    public void Prepare(object page) => ViewFor(page);

    private void Show(object? page)
    {
        if (page is null)
        {
            Content = null;
            return;
        }
        var view = ViewFor(page);
        if (view is not null && !ReferenceEquals(view.DataContext, page)) view.DataContext = page;
        Content = (object?)view ?? page;
    }

    private FrameworkElement? ViewFor(object page)
    {
        if (_views.TryGetValue(page, out var view)) return view;
        if (TryFindResource(new DataTemplateKey(page.GetType())) is not DataTemplate template || template.LoadContent() is not FrameworkElement created)
            return null;
        // Not inheriting the window's DataContext while waiting to be shown.
        created.DataContext = null;
        _views[page] = created;
        return created;
    }
}

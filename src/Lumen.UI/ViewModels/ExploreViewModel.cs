using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lumen.Application.Services;
using Lumen.Core.Enums;
using Lumen.Core.Interfaces;
using Lumen.Core.Models;
using Lumen.Core.Models.Catalog;
using Microsoft.Extensions.Logging;

namespace Lumen.UI.ViewModels;

/// <summary>
/// ViewModel managing the Explore tab, category filters, interactive search,
/// featured items carousel, pagination, and subscription workflow.
/// </summary>
public sealed class ExploreViewModel : INotifyPropertyChanged
{
    private readonly IExploreCatalogService _catalogService;
    private readonly ILibraryService _libraryService;
    private readonly IWallpaperEngineOrchestrator _orchestrator;
    private readonly IStorageManager _storageManager;
    private readonly ILogger<ExploreViewModel> _logger;

    private readonly ObservableCollection<CatalogCategory> _categories = [];
    private readonly ObservableCollection<CatalogItemViewModel> _featuredItems = [];
    private readonly ObservableCollection<CatalogItemViewModel> _searchResults = [];
    private readonly ObservableCollection<string> _popularTags = [];

    private CatalogCategory? _selectedCategory;
    private string _searchText = string.Empty;
    private WallpaperType? _selectedTypeFilter;
    private CatalogResolutionFilter _selectedResolutionFilter = CatalogResolutionFilter.All;
    private CatalogSortOption _selectedSortOption = CatalogSortOption.Relevance;

    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalCount = 0;
    private bool _isLoading;
    private string? _errorMessage;

    private CancellationTokenSource? _debounceCts;
    private readonly object _debounceLock = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<CatalogCategory> Categories { get; }
    public ReadOnlyObservableCollection<CatalogItemViewModel> FeaturedItems { get; }
    public ReadOnlyObservableCollection<CatalogItemViewModel> SearchResults { get; }
    public ReadOnlyObservableCollection<string> PopularTags { get; }

    /// <summary>Raised after a catalog item was applied to the desktop (id, title).</summary>
    public event EventHandler<(Guid Id, string Title)>? WallpaperApplied;

    public CatalogDetailsViewModel DetailsViewModel { get; } = new();

    public CatalogCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                _ = ExecuteSearchAsync(resetPage: true);
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
            {
                DebounceAndSearch();
            }
        }
    }

    public WallpaperType? SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (SetField(ref _selectedTypeFilter, value))
            {
                _ = ExecuteSearchAsync(resetPage: true);
            }
        }
    }

    public CatalogResolutionFilter SelectedResolutionFilter
    {
        get => _selectedResolutionFilter;
        set
        {
            if (SetField(ref _selectedResolutionFilter, value))
            {
                _ = ExecuteSearchAsync(resetPage: true);
            }
        }
    }

    public CatalogSortOption SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (SetField(ref _selectedSortOption, value))
            {
                _ = ExecuteSearchAsync(resetPage: true);
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetField(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(HasNextPage));
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(PageSummaryText));
                OnPropertyChanged(nameof(ResultCountText));
                OnPropertyChanged(nameof(HasMultiplePages));
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetField(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(HasNextPage));
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(PageSummaryText));
                OnPropertyChanged(nameof(ResultCountText));
                OnPropertyChanged(nameof(HasMultiplePages));
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetField(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(PageSummaryText));
                OnPropertyChanged(nameof(ResultCountText));
                OnPropertyChanged(nameof(HasMultiplePages));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool HasNextPage => _currentPage < _totalPages;
    public bool HasPreviousPage => _currentPage > 1;
    public string PageSummaryText => $"{_currentPage} / {_totalPages}";
    public string ResultCountText => _totalCount == 1 ? "1 wallpaper" : $"{_totalCount} wallpapers";
    public bool HasMultiplePages => _totalPages > 1;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    public bool IsEmpty => !_isLoading && _searchResults.Count == 0;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public ExploreViewModel(
        IExploreCatalogService catalogService,
        ILibraryService libraryService,
        IWallpaperEngineOrchestrator orchestrator,
        IStorageManager storageManager,
        ILogger<ExploreViewModel> logger)
    {
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _storageManager = storageManager ?? throw new ArgumentNullException(nameof(storageManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Categories = new ReadOnlyObservableCollection<CatalogCategory>(_categories);
        FeaturedItems = new ReadOnlyObservableCollection<CatalogItemViewModel>(_featuredItems);
        SearchResults = new ReadOnlyObservableCollection<CatalogItemViewModel>(_searchResults);
        PopularTags = new ReadOnlyObservableCollection<string>(_popularTags);

        _catalogService.DownloadStateChanged += OnCatalogDownloadStateChanged;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // 1. Load categories
            var categories = await _catalogService.GetCategoriesAsync();
            _categories.Clear();
            foreach (var cat in categories)
            {
                _categories.Add(cat);
            }

            if (_categories.Count > 0 && _selectedCategory == null)
            {
                _selectedCategory = _categories[0];
                OnPropertyChanged(nameof(SelectedCategory));
            }

            // 2. Load featured items
            var featured = await _catalogService.GetFeaturedAsync(limit: 6);
            var featuredStates = await _catalogService.GetDownloadStatesAsync(featured.Select(f => f.Id));

            _featuredItems.Clear();
            foreach (var f in featured)
            {
                var state = featuredStates.TryGetValue(f.Id, out var s) ? s : CatalogDownloadState.NotDownloaded;
                _featuredItems.Add(new CatalogItemViewModel(f, state));
            }

            // 2b. Trending tags: most frequent tags across the most popular wallpapers
            var popular = await _catalogService.GetPopularAsync(limit: 40);
            var topTags = popular
                .SelectMany(i => i.Tags)
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Take(14)
                .Select(g => g.Key);
            _popularTags.Clear();
            foreach (var tag in topTags)
            {
                _popularTags.Add(tag);
            }

            // 3. Initial search query
            await ExecuteSearchAsync(resetPage: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Explore catalog view.");
            ErrorMessage = $"Unable to load catalog: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ExecuteSearchAsync(bool resetPage = false, CancellationToken ct = default)
    {
        if (resetPage)
        {
            CurrentPage = 1;
        }

        IsLoading = true;
        ErrorMessage = null;

        var query = new CatalogSearchQuery
        {
            SearchTerm = _searchText,
            Filter = new CatalogFilter
            {
                CategoryId = _selectedCategory?.Id,
                Type = _selectedTypeFilter,
                Resolution = _selectedResolutionFilter
            },
            SortBy = _selectedSortOption,
            PageNumber = _currentPage,
            PageSize = 24
        };

        try
        {
            var page = await _catalogService.SearchAsync(query, ct);
            var downloadStates = await _catalogService.GetDownloadStatesAsync(page.Items.Select(x => x.Id), ct);

            _searchResults.Clear();
            foreach (var item in page.Items)
            {
                var state = downloadStates.TryGetValue(item.Id, out var s) ? s : CatalogDownloadState.NotDownloaded;
                _searchResults.Add(new CatalogItemViewModel(item, state));
            }

            TotalPages = Math.Max(1, page.TotalPages);
            TotalCount = page.TotalCount;
        }
        catch (OperationCanceledException)
        {
            // Query was superseded or cancelled; ignore cleanly without setting error state
            _logger.LogDebug("Explore search query superseded or cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing catalog search.");
            ErrorMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public async Task NextPageAsync()
    {
        if (HasNextPage && !IsLoading)
        {
            CurrentPage++;
            await ExecuteSearchAsync(resetPage: false);
        }
    }

    public async Task PreviousPageAsync()
    {
        if (HasPreviousPage && !IsLoading)
        {
            CurrentPage--;
            await ExecuteSearchAsync(resetPage: false);
        }
    }

    public async Task SubscribeItemAsync(CatalogItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsDownloading || item.IsDownloaded)
        {
            return;
        }

        var progress = new Progress<double>(p => item.DownloadProgress = p);

        try
        {
            await _catalogService.SubscribeAsync(item.Model, progress);
            // Reconcile/refresh library
            await _libraryService.GetWallpapersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe wallpaper '{Title}'.", item.Title);
            ErrorMessage = $"Download failed for '{item.Title}': {ex.Message}";
        }
    }

    public async Task ApplyItemToDesktopAsync(CatalogItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var existing = await _libraryService.GetWallpaperByIdAsync(item.Id);
        if (existing == null || existing.IsMissing)
        {
            ErrorMessage = $"'{item.Title}' must be subscribed and downloaded before setting as desktop wallpaper.";
            return;
        }

        string absolutePath = _storageManager.ResolveAbsolutePath(existing.FilePath);
        var activeWp = new Wallpaper
        {
            Id = existing.Id,
            Title = existing.Title,
            Type = existing.Type,
            FilePath = absolutePath,
            ThumbnailPath = _storageManager.ResolveAbsolutePath(existing.ThumbnailPath),
            ContentHash = existing.ContentHash,
            Width = existing.Width,
            Height = existing.Height
        };

        await _orchestrator.ApplyWallpaperAsync("PRIMARY_MONITOR", activeWp);
        WallpaperApplied?.Invoke(this, (existing.Id, existing.Title));
    }

    public void OpenDetails(CatalogItemViewModel item)
    {
        DetailsViewModel.Open(item);
    }

    public void CloseDetails()
    {
        DetailsViewModel.Close();
    }

    private void DebounceAndSearch()
    {
        lock (_debounceLock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            // Runs on the caller (UI) thread so collection updates stay on the UI thread.
            _ = DebounceCoreAsync(token);
        }
    }

    private async Task DebounceCoreAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
            {
                await ExecuteSearchAsync(resetPage: true, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnCatalogDownloadStateChanged(object? sender, (Guid CatalogItemId, CatalogDownloadState State) e)
    {
        // The catalog service raises this from worker threads; item ViewModels are bound to the UI.
        if (_uiContext != null && SynchronizationContext.Current != _uiContext)
        {
            _uiContext.Post(_ => ApplyDownloadState(e.CatalogItemId, e.State), null);
        }
        else
        {
            ApplyDownloadState(e.CatalogItemId, e.State);
        }
    }

    private void ApplyDownloadState(Guid id, CatalogDownloadState state)
    {
        var target = _searchResults.FirstOrDefault(x => x.Id == id);
        if (target != null)
        {
            target.DownloadState = state;
        }

        var featuredTarget = _featuredItems.FirstOrDefault(x => x.Id == id);
        if (featuredTarget != null)
        {
            featuredTarget.DownloadState = state;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

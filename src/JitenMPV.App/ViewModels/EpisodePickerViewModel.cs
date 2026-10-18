using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Net;
using JitenMPV.Core.Tracking;

namespace JitenMPV.App.ViewModels;

public partial class EpisodeCandidateViewModel : ViewModelBase
{
    private static readonly HttpClient CoverHttp = JitenHttp.CreateClient();
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Covers = new();

    public EpisodeCandidateViewModel(MediaDeck deck)
    {
        Deck = deck;
        Title = deck.DisplayTitle;
        var episodes = deck.ChildrenDeckCount == 0 ? "Movie" : $"{deck.ChildrenDeckCount} episodes";
        Details = deck.ReleaseYear is { } year ? $"{episodes} · {year}" : episodes;
        Subtitle = deck.EnglishTitle is { } english && english != Title ? english : deck.OriginalTitle ?? "";
        if (!string.IsNullOrEmpty(deck.CoverName))
            _ = LoadCoverAsync(deck.CoverName);
    }

    public MediaDeck Deck { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Details { get; }

    [ObservableProperty] private Bitmap? _cover;

    private async Task LoadCoverAsync(string url)
    {
        var bitmap = await Covers.GetOrAdd(url, DownloadAsync);
        Dispatcher.UIThread.Post(() => Cover = bitmap);
    }

    private static async Task<Bitmap?> DownloadAsync(string url)
    {
        try
        {
            var bytes = await CoverHttp.GetByteArrayAsync(url);
            return Bitmap.DecodeToWidth(new MemoryStream(bytes), 96);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}

public partial class EpisodePickerViewModel : ViewModelBase
{
    private readonly EpisodePickerData _data;
    private CancellationTokenSource? _searchCts;

    public event Action<EpisodePickerResult?>? Completed;

    public EpisodePickerViewModel(EpisodePickerData data)
    {
        _data = data;
        FileTitle = data.FileTitle;
        Episode = data.Episode;
        SearchText = data.FileTitle;
        ShowCandidates(data.Candidates);
        Selected = data.CurrentDeckId is { } id
            ? Find(id)
            : null;
    }

    public string FileTitle { get; }

    public ObservableCollection<EpisodeCandidateViewModel> Candidates { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private EpisodeCandidateViewModel? _selected;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private decimal? _episode;
    [ObservableProperty] private string _status = "";

    public bool IsMovieSelected => Selected?.Deck.ChildrenDeckCount == 0;

    partial void OnSelectedChanged(EpisodeCandidateViewModel? value) => OnPropertyChanged(nameof(IsMovieSelected));

    private EpisodeCandidateViewModel? Find(int deckId)
    {
        foreach (var candidate in Candidates)
            if (candidate.Deck.DeckId == deckId)
                return candidate;
        return null;
    }

    private void ShowCandidates(IReadOnlyList<DeckCandidate> candidates)
    {
        Candidates.Clear();
        foreach (var candidate in candidates)
            Candidates.Add(new EpisodeCandidateViewModel(candidate.Deck));
        Status = candidates.Count == 0 ? "No shows found. Try another search." : "";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchText.Trim();
        if (query.Length == 0) return;

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        Status = "Searching...";
        try
        {
            var results = await _data.Search(query, cts.Token);
            if (!cts.IsCancellationRequested) ShowCandidates(results);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status = $"Search failed: {ex.Message}";
        }
    }

    private bool CanConfirm => Selected is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (Selected is not { } selected) return;
        var episode = selected.Deck.ChildrenDeckCount == 0 || Episode is null ? (int?)null : (int)Episode.Value;
        Completed?.Invoke(new EpisodePickerResult(selected.Deck, episode, false));
    }

    [RelayCommand]
    private void IgnoreFolder() => Completed?.Invoke(new EpisodePickerResult(null, null, true));

    [RelayCommand]
    private void Cancel() => Completed?.Invoke(null);
}

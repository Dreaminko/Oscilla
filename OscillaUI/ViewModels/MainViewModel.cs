using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using OscillaUI.Base;
using OscillaUI.Models;
using OscillaUI.Services;

namespace OscillaUI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ILibraryService _libraryService;

        private TrackModel _selectedTrack;
        public TrackModel SelectedTrack { get => _selectedTrack; set { if (SetProperty(ref _selectedTrack, value) && value != null) { IsPlaying = true; PlayerStatus = "● PLAYING"; CurrentTime = 0; TotalTime = 270; PlayingQueue = (CurrentView == "PlaylistView" && SelectedPlaylist != null) ? SelectedPlaylist.Tracks : FilteredTracks; OnPropertyChanged(nameof(PlayerStatus)); OnPropertyChanged(nameof(IsPlaying)); } } }

        public bool IsPlaying { get; set; }
        public string PlayerStatus { get; set; } = "● IDLE";
        public string TimeDisplay => $"{TimeSpan.FromSeconds(CurrentTime):mm\\:ss} / {TimeSpan.FromSeconds(TotalTime):mm\\:ss}";
        private double _currentTime; public double CurrentTime { get => _currentTime; set { if (SetProperty(ref _currentTime, value)) { OnPropertyChanged(nameof(TimeDisplay)); if (_currentTime >= TotalTime && TotalTime > 0) HandleTrackFinished(); } } }
        public double TotalTime { get; set; } = 270;
        public string LoopModeText { get; set; } = "ALL";

        public ObservableCollection<TrackModel> FilteredTracks { get; set; } = new ObservableCollection<TrackModel>();
        public ObservableCollection<PlaylistModel> CustomPlaylists { get; set; } = new ObservableCollection<PlaylistModel>();
        public ObservableCollection<LibraryModel> ActiveLibraries { get; set; } = new ObservableCollection<LibraryModel>();
        public ObservableCollection<TrackModel> PlayingQueue { get; set; }

        public string CurrentView { get; set; } = "AllSongs";
        private PlaylistModel _selectedPlaylist; public PlaylistModel SelectedPlaylist { get => _selectedPlaylist; set { if (SetProperty(ref _selectedPlaylist, value) && value != null) CurrentView = "PlaylistView"; } }
        private LibraryModel _selectedLibrary; public LibraryModel SelectedLibrary { get => _selectedLibrary; set => SetProperty(ref _selectedLibrary, value); }

        public RelayCommand<string> NavCommand { get; }
        public RelayCommand<object> AddPlaylistCommand { get; }
        public RelayCommand<PlaylistModel> RenameCommand { get; }
        public RelayCommand<PlaylistModel> ConfirmRenameCommand { get; }
        public RelayCommand<PlaylistModel> DeletePlaylistCommand { get; }
        public RelayCommand<LibraryModel> RemoveLibraryCommand { get; }
        public RelayCommand<object> PlayPauseCommand { get; }
        public RelayCommand<object> PrevTrackCommand { get; }
        public RelayCommand<object> NextTrackCommand { get; }
        public RelayCommand<object> ToggleLoopModeCommand { get; }
        public RelayCommand<LibraryModel> ToggleLibraryCommand { get; }

        public MainViewModel(IAudioEngine audio, ILibraryService libSvc)
        {
            _libraryService = libSvc;
            NavCommand = new RelayCommand<string>(v => { CurrentView = v; if (v == "AllSongs") SelectedPlaylist = null; OnPropertyChanged(nameof(CurrentView)); });
            AddPlaylistCommand = new RelayCommand<object>(o => { var p = new PlaylistModel { Name = "New Playlist", IsEditing = true }; CustomPlaylists.Add(p); SelectedPlaylist = p; OnPropertyChanged(nameof(SelectedPlaylist)); });
            RenameCommand = new RelayCommand<PlaylistModel>(p => { foreach (var x in CustomPlaylists) x.IsEditing = false; p.IsEditing = true; });
            ConfirmRenameCommand = new RelayCommand<PlaylistModel>(p => p.IsEditing = false);
            DeletePlaylistCommand = new RelayCommand<PlaylistModel>(p => CustomPlaylists.Remove(p));
            RemoveLibraryCommand = new RelayCommand<LibraryModel>(l => { ActiveLibraries.Remove(l); RebuildAllSongs(); _libraryService.RewriteManifest(ActiveLibraries); });
            ToggleLibraryCommand = new RelayCommand<LibraryModel>(l => { l.IsActive = !l.IsActive; RebuildAllSongs(); });
            PlayPauseCommand = new RelayCommand<object>(o => { if (SelectedTrack != null) { IsPlaying = !IsPlaying; PlayerStatus = IsPlaying ? "● PLAYING" : "● PAUSED"; OnPropertyChanged(nameof(IsPlaying)); OnPropertyChanged(nameof(PlayerStatus)); } });
            ToggleLoopModeCommand = new RelayCommand<object>(o => { LoopModeText = LoopModeText == "ALL" ? "UNI" : "ALL"; OnPropertyChanged(nameof(LoopModeText)); });
            PrevTrackCommand = new RelayCommand<object>(o => AdvanceTrack(-1));
            NextTrackCommand = new RelayCommand<object>(o => AdvanceTrack(1));
            InitializeData();
        }

        public async Task AddNewLibraryFromPathAsync(string path, string name)
        {
            var tracks = await Task.Run(() => _libraryService.ScanFolderAndAppendToManifest(path));
            if (!tracks.Any()) return;
            var lib = new LibraryModel { Name = name, Path = path };
            foreach (var t in tracks) lib.Tracks.Add(t);
            ActiveLibraries.Add(lib); RebuildAllSongs();
        }

        private void RebuildAllSongs() { FilteredTracks.Clear(); foreach (var l in ActiveLibraries.Where(x => x.IsActive)) foreach (var t in l.Tracks) FilteredTracks.Add(t); }
        private void InitializeData()
        {
            var loaded = _libraryService?.LoadAndMergeManifests();
            if (loaded != null)
            {
                var groups = loaded.GroupBy(t => Path.GetDirectoryName(t.FilePath));
                foreach (var g in groups)
                {
                    var lib = new LibraryModel { Name = Path.GetFileName(g.Key) ?? g.Key, Path = g.Key };
                    foreach (var t in g) lib.Tracks.Add(t);
                    ActiveLibraries.Add(lib);
                }
            }
            RebuildAllSongs();
        }
        public void HandleTrackFinished() { if (LoopModeText == "UNI") CurrentTime = 0; else AdvanceTrack(1); }
        private void AdvanceTrack(int step) { if (PlayingQueue == null || !PlayingQueue.Any() || SelectedTrack == null) return; int i = PlayingQueue.IndexOf(SelectedTrack); SelectedTrack = PlayingQueue[(i + step + PlayingQueue.Count) % PlayingQueue.Count]; }
        public void AddTrackToPlaylist(TrackModel t, PlaylistModel p) { if (t != null && p != null && !p.Tracks.Contains(t)) p.Tracks.Add(t); }
        public void OnShutdown() { }
    }
}
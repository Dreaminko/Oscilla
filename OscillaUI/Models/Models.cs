using System.Collections.ObjectModel;
using OscillaUI.Base;

namespace OscillaUI.Models
{
    public class TrackModel : ViewModelBase
    {
        public string Id { get; set; }
        public string FilePath { get; set; }
        public string Format { get; set; }
        public string DisplayName { get; set; }
    }

    public class PlaylistModel : ViewModelBase
    {
        private string _name;
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set { SetProperty(ref _isEditing, value); OnPropertyChanged(nameof(IsNotEditing)); } }
        public bool IsNotEditing => !IsEditing;
        public ObservableCollection<TrackModel> Tracks { get; set; } = new ObservableCollection<TrackModel>();
    }

    public class LibraryModel : ViewModelBase
    {
        public string Name { get; set; }
        public string Path { get; set; }
        private bool _isActive = true;
        public bool IsActive { get => _isActive; set { if (SetProperty(ref _isActive, value)) StatusColor = value ? "#00FFCC" : "#555555"; } }
        private string _statusColor = "#00FFCC";
        public string StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }
        public ObservableCollection<TrackModel> Tracks { get; set; } = new ObservableCollection<TrackModel>();
    }
}
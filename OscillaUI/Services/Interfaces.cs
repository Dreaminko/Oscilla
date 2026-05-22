using System.Collections.Generic;
using OscillaUI.Models;

namespace OscillaUI.Services
{
    public interface IAudioEngine { void Initialize(); void Play(string filePath); }
    public interface ILibraryService
    {
        List<TrackModel> LoadAndMergeManifests();
        void SavePlaylists(IEnumerable<PlaylistModel> playlists);
        List<TrackModel> ScanFolderAndAppendToManifest(string folderPath);
        void RewriteManifest(IEnumerable<LibraryModel> activeLibraries);
    }
}
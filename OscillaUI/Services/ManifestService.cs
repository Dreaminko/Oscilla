using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OscillaUI.Models;

namespace OscillaUI.Services
{
    public class ManifestService : ILibraryService
    {
        private readonly string ManifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library.oscillamanifest");

        public List<TrackModel> LoadAndMergeManifests()
        {
            var tracks = new List<TrackModel>();
            if (!File.Exists(ManifestPath)) return tracks;
            try
            {
                var lines = File.ReadAllLines(ManifestPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("OSCILLA") || line.StartsWith("UUID") || !line.Contains("|")) continue;
                    var parts = line.Split('|');
                    if (parts.Length >= 4) tracks.Add(new TrackModel { Id = parts[0], FilePath = parts[1], Format = parts[2], DisplayName = parts[3] });
                }
            }
            catch { }
            return tracks;
        }

        public List<TrackModel> ScanFolderAndAppendToManifest(string folderPath)
        {
            var newTracks = new List<TrackModel>();
            if (!Directory.Exists(folderPath)) return newTracks;
            var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                                 .Where(f => new[] { ".mp3", ".flac", ".wav" }.Contains(Path.GetExtension(f).ToLower())).ToList();
            int startIndex = 1;
            if (File.Exists(ManifestPath))
            {
                var last = File.ReadLines(ManifestPath).LastOrDefault(l => l.Contains("|"));
                if (last != null && int.TryParse(last.Split('|')[0], out int id)) startIndex = id + 1;
            }
            else File.WriteAllText(ManifestPath, "OSCILLA_MANIFEST_V1\nUUID:" + Guid.NewGuid() + "\n");

            var lines = files.Select(f => {
                var t = new TrackModel { Id = (startIndex++).ToString("D5"), FilePath = f, Format = Path.GetExtension(f).Replace(".", "").ToUpper(), DisplayName = Path.GetFileNameWithoutExtension(f) };
                newTracks.Add(t);
                return $"{t.Id}|{t.FilePath}|{t.Format}|{t.DisplayName}";
            });
            File.AppendAllLines(ManifestPath, lines);
            return newTracks;
        }

        public void RewriteManifest(IEnumerable<LibraryModel> libs)
        {
            if (!libs.Any()) { if (File.Exists(ManifestPath)) File.Delete(ManifestPath); return; }
            var lines = new List<string> { "OSCILLA_MANIFEST_V1", "UUID:" + Guid.NewGuid() };
            lines.AddRange(libs.SelectMany(l => l.Tracks).Select(t => $"{t.Id}|{t.FilePath}|{t.Format}|{t.DisplayName}"));
            File.WriteAllLines(ManifestPath, lines);
        }
        public void SavePlaylists(IEnumerable<PlaylistModel> p) { }
    }
}
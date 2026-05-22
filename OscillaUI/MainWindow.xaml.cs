using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using OscillaUI.Services;
using OscillaUI.ViewModels;
using OscillaUI.Models;

namespace OscillaUI
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();
            var manifestService = new ManifestService();
            ViewModel = new MainViewModel(null, manifestService);
            this.DataContext = ViewModel;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }

        protected override void OnClosing(CancelEventArgs e)
        {
            ViewModel.OnShutdown();
            base.OnClosing(e);
        }

        private async void AddLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a music folder to add to Oscilla Engine";
                dialog.ShowNewFolderButton = false;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string path = dialog.SelectedPath;
                    string name = System.IO.Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;

                    await ViewModel.AddNewLibraryFromPathAsync(path, name);
                }
            }
        }

        private void Track_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe && fe.DataContext is TrackModel track)
            {
                DragDrop.DoDragDrop(fe, track, DragDropEffects.Copy);
            }
        }

        private void Playlist_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is PlaylistModel playlist)
            {
                if (e.Data.GetDataPresent(typeof(TrackModel)))
                {
                    var track = e.Data.GetData(typeof(TrackModel)) as TrackModel;
                    ViewModel.AddTrackToPlaylist(track, playlist);
                }
            }
        }
    }
}
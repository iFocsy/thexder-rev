using System.Windows;
using System.Windows.Controls;
using Serilog;
using Thexder.Services;
using Thexder.UI;
using Thexder.Utils;

namespace Thexder
{
    public partial class App : Application
    {
        private Mutex? instanceMutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            LoggerService.Configure();
            GlobalExceptionHandlers.Register(this);
            FixedSizeWindows.Register();
            CompactMenus.Register(this);
            NativeMethods.timeBeginPeriod(1);
            _ = CosmosScene.Prepare(); // the menu's planet surfaces, made while the intro plays
            Log.Information("Thexder {Version} starting", typeof(App).Assembly.GetName().Version);

            GameOptions options;
            try
            {
                options = GameOptions.Parse(e.Args);
            }
            catch (ArgumentException ex)
            {
                Dialogs.Message(null, $"{ex.Message}\n\n{GameOptions.Usage}", "Thexder", DialogIcon.Error);
                Shutdown(2);
                return;
            }

            if (!await PrepareAssetsAsync())
            {
                Log.Information("No game files: the player left without a CD image");
                Shutdown(1);
                return;
            }

            // A second copy asks first.
            instanceMutex = new Mutex(true, @"Local\Thexder.Game", out bool firstInstance);
            if (!firstInstance && options.UiScriptDir == null &&
                !Dialogs.AskYesNo(null, "You are already running this program.\nDo you want to run another copy?", "Thexder"))
            {
                Shutdown(0);
                return;
            }

            var window = new MainWindow(AppPaths.AssetsDirectory, options);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            NativeMethods.timeEndPeriod(1);
            instanceMutex?.Dispose();
            Log.Information("Thexder exiting");
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        // Checks assets\ and restores missing or damaged files from the CD image in iso\, or else from one the player
        // selects, asking again until an image works. False when the player exits instead.
        private static async Task<bool> PrepareAssetsAsync()
        {
            string assets = AppPaths.AssetsDirectory;
            var damaged = await Task.Run(() => AssetStore.FindDamaged(assets));
            if (damaged.Count == 0)
            {
                return true;
            }
            Log.Information("{Count} game files missing or damaged", damaged.Count);
            string? image = AssetStore.FindImage(AppPaths.IsoDirectory);
            string problem = damaged.Count == AssetStore.Manifest.Count
                ? "The game files have not been installed yet."
                : $"{damaged.Count} of the game files are missing or damaged.";
            while (true)
            {
                if (image != null)
                {
                    var (failed, error) = await RestoreAsync(image, assets, damaged);
                    if (failed.Count == 0)
                    {
                        return true;
                    }
                    Log.Warning("Restoring from {Image} failed: {Error}", image, error);
                    damaged = failed;
                    problem = error;
                }
                bool? select = Dialogs.Message(null,
                    $"{problem}\n\nThexder needs the original game CD image (.iso) to play. Select it, or put it in:\n{AppPaths.IsoDirectory}",
                    "Thexder", DialogIcon.Warning, yesNo: true, okText: "Select image...", noText: "Exit");
                if (select != true)
                {
                    return false;
                }
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Select the Thexder CD image",
                    Filter = "CD images (*.iso)|*.iso|All files (*.*)|*.*",
                };
                image = dialog.ShowDialog() == true ? dialog.FileName : null;
            }
        }

        // One attempt: copies the files from the image. Returns those still missing and, when some are, why.
        private static async Task<(List<AssetStore.Entry> Failed, string Error)> RestoreAsync(string image, string assets, List<AssetStore.Entry> damaged)
        {
            string name = Path.GetFileName(image);
            var bar = new ProgressBar { Height = 18, Maximum = 1, Margin = new Thickness(0, 10, 0, 0) };
            var text = damaged.Count == AssetStore.Manifest.Count ? "Installing the game files..." : "Repairing the game files...";
            var progressWindow = new Window
            {
                Title = "Thexder",
                Width = 360,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new StackPanel { Margin = new Thickness(16), Children = { new TextBlock { Text = text }, bar } },
            };
            progressWindow.Show();
            try
            {
                var progress = new Progress<double>(v => bar.Value = v);
                var failed = await Task.Run(() => AssetStore.Restore(image, assets, damaged, progress));
                if (failed.Count == 0)
                {
                    Log.Information("Game files restored from {Image}", image);
                    return (failed, "");
                }
                return (failed, failed.Count == damaged.Count
                    ? $"\"{name}\" is not the Thexder CD image."
                    : $"\"{name}\" does not hold valid copies of {failed.Count} game files.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "Reading {Image} failed", image);
                return (damaged, $"\"{name}\" could not be read as a CD image.");
            }
            finally
            {
                progressWindow.Close();
            }
        }
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MockRoom.Licensing;

namespace MockRoom;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Run the licensing gate before showing the editor. The provider is
            // chosen at the composition root; during development it is bypassed.
            var license = new LicenseManager(LicensingOptions.CreateProvider());
            var status = license.ValidateAsync().GetAwaiter().GetResult().Status;

            desktop.MainWindow = new MainWindow(status);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

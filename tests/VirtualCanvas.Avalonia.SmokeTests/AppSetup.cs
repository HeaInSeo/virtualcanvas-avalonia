using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestFramework]
[assembly: AvaloniaTestApplication(typeof(VirtualCanvas.Avalonia.SmokeTests.SmokeApp))]

namespace VirtualCanvas.Avalonia.SmokeTests;

public class SmokeApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SmokeApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

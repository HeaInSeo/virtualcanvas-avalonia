using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

// [AvaloniaTestFramework] tells xUnit to use Avalonia's test runner (no-arg marker).
// [AvaloniaTestApplication] tells Avalonia which AppBuilder entry point to use.
// Both are required: the former switches the xUnit executor; the latter wires up the session.
[assembly: AvaloniaTestFramework]
[assembly: AvaloniaTestApplication(typeof(VirtualCanvas.Avalonia.Tests.TestApp))]

namespace VirtualCanvas.Avalonia.Tests;

public class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

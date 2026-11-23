using AutoWire;

namespace AutoWire.Sample.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddAutoWireServices();
        return builder.Build();
    }
}

public interface IGreetingService { string Greet(); }

[Singleton]
public class GreetingService : IGreetingService
{
    public string Greet() => "Hello from AutoWire on MAUI";
}

[Transient]
public class MainPage : ContentPage
{
    public MainPage(IGreetingService greeting) => Content = new Label { Text = greeting.Greet() };
}

public class App : Application
{
    private readonly MainPage _page;

    public App(MainPage page) => _page = page;

    protected override Window CreateWindow(IActivationState? activationState) => new(_page);
}

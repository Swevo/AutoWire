using AutoWire;
using AutoWire.Sample.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddAutoWireServices();

var app = builder.Build();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public interface IClock { DateTime Now { get; } }

[Singleton]
public class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

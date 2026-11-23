using AutoWire;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAutoWireServices();

var app = builder.Build();
app.MapGet("/", (IGreeter greeter) => greeter.Greet("AutoWire"));
app.Run();

public interface IGreeter { string Greet(string name); }

[Scoped]
public class Greeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

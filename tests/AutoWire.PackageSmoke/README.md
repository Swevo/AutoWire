# AutoWire.PackageSmoke

Consumes the packed AutoWire `.nupkg` the way a user would. CI runs it on the .NET 8 SDK only, and publishes it with Native AOT (warnings as errors).

Run it locally from the repo root:

```bash
dotnet pack src/AutoWire/AutoWire.csproj -c Release -o nupkgs -p:Version=0.0.0-local
dotnet run --project tests/AutoWire.PackageSmoke -p:AutoWireVersion=0.0.0-local

# Native AOT check (Linux/macOS, or Windows with the C++ build tools)
dotnet publish tests/AutoWire.PackageSmoke -c Release -r linux-x64 -p:PublishAot=true -p:AutoWireVersion=0.0.0-local -o aot-out
```

After re-packing the same version, delete `~/.nuget/packages/autowire/0.0.0-local` so NuGet picks up the new package.

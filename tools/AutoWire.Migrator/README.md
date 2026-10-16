# Swevo.AutoWire.Migrator

`dotnet tool` that migrates existing DI registrations to [AutoWire](https://github.com/Swevo/AutoWire) attributes.

```bash
dotnet tool install --global Swevo.AutoWire.Migrator

# Analyze (no changes) – writes autowire-semantic-migration-report.md/.json
autowire-migrate --path ./src

# Rewrite sources: Scrutor scans, Autofac RegisterType<>, services.AddScoped/Singleton/Transient
autowire-migrate --path ./src --apply

# CI gate
autowire-migrate --path ./src --fail-on-threshold --max-manual 0

# Diff two RegistrationSummary.RegistrationManifestEntries exports
autowire-migrate diff --baseline before.txt --candidate after.txt --report diff.md
```

Every registration gets a confidence: `AutoConverted`, `NeedsReview` or `Manual`. `--apply` only rewrites
`AutoConverted` registrations (plus Scrutor scaffolds): it adds `[AutoWire.Scoped(typeof(IFoo))]`-style attributes
to the implementation class and removes the old registration statement. Call `services.AddAutoWireServices()`
afterwards.

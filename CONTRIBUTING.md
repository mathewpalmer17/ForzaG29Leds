# Contributing

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download) and Windows.

```
git clone https://github.com/mathewpalmer17/ForzaG29Leds
cd ForzaG29Leds
dotnet build
dotnet test
```

## Running the app locally

```
dotnet run --project ForzaG29Leds
```

## Running the tests

```
dotnet test
```

## Submitting a PR

- Keep changes focused — one fix or feature per PR
- Make sure `dotnet build` and `dotnet test` both pass
- Describe what the change does and why in the PR description

## Reporting a bug

Use the [bug report template](.github/ISSUE_TEMPLATE/bug_report.md) — include your wheel model, OS version, and app version (tray right-click › About).

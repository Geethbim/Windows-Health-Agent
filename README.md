# Windows Health Monitor

Two-app C#/.NET 8 demo:

- **Server**: ASP.NET Core minimal API + tiny dashboard
- **Agent**: Worker that collects basic Windows health and POSTs it to the Server

## Prereqs

You need **.NET 8 SDK**.

Two options:

1. **Use a system-wide install** (recommended): install .NET 8 SDK and use `dotnet`.

2. **Install locally into this folder** (good for demos): run the provided script to install into `.dotnet/`.
   Note: `.dotnet/` is **gitignored** (SDK binaries are not committed).

## Run (PowerShell)

From the repo root:

### Optional: install local .NET 8 SDK

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
./dotnet-install.ps1 -Channel 8.0 -InstallDir "$PWD/.dotnet"
```

### 1) Start the Server

```powershell
$dotnet = "$PWD\.dotnet\dotnet.exe"
& $dotnet run --project .\WindowsHealthMonitor.Server
```

Open the dashboard:

- http://localhost:5080/

### 2) Start the Agent

In another terminal:

```powershell
$dotnet = "$PWD\.dotnet\dotnet.exe"
& $dotnet run --project .\WindowsHealthMonitor.Agent
```

Within ~10 seconds you should see your machine appear on the dashboard.

## Configure the Agent

Edit [WindowsHealthMonitor.Agent/appsettings.json](WindowsHealthMonitor.Agent/appsettings.json):

- `Agent:ServerBaseUrl` (default: `http://localhost:5080`)
- `Agent:PollSeconds` (default: `10`)
- `Agent:ServicesToMonitor` (default: `Spooler`, `W32Time`)

## API endpoints

- `POST /api/health` ingest a `HealthReport`
- `GET /api/machines` list latest health by machine
- `GET /api/machines/{name}` fetch latest health for a machine
- `GET /api/alerts` derived alerts from latest health (disk < 10%, service not Running)

## Notes

- Storage is **in-memory** (restart Server = data cleared).
- CPU and memory usage are **best-effort** and implemented with Windows APIs.

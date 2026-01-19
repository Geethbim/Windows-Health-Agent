using System.Collections.Concurrent;
using WindowsHealthMonitor.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
	options.AddDefaultPolicy(policy => policy
		.AllowAnyHeader()
		.AllowAnyMethod()
		.AllowAnyOrigin());
});

var app = builder.Build();

app.UseCors();

app.UseDefaultFiles();
app.UseStaticFiles();

var latestByMachine = new ConcurrentDictionary<string, HealthReport>(StringComparer.OrdinalIgnoreCase);

app.MapPost("/api/health", (HealthReport report) =>
{
	if (string.IsNullOrWhiteSpace(report.MachineName))
	{
		return Results.BadRequest(new { error = "MachineName is required" });
	}

	var normalized = report with { MachineName = report.MachineName.Trim() };
	latestByMachine[normalized.MachineName] = normalized;
	return Results.Accepted();
});

app.MapGet("/api/machines", () =>
{
	var list = latestByMachine.Values
		.OrderByDescending(r => r.TimestampUtc)
		.ThenBy(r => r.MachineName)
		.ToArray();
	return Results.Ok(list);
});

app.MapGet("/api/machines/{name}", (string name) =>
{
	if (latestByMachine.TryGetValue(name, out var report))
	{
		return Results.Ok(report);
	}

	return Results.NotFound(new { error = $"Unknown machine '{name}'" });
});

app.MapGet("/api/alerts", () =>
{
	var alerts = new List<Alert>();

	foreach (var report in latestByMachine.Values)
	{
		foreach (var disk in report.Disks)
		{
			if (disk.FreePercent < 10)
			{
				alerts.Add(new Alert(
					report.MachineName,
					report.TimestampUtc,
					"Critical",
					"DiskLow",
					$"Disk {disk.Name} is low: {disk.FreePercent:F1}% free"));
			}
		}

		foreach (var svc in report.Services)
		{
			if (!string.Equals(svc.Status, "Running", StringComparison.OrdinalIgnoreCase))
			{
				alerts.Add(new Alert(
					report.MachineName,
					report.TimestampUtc,
					"Warning",
					"ServiceNotRunning",
					$"Service {svc.Name} is {svc.Status}"));
			}
		}
	}

	var ordered = alerts
		.OrderByDescending(a => a.TimestampUtc)
		.ThenBy(a => a.MachineName)
		.ToArray();

	return Results.Ok(ordered);
});

app.Run();

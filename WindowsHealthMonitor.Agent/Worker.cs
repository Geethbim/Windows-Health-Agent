using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using WindowsHealthMonitor.Contracts;

namespace WindowsHealthMonitor.Agent;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AgentOptions> _options;
    private readonly SystemMetrics.CpuSampler _cpuSampler = new();

    public Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, IOptionsMonitor<AgentOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WindowsHealthMonitor.Agent starting on {Machine}", Environment.MachineName);

        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                var options = _options.CurrentValue;
                var report = BuildReport(options);

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                var url = options.ServerBaseUrl.TrimEnd('/') + "/api/health";

                var response = await client.PostAsJsonAsync(url, report, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("POST {Url} failed: {Status}", url, (int)response.StatusCode);
                }
                else
                {
                    _logger.LogInformation("Posted health report ({Machine})", report.MachineName);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health loop error");
            }

            var pollSeconds = Math.Max(2, _options.CurrentValue.PollSeconds);
            var delay = TimeSpan.FromSeconds(pollSeconds) - (DateTimeOffset.UtcNow - started);
            if (delay < TimeSpan.FromMilliseconds(200))
            {
                delay = TimeSpan.FromMilliseconds(200);
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private HealthReport BuildReport(AgentOptions options)
    {
        var machine = Environment.MachineName;
        var timestamp = DateTimeOffset.UtcNow;
        var os = SystemMetrics.GetOsDescription();
        var uptime = SystemMetrics.GetUptime();
        double? cpu = _cpuSampler.SampleCpuUsagePercent();
        double? memUsed = SystemMetrics.TryGetMemoryUsage()?.usedPercent;

        var disks = SystemMetrics.GetFixedDisks()
            .Select(d => new DiskInfo(d.name, d.totalBytes, d.freeBytes, d.freePercent))
            .ToArray();

        var services = SystemMetrics.GetWindowsServiceStatuses(options.ServicesToMonitor)
            .Select(s => new ServiceInfo(s.name, s.status, s.displayName))
            .ToArray();

        var fansRaw = SystemMetrics.TryGetFanRpm();
        var fans = fansRaw.Count == 0
            ? null
            : fansRaw
                .Select(f => new FanInfo(f.name, f.rpm, f.hardware))
                .ToArray();

        var tempsRaw = SystemMetrics.TryGetTemperaturesCelsius();
        var temps = tempsRaw.Count == 0
            ? null
            : tempsRaw
                .Select(t => new TemperatureInfo(t.name, t.celsius, t.hardware))
                .ToArray();

        return new HealthReport(
            machine,
            timestamp,
            os,
            uptime,
            cpu,
            memUsed,
            disks,
            services,
            fans,
            temps);
    }
}

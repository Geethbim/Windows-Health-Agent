﻿namespace WindowsHealthMonitor.Contracts;

public sealed record DiskInfo(
	string Name,
	long TotalBytes,
	long FreeBytes,
	double FreePercent
);

public sealed record ServiceInfo(
	string Name,
	string Status,
	string? DisplayName = null
);

public sealed record FanInfo(
	string Name,
	double Rpm,
	string? Hardware = null
);

public sealed record TemperatureInfo(
	string Name,
	double Celsius,
	string? Hardware = null
);

public sealed record HealthReport(
	string MachineName,
	DateTimeOffset TimestampUtc,
	string OsDescription,
	TimeSpan Uptime,
	double? CpuUsagePercent,
	double? MemoryUsedPercent,
	IReadOnlyList<DiskInfo> Disks,
	IReadOnlyList<ServiceInfo> Services,
	IReadOnlyList<FanInfo>? Fans = null,
	IReadOnlyList<TemperatureInfo>? Temperatures = null
);

public sealed record Alert(
	string MachineName,
	DateTimeOffset TimestampUtc,
	string Severity,
	string Code,
	string Message
);

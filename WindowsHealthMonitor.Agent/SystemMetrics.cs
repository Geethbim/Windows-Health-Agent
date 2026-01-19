using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using System.Management;

namespace WindowsHealthMonitor.Agent;

internal static class SystemMetrics
{
	public static TimeSpan GetUptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);

	public static (double usedPercent, ulong totalBytes, ulong availBytes)? TryGetMemoryUsage()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		var mem = new MEMORYSTATUSEX();
		mem.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
		if (!GlobalMemoryStatusEx(ref mem))
		{
			return null;
		}

		var total = mem.ullTotalPhys;
		var avail = mem.ullAvailPhys;
		if (total == 0)
		{
			return null;
		}

		double usedPercent = 100.0 * (1.0 - (double)avail / total);
		return (usedPercent, total, avail);
	}

	public sealed class CpuSampler
	{
		private ulong? _prevIdle;
		private ulong? _prevKernel;
		private ulong? _prevUser;

		public double? SampleCpuUsagePercent()
		{
			if (!OperatingSystem.IsWindows())
			{
				return null;
			}

			if (!GetSystemTimes(out var idle, out var kernel, out var user))
			{
				return null;
			}

			ulong idleTicks = ToUInt64(idle);
			ulong kernelTicks = ToUInt64(kernel);
			ulong userTicks = ToUInt64(user);

			if (_prevIdle is null || _prevKernel is null || _prevUser is null)
			{
				_prevIdle = idleTicks;
				_prevKernel = kernelTicks;
				_prevUser = userTicks;
				return null;
			}

			ulong idleDelta = idleTicks - _prevIdle.Value;
			ulong kernelDelta = kernelTicks - _prevKernel.Value;
			ulong userDelta = userTicks - _prevUser.Value;

			_prevIdle = idleTicks;
			_prevKernel = kernelTicks;
			_prevUser = userTicks;

			ulong totalDelta = kernelDelta + userDelta;
			if (totalDelta == 0)
			{
				return 0;
			}

			// Kernel includes idle, so subtract idle from total usage.
			double busy = Math.Max(0, (double)(totalDelta - idleDelta) / totalDelta);
			return busy * 100.0;
		}
	}

	public static string GetOsDescription() => RuntimeInformation.OSDescription;

	public static IReadOnlyList<(string name, string status, string? displayName)> GetWindowsServiceStatuses(IEnumerable<string> serviceNames)
	{
		var results = new List<(string, string, string?)>();

		if (!OperatingSystem.IsWindows())
		{
			foreach (var name in serviceNames)
			{
				results.Add((name, "Unsupported", null));
			}
			return results;
		}

		foreach (var name in serviceNames.Where(s => !string.IsNullOrWhiteSpace(s)))
		{
			try
			{
				using var sc = new System.ServiceProcess.ServiceController(name);
				results.Add((name, sc.Status.ToString(), sc.DisplayName));
			}
			catch
			{
				results.Add((name, "NotFound", null));
			}
		}

		return results;
	}

	public static IReadOnlyList<(string name, long totalBytes, long freeBytes, double freePercent)> GetFixedDisks()
	{
		var disks = new List<(string, long, long, double)>();
		foreach (var drive in DriveInfo.GetDrives())
		{
			if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
			{
				continue;
			}

			long total = drive.TotalSize;
			long free = drive.TotalFreeSpace;
			double freePercent = total > 0 ? (100.0 * (double)free / total) : 0;
			disks.Add((drive.Name, total, free, freePercent));
		}

		return disks;
	}

	public static IReadOnlyList<(string name, double rpm, string? hardware)> TryGetFanRpm()
	{
		if (!OperatingSystem.IsWindows())
		{
			return Array.Empty<(string, double, string?)>();
		}

		try
		{
			var computer = new Computer
			{
				IsCpuEnabled = true,
				IsMotherboardEnabled = true,
				IsControllerEnabled = true,
				IsGpuEnabled = true,
				IsStorageEnabled = false,
				IsNetworkEnabled = false
			};
			try
			{
				computer.Open();

				var fans = new List<(string, double, string?)>();
				foreach (var hw in computer.Hardware)
				{
					CollectFansRecursive(hw, fans);
				}

				return fans;
			}
			finally
			{
				computer.Close();
			}
		}
		catch
		{
			// Fan sensors are highly hardware/driver dependent; fail silently.
			return Array.Empty<(string, double, string?)>();
		}
	}

	public static IReadOnlyList<(string name, double celsius, string? hardware)> TryGetTemperaturesCelsius()
	{
		if (!OperatingSystem.IsWindows())
		{
			return Array.Empty<(string, double, string?)>();
		}

		// Attempt 1: LibreHardwareMonitor (best detail when available)
		try
		{
			var computer = new Computer
			{
				IsCpuEnabled = true,
				IsMotherboardEnabled = true,
				IsControllerEnabled = true,
				IsGpuEnabled = true,
				IsStorageEnabled = true
			};

			try
			{
				computer.Open();

				var temps = new List<(string, double, string?)>();
				foreach (var hw in computer.Hardware)
				{
					CollectTempsRecursive(hw, temps);
				}

				if (temps.Count > 0)
				{
					return temps;
				}
			}
			finally
			{
				computer.Close();
			}
		}
		catch
		{
			// Ignore and fall back.
		}

		// Attempt 2: WMI/ACPI thermal zones (often available on laptops; less specific)
		try
		{
			var results = new List<(string, double, string?)>();
			using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
			foreach (ManagementObject obj in searcher.Get())
			{
				var name = obj["InstanceName"]?.ToString() ?? "ThermalZone";
				var raw = obj["CurrentTemperature"]; // tenths of Kelvin
				if (raw is null)
				{
					continue;
				}

				double tenthKelvin = Convert.ToDouble(raw);
				double celsius = (tenthKelvin / 10.0) - 273.15;
				if (double.IsFinite(celsius))
				{
					results.Add((name, celsius, "ACPI"));
				}
			}

			return results;
		}
		catch
		{
			return Array.Empty<(string, double, string?)>();
		}
	}

	private static void CollectFansRecursive(IHardware hardware, List<(string name, double rpm, string? hardware)> fans)
	{
		hardware.Update();

		foreach (var sensor in hardware.Sensors)
		{
			if (sensor.SensorType != SensorType.Fan)
			{
				continue;
			}

			if (!sensor.Value.HasValue)
			{
				continue;
			}

			var rpm = (double)sensor.Value.Value;
			var name = string.IsNullOrWhiteSpace(sensor.Name) ? "Fan" : sensor.Name;
			fans.Add((name, rpm, hardware.Name));
		}

		foreach (var sub in hardware.SubHardware)
		{
			CollectFansRecursive(sub, fans);
		}
	}

	private static void CollectTempsRecursive(IHardware hardware, List<(string name, double celsius, string? hardware)> temps)
	{
		hardware.Update();

		foreach (var sensor in hardware.Sensors)
		{
			if (sensor.SensorType != SensorType.Temperature)
			{
				continue;
			}

			if (!sensor.Value.HasValue)
			{
				continue;
			}

			var c = (double)sensor.Value.Value;
			var name = string.IsNullOrWhiteSpace(sensor.Name) ? "Temp" : sensor.Name;
			temps.Add((name, c, hardware.Name));
		}

		foreach (var sub in hardware.SubHardware)
		{
			CollectTempsRecursive(sub, temps);
		}
	}

	private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

	[StructLayout(LayoutKind.Sequential)]
	private struct FILETIME
	{
		public uint dwLowDateTime;
		public uint dwHighDateTime;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MEMORYSTATUSEX
	{
		public uint dwLength;
		public uint dwMemoryLoad;
		public ulong ullTotalPhys;
		public ulong ullAvailPhys;
		public ulong ullTotalPageFile;
		public ulong ullAvailPageFile;
		public ulong ullTotalVirtual;
		public ulong ullAvailVirtual;
		public ulong ullAvailExtendedVirtual;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}

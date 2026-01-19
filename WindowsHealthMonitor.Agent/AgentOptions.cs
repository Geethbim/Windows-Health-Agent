namespace WindowsHealthMonitor.Agent;

public sealed class AgentOptions
{
	public string ServerBaseUrl { get; init; } = "http://localhost:5080";
	public int PollSeconds { get; init; } = 10;
	public List<string> ServicesToMonitor { get; init; } = ["Spooler", "W32Time"]; 
}

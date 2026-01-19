using WindowsHealthMonitor.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient();

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

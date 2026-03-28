using Anthropic.SDK;
using ValorantBot;
using ValorantBot.Models;
using ValorantBot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuration bindings with validation
builder.Services
    .AddOptions<DiscordSettings>()
    .BindConfiguration("DiscordBot")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<HenrikDevSettings>()
    .BindConfiguration("HenrikDevValorantApi")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<List<TrackedPlayer>>(builder.Configuration.GetSection("TrackedPlayers"));

builder.Services
    .AddOptions<PollingSettings>()
    .BindConfiguration("Polling")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// HenrikDev typed HTTP client
var henrikSettings = builder.Configuration.GetSection("HenrikDevValorantApi").Get<HenrikDevSettings>()
    ?? new HenrikDevSettings();

builder.Services.AddHttpClient<IHenrikDevClient, HenrikDevClient>(client =>
{
    var baseUrl = henrikSettings.BaseUrl.TrimEnd('/') + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Authorization", henrikSettings.ApiKey);
    client.DefaultRequestHeaders.Add("User-Agent", "ValorantBot/1.0");
});

// Anthropic client for AI message generation
var anthropicApiKey = builder.Configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Anthropic:ApiKey is required in configuration");
builder.Services.AddSingleton(new AnthropicClient(anthropicApiKey));

// Services
builder.Services.AddSingleton<IMatchTracker, MatchTracker>();
builder.Services.AddSingleton<IPerformanceAnalyzer, PerformanceAnalyzer>();
builder.Services.AddSingleton<IMessageGenerator, MessageGenerator>();
builder.Services.AddSingleton<IDiscordNotifier, DiscordNotifier>();
builder.Services.AddScoped<IMatchService, MatchService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

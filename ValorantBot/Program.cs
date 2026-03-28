using Anthropic.SDK;
using ValorantBot;
using ValorantBot.Models;
using ValorantBot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuration bindings
builder.Services.Configure<DiscordSettings>(builder.Configuration.GetSection("DiscordBot"));
builder.Services.Configure<HenrikDevSettings>(builder.Configuration.GetSection("HenrikDevValorantApi"));
builder.Services.Configure<List<TrackedPlayer>>(builder.Configuration.GetSection("TrackedPlayers"));

// HenrikDev typed HTTP client
var henrikSettings = builder.Configuration.GetSection("HenrikDevValorantApi").Get<HenrikDevSettings>()
    ?? new HenrikDevSettings();

builder.Services.AddHttpClient<HenrikDevClient>(client =>
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
builder.Services.AddSingleton<MessageGenerator>();

// Services
builder.Services.AddSingleton<DiscordNotifier>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

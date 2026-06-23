using HorseRacingGame.Services;

var builder = WebApplication.CreateBuilder(args);

// Add Blazor Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add SignalR services
builder.Services.AddSignalR();

// Register GameService as a singleton
builder.Services.AddSingleton<GameService>();

// Configure Kestrel to listen on all interfaces at port 5000
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// Serve static files from wwwroot
app.UseStaticFiles();
app.UseAntiforgery();

// Map Blazor and SignalR
app.MapRazorComponents<HorseRacingGame.Components.App>()
    .AddInteractiveServerRenderMode();

app.MapHub<GameHub>("/gamehub");

app.Run();

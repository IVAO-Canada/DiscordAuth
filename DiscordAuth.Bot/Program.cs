using DiscordAuth.Bot;
using DiscordAuth.Database;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

Context.Register(builder);
builder.Services.AddActivatedSingleton<BotService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found");

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<DiscordAuth.Bot.Components.App>()
	.AddInteractiveServerRenderMode();

app.Run();

using DiscordAuth.Api;
using DiscordAuth.Database;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHttpClient();
Context.Register(builder);
builder.Services.AddActivatedSingleton<MigrationService>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseHttpsRedirection();

app.MapGet("/register/{divisionName}", Ivao.RedirectAsync);
app.MapGet("/ivao-callback", Ivao.CallbackAsync).WithName("ivao-callback");
app.MapGet("/discord", Discord.RedirectAsync).WithName("discord");
app.MapGet("/discord-callback", Discord.CallbackAsync).WithName("discord-callback");
app.MapGet("/success", () => Results.Ok("Success! You may close this tab.")).WithName("success");

app.MapGet("/add", () => Results.Redirect($"https://discord.com/oauth2/authorize?client_id={Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID")!}&permissions=402653185&integration_type=0&scope=bot"));

app.Run();

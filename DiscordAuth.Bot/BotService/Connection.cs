using Discord;
using Discord.WebSocket;

using DiscordAuth.Database;

using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Metrics;

using System.Diagnostics.CodeAnalysis;

namespace DiscordAuth.Bot;

internal partial class BotService
{
	public bool Connected => _discord.ConnectionState is ConnectionState.Connected;
	public Action Disconnect;

	readonly IDbContextFactory<Context> _dbFactory;
	readonly DiscordSocketClient _discord;
	readonly MeterProvider _meterProvider;
	CancellationToken _token;

	public BotService(IDbContextFactory<Context> dbFactory, HttpClient http, ILogger<BotService> logger, MeterProvider meterProvider, CancellationToken cancellation = default)
	{
		(_dbFactory, _http, _logger, _meterProvider) = (dbFactory, http, logger, meterProvider);
		_discord = new();
		SetupToken(cancellation);
		_ = Task.Run(StartAsync, _token);
	}

	[MemberNotNull(nameof(_token), nameof(Disconnect))]
	void SetupToken(CancellationToken cancellation)
	{
		CancellationTokenSource cts = new();
		_token = cts.Token;
		Disconnect = cts.Cancel;
		_discord.Disconnected += _ => _token.IsCancellationRequested ? Task.CompletedTask : cts.CancelAsync();
		_token.Register(async () => { if (!Connected) await _discord.StopAsync(); });

		// Chain cancellation through to one we control.
		if (cancellation.CanBeCanceled)
			cancellation.Register(cts.Cancel);
	}

	async Task StartAsync()
	{
		_discord.Log += LogAsync;
		_discord.Connected += ConnectedAsync;
		_discord.JoinedGuild += JoinedGuildAsync;
		_discord.LeftGuild += LeftGuildAsync;
		_discord.SlashCommandExecuted += CommandAsync;

		await _discord.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN")!);
		await _discord.StartAsync();
	}

	async Task ConnectedAsync()
	{
		await _discord.SetStatusAsync(UserStatus.Online);

		foreach (var guild in _discord.Guilds)
			await JoinedGuildAsync(guild);
	}
}

using Discord;

namespace DiscordAuth.Bot;

partial class BotService
{
	readonly ILogger<BotService> _logger;

	async Task LogAsync(LogMessage message)
	{
		LogLevel level = message.Severity switch {
			LogSeverity.Critical => LogLevel.Critical,
			LogSeverity.Error => LogLevel.Error,
			LogSeverity.Warning => LogLevel.Warning,
			LogSeverity.Verbose or LogSeverity.Debug => LogLevel.Debug,
			_ => LogLevel.Information,
		};

		if (!_logger.IsEnabled(level))
			return;

		_logger.Log(level, "{Message}", message.Message);
	}
}

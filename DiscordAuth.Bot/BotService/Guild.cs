using Discord;
using Discord.WebSocket;

using DiscordAuth.Database;
using DiscordAuth.ServiceDefaults;

namespace DiscordAuth.Bot;

partial class BotService
{
	async Task JoinedGuildAsync(SocketGuild guild)
	{
		Meters.GuildsActive.Add(1);

		// Add/update the /auth command.
		await guild.CreateApplicationCommandAsync(COMMAND);
	}

	private const string COMMAND_NAME = "auth", OPTION_NAME = "user";

	private readonly static SlashCommandProperties COMMAND = new SlashCommandBuilder()
			.WithName(COMMAND_NAME)
			.WithContextTypes(InteractionContextType.Guild)
			.AddOption(
				name: OPTION_NAME,
				type: ApplicationCommandOptionType.User,
				description: "The description of the user to re-auth. Must be a staff member if this isn't you.",
				isRequired: false
			)
			.WithDescription("Updates the current user, forcing authentication if not already complete.")
			.Build();

	async Task LeftGuildAsync(SocketGuild guild)
	{
		Meters.GuildsActive.Add(-1);
		var db = await _dbFactory.CreateDbContextAsync();

		if (await db.Divisions.FindAsync(guild.Id) is not DivisionGuild div)
		{
			if (_logger.IsEnabled(LogLevel.Warning))
				_logger.LogWarning("Removed from server {g} ({s}) despite not having a matching database record.", guild.Name, guild.Id);

			return;
		}

		if (_logger.IsEnabled(LogLevel.Information))
			_logger.LogInformation("Removed from the {g} division's server. Deleted corresponding database record.", div.Division);

		db.Divisions.Remove(div);
		await db.SaveChangesAsync();
	}

	async Task CommandAsync(SocketSlashCommand command)
	{
		await command.RespondAsync("Verifying…", ephemeral: true);

		SocketUser user = command.User;
		if (command.Data.Options.SingleOrDefault(static o => o.Name == OPTION_NAME)?.Value is SocketGuildUser targetUser && targetUser.Id != user.Id)
		{
			// Targeting someone else. Let's make sure they can do that…

			if (await VerifyAsync(targetUser, _discord.GetGuild(command.GuildId!.Value), user))
				await command.DeleteOriginalResponseAsync();
			else
				await command.ModifyOriginalResponseAsync(r => r.Content = "You are not authorized to perform this action. Please get someone else to do it.");
		}
		else
		{
			if (await VerifyAsync(user, _discord.GetGuild(command.GuildId!.Value)))
				await command.DeleteOriginalResponseAsync();
			else
				await command.ModifyOriginalResponseAsync(r => r.Content = "Authorization failed. Do you have higher permissions than the bot?");
		}
	}

	public string? GetMention(ulong guildId, ulong roleId) =>
		_discord.GetGuild(guildId)
		?.GetRole(roleId)
		?.Name is string mention ? $"@{mention}" : null;

	public int? GetUserCount(ulong guildId) =>
		_discord.GetGuild(guildId)
		?.MemberCount;
}

using Discord.WebSocket;

using DiscordAuth.Database;
using DiscordAuth.ServiceDefaults;

using Microsoft.EntityFrameworkCore;

namespace DiscordAuth.Bot;

partial class BotService
{
	private HttpClient _http;

	async Task<bool> VerifyAsync(SocketUser targetUser, SocketGuild guild, SocketUser? executingUser = null)
	{
		Meters.Authentications.Add(1);

		var db = await _dbFactory.CreateDbContextAsync();
		if (await db.Divisions.FindAsync(guild.Id) is not DivisionGuild division)
			return false;

		if (executingUser is not null && executingUser.Id != targetUser.Id)
		{
			// Verify user is a staff member in the appropriate division.
			if (executingUser is not SocketGuildUser sgu)
			{
				// Can't get a pin on them from the command. Figure it out with the database.
				if (await db.Users.FirstOrDefaultAsync(u => u.Snowflake == executingUser.Id) is not User staff)
					return false;

				if (!(await Api.Ivao.GetUserRolesAsync(_http, staff.Vid, division.Division)).HasFlag(Api.Ivao.Roles.DivStaff))
					return false;
			}
			else if (!sgu.Roles.Any(r => r.Id == division.StaffRole))
				// Role based check to allow divisions to set necessary roles manually.
				return false;
		}

		// Authorised! Execute the update.
		if (targetUser is not SocketGuildUser tsgu || await db.Users.FirstOrDefaultAsync(u => u.Snowflake == targetUser.Id) is not User user)
			return false;

		ulong[] roles = [.. Api.Discord.GetRoleSnowflakes(await Api.Ivao.GetUserRolesAsync(_http, user.Vid, division.Division), division)];
		string nick = await Api.Ivao.GetUserNicknameAsync(_http, user.Vid);
		string auditLogReason = "User authentication" + (executingUser is SocketGuildUser esgu ? $" by {esgu.Nickname}" : executingUser is SocketUser esu ? $" by {esu.Username}" : "");

		try
		{
			HttpRequestMessage req = new(HttpMethod.Patch, $"https://discord.com/api/guilds/{division.Snowflake}/members/{tsgu.Id}") {
				Content = JsonContent.Create(new
				{
					roles,
					nick
				})
			};
			req.Headers.Add("X-Audit-Log-Reason", auditLogReason);
			req.Headers.Authorization = new("Bot", Environment.GetEnvironmentVariable("DISCORD_TOKEN")!);
			var resp = await _http.SendAsync(req);

			if (!resp.IsSuccessStatusCode)
				throw new Exception($"Update failed: {await resp.Content.ReadAsStringAsync()}");

			if (_logger.IsEnabled(LogLevel.Information))
				_logger.LogInformation("Updated {nickname} in {guild} division", nick, division.Division);

			return true;
		}
		catch (Exception ex)
		{
			if (_logger.IsEnabled(LogLevel.Error))
				_logger.LogError(ex, "Failed to update {nickname} in {guild} division.", nick, division.Division);

			return false;
		}
	}
}

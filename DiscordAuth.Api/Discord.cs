using DiscordAuth.Database;

using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DiscordAuth.Api;

public static class Discord
{
	// https://localhost:7076/discord-callback?code=0ZLkEV9ynHs5GI43aa7EL65qUefoP3&state=644899
	internal static async Task<IResult> RedirectAsync(HttpContext ctx, LinkGenerator linker, [FromQuery] string vid, [FromQuery] string division, [FromQuery] string? redirect) => Results.Redirect(
		$"https://discord.com/oauth2/authorize" + new QueryBuilder() {
			{ "client_id", Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID")! },
			{ "response_type", "code" },
			{ "redirect_uri", linker.GetUriByName(ctx, "discord-callback")! },
			{ "scope", "identify guilds.join" },
			{ "state", vid + ":" + division + ":" + (redirect ?? "") },
			{ "prompt", "none" }
		}.ToQueryString().ToUriComponent()
	);

	internal static async Task<IResult> CallbackAsync(Context db, HttpClient http, HttpContext ctx, LinkGenerator linker, [FromQuery(Name = "state")] string state, [FromQuery] string code)
	{
		string[] stateParts = state.Split(':', 3, StringSplitOptions.TrimEntries);
		var (vid, division, redirect) = (stateParts[0], stateParts[1], stateParts[2]);
		DivisionGuild[] divisions = [.. db.Divisions.Where(d => d.Division == division)];

		if (await db.Users.FindAsync(vid) is not User user)
			return Results.BadRequest();

		var resp = await http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent([
			new("grant_type", "authorization_code"),
			new("code", code),
			new("redirect_uri", linker.GetUriByName(ctx, "discord-callback")!),
			new("client_id", Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID")!),
			new("client_secret", Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET")!)
		]));

		string token = (await resp.Content.ReadFromJsonAsync<TokenGrant>())!.access_token;

		HttpRequestMessage req = new(HttpMethod.Get, "https://discord.com/api/users/@me");
		req.Headers.Authorization = new("Bearer", token);
		resp = await http.SendAsync(req);
		user.Snowflake = ulong.Parse((await resp.Content.ReadFromJsonAsync<DiscordUser>())!.id);
		await db.SaveChangesAsync();

		foreach (var guild in divisions)
		{
			string[] roles = [.. GetRoleSnowflakes(await Ivao.GetUserRolesAsync(http, vid, guild.Division), guild).Select(s => s.ToString())];

			async Task<HttpResponseMessage> sendAsync(HttpMethod method)
			{
				req = new(method, $"https://discord.com/api/guilds/{guild.Snowflake}/members/{user.Snowflake}") {
					Content = JsonContent.Create(new
					{
						access_token = token,
						nick = await Ivao.GetUserNicknameAsync(http, vid),
						roles
					})
				};
				req.Headers.Authorization = new("Bot", Environment.GetEnvironmentVariable("DISCORD_TOKEN")!);
				req.Headers.Add("X-Audit-Log-Reason", "User authentication");
				resp = await http.SendAsync(req);
				return resp;
			}

			if ((await sendAsync(HttpMethod.Put)).StatusCode is System.Net.HttpStatusCode.NoContent)
				// Already there. Modify the user instead.
				await sendAsync(HttpMethod.Patch);
		}

		if (string.IsNullOrWhiteSpace(redirect))
			return Results.Redirect(linker.GetUriByName(ctx, "success") ?? "/");
		else
			return Results.Redirect(redirect);
	}

	public static IEnumerable<ulong> GetRoleSnowflakes(Ivao.Roles roles, DivisionGuild guild)
	{
		if (roles.HasFlag(Ivao.Roles.Member))
		{
			if (guild.MemberRole is ulong member)
				yield return member;
		}
		else if (roles.HasFlag(Ivao.Roles.GcaHolder) && guild.GcaRole is ulong gca)
			yield return gca;
		else if (guild.VisitorRole is ulong visitor)
			// Yield visitor for GCA holders if no GCA role is set.
			yield return visitor;

		if (roles.HasFlag(Ivao.Roles.HqStaff) && guild.HqStaffRole is ulong hqStaff)
			yield return hqStaff;

		if (roles.HasFlag(Ivao.Roles.DivStaff) && guild.StaffRole is ulong divStaff)
			yield return divStaff;
	}

#pragma warning disable IDE1006
	internal class TokenGrant
	{
		public string access_token { get; set; } = null!;
	}

	internal class DiscordUser
	{
		public string id { get; set; } = null!;
	}
#pragma warning restore
}

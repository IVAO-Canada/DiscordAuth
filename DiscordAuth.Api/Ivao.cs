using DiscordAuth.Database;
using DiscordAuth.ServiceDefaults;

using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using System.Buffers.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace DiscordAuth.Api;

public static class Ivao
{
	private static OidcWellKnown? _oidc;

	private static async Task<OidcWellKnown> GetOidcAsync(HttpClient http)
	{
		if (_oidc is not null)
			return _oidc;

		_oidc = await http.GetFromJsonAsync<OidcWellKnown>("https://api.ivao.aero/.well-known/openid-configuration");
		return _oidc ?? throw new Exception("Failed to parse IVAO SSO OIDC configuration.");
	}

	internal static async Task<IResult> RedirectAsync(Context db, HttpClient http, HttpContext ctx, LinkGenerator linker, string divisionName, [FromQuery] string? redirect = null)
	{
		Meters.Authentications.Add(1);
		divisionName = divisionName.ToUpperInvariant().Trim();

		if (await db.Divisions.FirstOrDefaultAsync(d => d.Division == divisionName) is not DivisionGuild division)
			return Results.NotFound($"Division {divisionName} could not be found. Please check your link.");

		OidcWellKnown oidc = await GetOidcAsync(http);
		string[] scopes = [
			..oidc.scopes_supported.Where(static s => s.Equals("openid", StringComparison.InvariantCultureIgnoreCase)),
			..oidc.scopes_supported.Where(static s => s.Equals("profile", StringComparison.InvariantCultureIgnoreCase)),
			..oidc.scopes_supported.Where(static s => s.Equals("discord", StringComparison.InvariantCultureIgnoreCase)),
		];

		QueryBuilder query = new() {
			{ "response_type", "code" },
			{ "client_id", Environment.GetEnvironmentVariable("IVAO_CLIENT_ID") ?? throw new Exception("Client ID environment variable not provided.") },
			{ "redirect_uri", linker.GetUriByName(ctx, "ivao-callback")! },
			{ "scope", string.Join(" ", scopes) },
			{ "state", divisionName + ":" + (redirect ?? "") },
		};

		return Results.Redirect(oidc.authorization_endpoint + query.ToQueryString().ToUriComponent());
	}

	internal static async Task<IResult> CallbackAsync(Context db, HttpClient http, [FromQuery] string state, [FromQuery(Name = "code")] string authCode)
	{
		string[] codeParts = authCode.Split('.');
		string[] jwtParts = [.. codeParts[..2].Select(s => Base64Url.DecodeFromChars(s)).Select(System.Text.Encoding.UTF8.GetString)];

		var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
			(await GetOidcAsync(http)).jwks_uri,
			new OpenIdConnectConfigurationRetriever(),
			new HttpDocumentRetriever()
		);

		var config = await configManager.GetConfigurationAsync();
		var key = ((JsonElement)config.AdditionalData["keys"])[0].Deserialize<JsonWebKey>();

		JwtSecurityTokenHandler handler = new();
		var validationResult = await handler.ValidateTokenAsync(authCode, new() {
			ValidateIssuer = true,
			ValidateIssuerSigningKey = true,
			ValidateAudience = false,
			ValidIssuer = "https://api.ivao.aero",
			IssuerSigningKey = key
		});

		if (!validationResult.IsValid)
			return Results.Unauthorized();

		string vid = (string)validationResult.Claims[validationResult.ClaimsIdentity.NameClaimType + "identifier"];

		if (await db.Users.FindAsync(vid) is User user)
			user.Snowflake = 0ul;
		else
			await db.Users.AddAsync(new() { Vid = vid, Snowflake = 0ul });

		await db.SaveChangesAsync();

		string[] stateParts = state.Split(':', 2, StringSplitOptions.TrimEntries);

		return Results.RedirectToRoute("discord", routeValues: new
		{
			vid,
			division = stateParts[0],
			redirect = stateParts[1]
		});
	}

	public static async Task<string> GetUserNicknameAsync(HttpClient http, string vid)
	{
		HttpRequestMessage req = new(HttpMethod.Get, $"https://api.ivao.aero/v2/users/{vid}");
		req.Headers.Add("X-API-KEY", Environment.GetEnvironmentVariable("IVAO_TOKEN")!);
		var resp = await http.SendAsync(req);
		var user = (await resp.Content.ReadFromJsonAsync<IvaoUser>())!;

		string suffix = $"- {user.id}";

		if (user.isStaff)
		{
			var positions = user.userStaffPositions.GroupBy(static pos => pos.divisionId ?? "HQ").ToDictionary(static g => g.Key, static g => g.ToArray());

			suffix = "|";

			if (positions.TryGetValue("HQ", out var hqPositions))
				foreach (var hqPos in hqPositions)
					suffix += $" {hqPos.staffPositionId}";

			foreach (var (div, divPositions) in positions.Where(static kvp => kvp.Key != "HQ"))
			{
				Userstaffposition[] divHqPositions = [.. divPositions.Where(static pos => pos.centerId is null)];
				Userstaffposition[] firPositions = [.. divPositions.Where(static pos => pos.centerId is not null)];

				if (divHqPositions.Length is not 0)
					suffix += $" {div}-{string.Join('/', divHqPositions.Select(static pos => pos.staffPositionId.TrimStart('-')))}";

				if (firPositions.Length is not 0)
					suffix += $" {string.Join(' ', firPositions.Select(static pos => pos.connectAs))}";
			}
		}

		return $"{user.nickname ?? user.firstName ?? user.publicNickname} {suffix}";
	}

	public static async Task<Roles> GetUserRolesAsync(HttpClient http, string vid, string division)
	{
		HttpRequestMessage req = new(HttpMethod.Get, $"https://api.ivao.aero/v2/users/{vid}");
		req.Headers.Add("X-API-KEY", Environment.GetEnvironmentVariable("IVAO_TOKEN")!);
		var resp = await http.SendAsync(req);
		var user = (await resp.Content.ReadFromJsonAsync<IvaoUser>())!;

		Roles retval = Roles.None;

		if (user.divisionId.Equals(division, StringComparison.InvariantCultureIgnoreCase))
			retval |= Roles.Member;

		if (user.gcas.Any(gca => gca.divisionId.Equals(division, StringComparison.InvariantCultureIgnoreCase)))
			retval |= Roles.GcaHolder;

		if (user.isStaff)
		{
			if (user.userStaffPositions.Any(pos => pos.divisionId?.Equals(division, StringComparison.InvariantCultureIgnoreCase) ?? false))
				retval |= Roles.DivStaff;

			if (user.userStaffPositions.Any(pos => pos.divisionId is null))
				retval |= Roles.HqStaff;
		}

		return retval;
	}

	public static async Task<DivisionInfo[]> GetDivisionsAsync(HttpClient http) =>
		await http.GetFromJsonAsync<DivisionInfo[]>("https://api.ivao.aero/v2/divisions/all?apiKey=" + Environment.GetEnvironmentVariable("IVAO_TOKEN")!)
		?? [];

	[Flags]
	public enum Roles
	{
		None = 0,
		Member = 1,
		DivStaff = 2,
		HqStaff = 4,
		GcaHolder = 8,
	}

#pragma warning disable IDE1006
	internal class OidcWellKnown
	{
		public string issuer { get; set; } = null!;
		public string authorization_endpoint { get; set; } = null!;
		public string token_endpoint { get; set; } = null!;
		public string userinfo_endpoint { get; set; } = null!;
		public string revocation_endpoint { get; set; } = null!;
		public string end_session_endpoint { get; set; } = null!;
		public string jwks_uri { get; set; } = null!;
		public string[] scopes_supported { get; set; } = [];
		public string[] response_types_supported { get; set; } = [];
		public string[] grant_types_supported { get; set; } = [];
		public string[] subject_types_supported { get; set; } = [];
		public string[] id_token_signing_alg_values_supported { get; set; } = [];
		public object[] id_token_encryption_alg_values_supported { get; set; } = [];
		public object[] id_token_encryption_enc_values_supported { get; set; } = [];
		public string[] token_endpoint_auth_methods_supported { get; set; } = [];
		public string[] token_endpoint_auth_signing_alg_values_supported { get; set; } = [];
		public string[] code_challenge_methods_supported { get; set; } = [];
		public bool claims_parameter_supported { get; set; }
		public bool request_parameter_supported { get; set; }
		public bool request_uri_parameter_supported { get; set; }
	}

#nullable disable
	public class IvaoUser
	{
		public int id { get; set; }
		public string firstName { get; set; }
		public string lastName { get; set; }
		public string centerId { get; set; }
		public string countryId { get; set; }
		public DateTime createdAt { get; set; }
		public string divisionId { get; set; }
		public bool isStaff { get; set; }
		public bool isSupervisor { get; set; }
		public string languageId { get; set; }
		public string email { get; set; }
		public Familyprofile familyProfile { get; set; }
		public Rating rating { get; set; }
		public Gca[] gcas { get; set; }
		public Hour[] hours { get; set; }
		public string profile { get; set; }
		public Userstaffposition[] userStaffPositions { get; set; }
		public Userstaffdetails userStaffDetails { get; set; }
		public object prCreator { get; set; }
		public object[] ownedVirtualAirlines { get; set; }
		public int sub { get; set; }
		public string given_name { get; set; }
		public string family_name { get; set; }
		public string nickname { get; set; }
		public string publicNickname { get; set; }
	}

	public class Familyprofile
	{
		public string discordUserId { get; set; }
	}

	public class Rating
	{
		public bool isPilot { get; set; }
		public bool isAtc { get; set; }
		public Pilotrating pilotRating { get; set; }
		public Atcrating atcRating { get; set; }
		public Networkrating networkRating { get; set; }
	}

	public class Pilotrating
	{
		public int id { get; set; }
		public string name { get; set; }
		public string shortName { get; set; }
		public string description { get; set; }
	}

	public class Atcrating
	{
		public int id { get; set; }
		public string name { get; set; }
		public string shortName { get; set; }
		public string description { get; set; }
	}

	public class Networkrating
	{
		public int id { get; set; }
		public string name { get; set; }
		public string description { get; set; }
	}

	public class Userstaffdetails
	{
		public string email { get; set; }
		public object note { get; set; }
		public object description { get; set; }
		public object remark { get; set; }
	}

	public class Gca
	{
		public string divisionId { get; set; }
	}

	public class Hour
	{
		public string type { get; set; }
		public int hours { get; set; }
	}

	public class Userstaffposition
	{
		public string id { get; set; }
		public string staffPositionId { get; set; }
		public string divisionId { get; set; }
		public object centerId { get; set; }
		public string connectAs { get; set; }
		public bool onTrial { get; set; }
		public object description { get; set; }
		public Staffposition staffPosition { get; set; }
	}

	public class Staffposition
	{
		public string id { get; set; }
		public string name { get; set; }
		public string type { get; set; }
		public Departmentteam departmentTeam { get; set; }
	}

	public class Departmentteam
	{
		public string id { get; set; }
		public string name { get; set; }
		public Department department { get; set; }
	}

	public class Department
	{
		public string id { get; set; }
		public string name { get; set; }
	}

	public class DivisionInfo
	{
		public string id { get; set; }
		public string name { get; set; }
		public string web { get; set; }
		public string mcd { get; set; }
		public int status { get; set; }
	}

#nullable restore
#pragma warning restore
}

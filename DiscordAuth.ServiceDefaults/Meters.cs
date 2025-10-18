using System.Diagnostics.Metrics;

namespace DiscordAuth.ServiceDefaults;

public static class Meters
{
	public const string METER_NAME = "DiscordAuth";

	public static Meter Meter { get; } = new(METER_NAME);

	public static UpDownCounter<int> GuildsActive { get; } = Meter.CreateUpDownCounter<int>("discordauth.guilds_active", description: "The number of Discord servers the bot is currently connected to.");
	public static Counter<int> Authentications { get; } = Meter.CreateCounter<int>("discordauth.authentications", description: "The number of authentication actions attempted.");
}

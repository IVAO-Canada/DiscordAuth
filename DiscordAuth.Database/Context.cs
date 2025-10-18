using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DiscordAuth.Database;

public class Context(DbContextOptions<Context> options) : DbContext(options)
{
	public static void Register(IHostApplicationBuilder builder)
	{
		builder.AddNpgsqlDbContext<Context>(
			connectionName: "discord",
			configureDbContextOptions: options => options.EnableDetailedErrors()
		);

		builder.EnrichNpgsqlDbContext<Context>();
		builder.Services.AddDbContextFactory<Context>();
	}

	public DbSet<DivisionGuild> Divisions { get; set; }
	public DbSet<User> Users { get; set; }
}

[PrimaryKey(nameof(Snowflake))]
public class DivisionGuild
{
	public ulong Snowflake { get; set; }
	public string Division { get; set; } = null!;

	public ulong? MemberRole { get; set; } = null;
	public ulong? StaffRole { get; set; } = null;
	public ulong? HqStaffRole { get; set; } = null;
	public ulong? GcaRole { get; set; } = null;
	public ulong? VisitorRole { get; set; } = null;
}

[PrimaryKey(nameof(Vid))]
public class User
{
	public string Vid { get; set; } = null!;
	public ulong Snowflake { get; set; }
}

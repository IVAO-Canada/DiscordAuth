using DiscordAuth.Database;

using Microsoft.EntityFrameworkCore;

namespace DiscordAuth.Api;

internal class MigrationService
{
	public MigrationService(IDbContextFactory<Context> factory)
	{
		var db = factory.CreateDbContext();
		db.Database.Migrate();
	}
}

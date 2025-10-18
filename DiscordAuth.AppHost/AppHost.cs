var builder = DistributedApplication.CreateBuilder(args);
builder.AddDockerComposeEnvironment("compose")
	.WithDashboard()
	.WithProperties(env =>
	{
		env.DefaultNetworkName = "discord-auth-net";
		env.DashboardEnabled = true;
		env.BuildContainerImages = false;
	});

var pgUsername = builder.AddParameter("pg-username", "postgres", publishValueAsDefault: true, secret: false);
var pgPassword = builder.AddParameter("pg-password", true);

var postgres = builder
	.AddPostgres("postgres")
	.WithVolume("postgres-data", "/var/lib/postgresql/data", isReadOnly: false)
	.WithUserName(pgUsername)
	.WithPassword(pgPassword)
	.PublishAsDockerComposeService((resource, service) =>
	{
		service.Name = "postgres";
		service.ContainerName = "postgres";
		service.Expose.Add("5432");
	});

var discordDb = postgres.AddDatabase("discord");

var ivaoId = builder.AddParameter("ivao-client-id", secret: false);
var ivaoSecret = builder.AddParameter("ivao-client-secret", secret: true);
var ivaoToken = builder.AddParameter("ivao-token", secret: true);
var discordId = builder.AddParameter("discord-client-id", secret: false);
var discordSecret = builder.AddParameter("discord-client-secret", secret: true);
var discordToken = builder.AddParameter("discord-token", secret: true);

var api = builder.AddProject<Projects.DiscordAuth_Api>("api")
	.WithReference(discordDb)
	.WaitFor(discordDb)
	.WithExternalHttpEndpoints()
	.WithEnvironment("IVAO_CLIENT_ID", ivaoId)
	.WithEnvironment("IVAO_CLIENT_SECRET", ivaoSecret)
	.WithEnvironment("IVAO_TOKEN", ivaoToken)
	.WithEnvironment("DISCORD_CLIENT_ID", discordId)
	.WithEnvironment("DISCORD_CLIENT_SECRET", discordSecret)
	.WithEnvironment("DISCORD_TOKEN", discordToken)
	.PublishAsDockerComposeService((resource, service) =>
	{
		service.Name = "api";
	});

builder.AddProject<Projects.DiscordAuth_Bot>("bot")
	.WithReference(discordDb)
	.WaitFor(discordDb)
	.WithReference(api)
	.WithExternalHttpEndpoints()
	.WithEnvironment("IVAO_TOKEN", ivaoToken)
	.WithEnvironment("DISCORD_TOKEN", discordToken)
	.PublishAsDockerComposeService((resource, service) =>
	{
		service.Name = "bot";
	});

builder.Build().Run();

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using OpenIddict.Abstractions;
using Resgrid.Config;
using Resgrid.Web.Services.Helpers;
using AuthorizationDbContext = Resgrid.Web.Services.Models.AuthorizationDbContext;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The apps' OAuth clients (omni-plan section 8.6 item 5) as the API registers them at start, against OpenIddict's
	/// Entity Framework stores. LoginMfaTransactionApiTests covers what the token endpoint then does with them.
	/// </summary>
	[TestFixture]
	public class ResgridAppClientsTests
	{
		private static readonly string[] ExpectedPermissions =
		{
			"ept:token",
			"gt:password",
			"gt:refresh_token",
			"gt:urn:resgrid:params:oauth:grant-type:mfa_completion",
			"gt:urn:resgrid:params:oauth:grant-type:external_token",
			"scp:email",
			"scp:profile",
			"scp:mobile"
		};

		private static ServiceProvider Services()
		{
			var database = "oidc-" + Guid.NewGuid().ToString("N");
			var services = new ServiceCollection();
			services.AddLogging();
			services.AddDbContext<AuthorizationDbContext>(options =>
			{
				options.UseInMemoryDatabase(database);
				options.ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
				options.UseOpenIddict<Guid>();
			});
			services.AddOpenIddict().AddCore(options => options.UseEntityFrameworkCore()
				.UseDbContext<AuthorizationDbContext>()
				.ReplaceDefaultEntities<Guid>());
			return services.BuildServiceProvider();
		}

		private static async Task<string[]> PermissionsOf(IServiceProvider services, string clientId)
		{
			using var scope = services.CreateScope();
			var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
			var application = await manager.FindByClientIdAsync(clientId);
			application.Should().NotBeNull(clientId);
			return (await manager.GetPermissionsAsync(application)).ToArray();
		}

		[Test]
		public async Task Registering_on_every_start_leaves_one_public_client_per_app_allowed_only_the_app_grants()
		{
			await using var services = Services();
			await ResgridAppClients.RegisterAsync(services);
			await ResgridAppClients.RegisterAsync(services);

			using var scope = services.CreateScope();
			var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
			(await manager.CountAsync()).Should().Be(5);
			foreach (var clientId in new[] { "responder", "unit", "ic", "dispatch", "bigboard" })
			{
				var application = await manager.FindByClientIdAsync(clientId);
				application.Should().NotBeNull(clientId);
				(await manager.HasClientTypeAsync(application, OpenIddictConstants.ClientTypes.Public)).Should().BeTrue("an app cannot keep a secret");
				(await manager.GetPermissionsAsync(application)).Should().BeEquivalentTo(ExpectedPermissions,
					"the token endpoint and the grants the apps use, and never client_credentials");
			}
		}

		[Test]
		public async Task A_client_whose_permissions_drifted_is_brought_back_in_line_on_the_next_start()
		{
			await using var services = Services();
			await ResgridAppClients.RegisterAsync(services);

			using (var scope = services.CreateScope())
			{
				var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
				var application = await manager.FindByClientIdAsync(ResgridAppClients.Unit);
				var descriptor = new OpenIddictApplicationDescriptor();
				await manager.PopulateAsync(descriptor, application);
				descriptor.Permissions.Remove("gt:urn:resgrid:params:oauth:grant-type:external_token");
				await manager.UpdateAsync(application, descriptor);
			}

			(await PermissionsOf(services, ResgridAppClients.Unit)).Should().NotContain("gt:urn:resgrid:params:oauth:grant-type:external_token");

			await ResgridAppClients.RegisterAsync(services);

			(await PermissionsOf(services, ResgridAppClients.Unit)).Should().BeEquivalentTo(ExpectedPermissions);
		}

		[Test]
		public async Task The_api_start_registers_the_app_clients_beside_the_events_client()
		{
			var secret = JwtConfig.EventsClientSecret;
			JwtConfig.EventsClientSecret = "events-secret";
			try
			{
				await using var services = Services();
				await new Resgrid.Web.Services.Worker(services).StartAsync(CancellationToken.None);

				using var scope = services.CreateScope();
				var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
				(await manager.FindByClientIdAsync(JwtConfig.EventsClientId)).Should().NotBeNull();
				foreach (var clientId in ResgridAppClients.ClientIds)
					(await manager.FindByClientIdAsync(clientId)).Should().NotBeNull(clientId);
			}
			finally
			{
				JwtConfig.EventsClientSecret = secret;
			}
		}

		[Test]
		public void The_shipped_long_lived_client_ids_are_the_registered_app_clients()
		{
			OidcConfig.TrustedLongLivedClientIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Should().BeEquivalentTo(ResgridAppClients.ClientIds);
		}
	}
}

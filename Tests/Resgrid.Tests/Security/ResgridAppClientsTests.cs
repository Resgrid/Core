using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
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

		/// <param name="manager">Stands in for OpenIddict's application manager; otherwise its Entity Framework stores.</param>
		private static ServiceProvider Services(IOpenIddictApplicationManager manager = null)
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
			if (manager != null)
				services.AddSingleton(manager);
			return services.BuildServiceProvider();
		}

		private static async Task<string> ConsoleOutputOf(Func<Task> action)
		{
			var originalOut = Console.Out;
			using var writer = new StringWriter();
			Console.SetOut(writer);
			try
			{
				await action();
			}
			finally
			{
				Console.SetOut(originalOut);
			}

			return writer.ToString();
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
		public async Task The_api_start_hands_its_cancellation_token_to_every_client_registration()
		{
			using var startup = new CancellationTokenSource();
			var lookups = new List<(string ClientId, CancellationToken Token)>();
			var creates = new List<(string ClientId, CancellationToken Token)>();
			var manager = new Mock<IOpenIddictApplicationManager>();
			manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns((string clientId, CancellationToken token) =>
				{
					lookups.Add((clientId, token));
					return new ValueTask<object>(clientId == JwtConfig.EventsClientId ? new object() : null);
				});
			manager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
				.Returns((OpenIddictApplicationDescriptor descriptor, CancellationToken token) =>
				{
					creates.Add((descriptor.ClientId, token));
					return new ValueTask<object>(new object());
				});

			await using var services = Services(manager.Object);
			await new Resgrid.Web.Services.Worker(services).StartAsync(startup.Token);

			lookups.Select(l => l.ClientId).Should().Contain(ResgridAppClients.ClientIds);
			creates.Select(c => c.ClientId).Should().BeEquivalentTo(ResgridAppClients.ClientIds);
			lookups.Concat(creates).Should().OnlyContain(call => call.Token == startup.Token,
				"a start cancelled part way through must not go on through the remaining clients");
		}

		[Test]
		public async Task A_client_that_cannot_be_written_is_logged_with_the_write_failure_as_its_cause()
		{
			var manager = new Mock<IOpenIddictApplicationManager>();
			manager.Setup(m => m.FindByClientIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Returns(new ValueTask<object>((object)null));
			manager.Setup(m => m.CreateAsync(It.IsAny<OpenIddictApplicationDescriptor>(), It.IsAny<CancellationToken>()))
				.Returns(() => ValueTask.FromException<object>(new InvalidOperationException("The application store is unavailable (test).")));

			await using var services = Services(manager.Object);
			var output = await ConsoleOutputOf(() => ResgridAppClients.RegisterAsync(services));

			foreach (var clientId in ResgridAppClients.ClientIds)
				output.Should().Contain($"The OAuth client '{clientId}' was not registered as described.");
			output.Should().Contain("The application store is unavailable (test).", "the store's own failure is why the client is missing");
			output.Should().Contain("invalid_client", "until a later start registers it, the token endpoint refuses its client_id");
		}

		[Test]
		public void The_shipped_long_lived_client_ids_are_the_registered_app_clients()
		{
			OidcConfig.TrustedLongLivedClientIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Should().BeEquivalentTo(ResgridAppClients.ClientIds);
		}
	}
}

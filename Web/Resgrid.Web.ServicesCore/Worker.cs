using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Threading;
using System;
using System.Threading.Tasks;
using Resgrid.Config;
using Microsoft.Extensions.DependencyInjection;
using Resgrid.Web.Services.Models;

namespace Resgrid.Web.Services
{
	public class Worker : IHostedService
	{
		private readonly IServiceProvider _serviceProvider;

		public Worker(IServiceProvider serviceProvider)
		=> _serviceProvider = serviceProvider;

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			using var scope = _serviceProvider.CreateScope();

			var context = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
			await context.Database.EnsureCreatedAsync(cancellationToken);

			await RegisterApplicationsAsync(scope.ServiceProvider);
			await RegisterScopesAsync(scope.ServiceProvider);

			static async Task RegisterApplicationsAsync(IServiceProvider provider)
			{
				var manager = provider.GetRequiredService<IOpenIddictApplicationManager>();

				// Events Web API
				if (await manager.FindByClientIdAsync(JwtConfig.EventsClientId) == null)
				{
					await manager.CreateAsync(new OpenIddictApplicationDescriptor
					{
						ClientId = JwtConfig.EventsClientId,
						ClientSecret = JwtConfig.EventsClientSecret,
						DisplayName = "Events API Client",
						Permissions =
						{
							Permissions.Endpoints.Introspection
						}
					});
				}

				//// Blazor Hosted
				//if (await manager.FindByClientIdAsync("blazorcodeflowpkceclient") is null)
				//{
				//	await manager.CreateAsync(new OpenIddictApplicationDescriptor
				//	{
				//		ClientId = "blazorcodeflowpkceclient",
				//		ConsentType = ConsentTypes.Explicit,
				//		DisplayName = "Blazor code PKCE",
				//		DisplayNames =
				//		{
				//			[CultureInfo.GetCultureInfo("fr-FR")] = "Application cliente MVC"
				//		},
				//		PostLogoutRedirectUris =
				//		{
				//			new Uri("https://localhost:44348/signout-callback-oidc"),
				//			new Uri("https://localhost:5001/signout-callback-oidc")
				//		},
				//		RedirectUris =
				//		{
				//			new Uri("https://localhost:44348/signin-oidc"),
				//			new Uri("https://localhost:5001/signin-oidc")
				//		},
				//		ClientSecret = "codeflow_pkce_client_secret",
				//		Permissions =
				//		{
				//			Permissions.Endpoints.Authorization,
				//			Permissions.Endpoints.Logout,
				//			Permissions.Endpoints.Token,
				//			Permissions.Endpoints.Revocation,
				//			Permissions.GrantTypes.AuthorizationCode,
				//			Permissions.GrantTypes.RefreshToken,
				//			Permissions.ResponseTypes.Code,
				//			Permissions.Scopes.Email,
				//			Permissions.Scopes.Profile,
				//			Permissions.Scopes.Roles,
				//			Permissions.Prefixes.Scope + "dataEventRecords"
				//		},
				//		Requirements =
				//		{
				//			Requirements.Features.ProofKeyForCodeExchange
				//		}
				//	});
				//}
			}

			static async Task RegisterScopesAsync(IServiceProvider provider)
			{
				//var manager = provider.GetRequiredService<IOpenIddictScopeManager>();

				//if (await manager.FindByNameAsync("dataEventRecords") is null)
				//{
				//	await manager.CreateAsync(new OpenIddictScopeDescriptor
				//	{
				//		DisplayName = "dataEventRecords API access",
				//		DisplayNames =
				//		{
				//			[CultureInfo.GetCultureInfo("fr-FR")] = "Accès à l'API de démo"
				//		},
				//		Name = "dataEventRecords",
				//		Resources =
				//		{
				//			"rs_dataEventRecordsApi"
				//		}
				//	});
				//}
			}
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}
}

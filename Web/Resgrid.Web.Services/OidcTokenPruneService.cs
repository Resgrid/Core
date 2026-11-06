using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Resgrid.Config;

namespace Resgrid.Web.Services
{
	/// <summary>
	/// Prunes the OIDC store every <c>OidcConfig.TokenPruneIntervalMinutes</c>: tokens that are expired, revoked or redeemed,
	/// and authorizations left with no valid token. Nothing pruned them before, and a reference access token's row also
	/// holds the whole token. Every API instance runs this; the deletes are idempotent, so overlapping passes only repeat work.
	/// </summary>
	public sealed class OidcTokenPruneService : BackgroundService
	{
		private readonly IServiceScopeFactory _scopes;

		public OidcTokenPruneService(IServiceScopeFactory scopes)
		{
			_scopes = scopes;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (OidcConfig.TokenPruneIntervalMinutes <= 0)
				return;

			// Instances started by the same deploy would otherwise all prune at once.
			await Task.Delay(TimeSpan.FromMinutes(Random.Shared.Next(1, 16)), stoppingToken);

			using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, OidcConfig.TokenPruneIntervalMinutes)));
			do
			{
				try
				{
					using var scope = _scopes.CreateScope();
					await PruneAsync(scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>(),
						scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>(), DateTimeOffset.UtcNow, stoppingToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// The next tick tries again; OpenIddict deletes in batches, so whatever this pass removed stays removed.
					Resgrid.Framework.Logging.LogException(ex, "Pruning the OIDC token store failed.");
				}
			} while (await timer.WaitForNextTickAsync(stoppingToken));
		}

		/// <summary>
		/// One pass. Only entries older than <c>OidcConfig.TokenPruneMinimumAgeDays</c> are considered, and OpenIddict never
		/// removes a valid, unexpired token or an authorization that still has one.
		/// </summary>
		public static async Task PruneAsync(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations,
			DateTimeOffset now, CancellationToken cancellationToken)
		{
			var threshold = now.AddDays(-Math.Max(1, OidcConfig.TokenPruneMinimumAgeDays));

			await tokens.PruneAsync(threshold, cancellationToken);
			await authorizations.PruneAsync(threshold, cancellationToken);
		}
	}
}

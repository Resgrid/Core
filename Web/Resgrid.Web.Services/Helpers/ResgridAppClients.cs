using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Resgrid.Model.Security;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// The OAuth clients of the Resgrid apps (omni-plan section 8.6 item 5). Each app sends its own <c>client_id</c> on the
	/// token grants. OpenIddict accepts a request with no <c>client_id</c> as anonymous, but answers <c>invalid_client</c> to
	/// one it cannot find in its application store, so every id the apps send has to be registered here.
	/// </summary>
	/// <remarks>
	/// These are public clients: an app cannot keep a secret, so a <c>client_id</c> says which app is calling, not who is.
	/// Registering one limits what a caller using it may do (the token endpoint and the grants below, never
	/// client_credentials); it proves nothing about the caller.
	/// </remarks>
	public static class ResgridAppClients
	{
		public const string Responder = "responder";
		public const string Unit = "unit";
		public const string IncidentCommand = "ic";
		public const string Dispatch = "dispatch";
		public const string BigBoard = "bigboard";

		/// <summary>Every app client id, with the name shown for it in the OpenIddict store.</summary>
		public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[Responder] = "Resgrid Responder app",
			[Unit] = "Resgrid Unit app",
			[IncidentCommand] = "Resgrid IC app",
			[Dispatch] = "Resgrid Dispatch app",
			[BigBoard] = "Resgrid BigBoard app"
		};

		public static IEnumerable<string> ClientIds => DisplayNames.Keys;

		/// <summary>
		/// What the apps do at the token endpoints (both token paths are the one OpenIddict token endpoint): sign in with a
		/// password, refresh, complete a login MFA transaction, and exchange a legacy SSO token. The openid and
		/// offline_access scopes are protocol scopes OpenIddict does not check against permissions (offline_access follows
		/// the refresh_token grant permission), so only the other scopes the apps request are listed.
		/// </summary>
		public static readonly IReadOnlyList<string> ClientPermissions = new[]
		{
			OpenIddictConstants.Permissions.Endpoints.Token,
			OpenIddictConstants.Permissions.GrantTypes.Password,
			OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
			OpenIddictConstants.Permissions.Prefixes.GrantType + MfaLoginTransactions.CompletionGrantType,
			OpenIddictConstants.Permissions.Prefixes.GrantType + ResgridTokenEndpoints.ExternalTokenGrantType,
			OpenIddictConstants.Permissions.Scopes.Email,
			OpenIddictConstants.Permissions.Scopes.Profile,
			OpenIddictConstants.Permissions.Prefixes.Scope + "mobile"
		};

		public static OpenIddictApplicationDescriptor Describe(string clientId)
		{
			var descriptor = new OpenIddictApplicationDescriptor
			{
				ClientId = clientId,
				ClientType = OpenIddictConstants.ClientTypes.Public,
				DisplayName = DisplayNames[clientId]
			};
			descriptor.Permissions.UnionWith(ClientPermissions);
			return descriptor;
		}

		/// <summary>
		/// Registers each app client that is missing, and brings back in line one whose type or permissions differ from
		/// <see cref="Describe"/>, so a permission added here reaches stores registered earlier. Safe on every start and on
		/// several instances at once: when another instance created or updated the same client first, that is kept. Each
		/// client gets its own scope, so a failed write leaves nothing tracked for the next. A client that still cannot be
		/// registered is logged and skipped: its app goes on signing in without a client_id, as before.
		/// </summary>
		public static async Task RegisterAsync(IServiceProvider services, CancellationToken cancellationToken = default)
		{
			foreach (var clientId in ClientIds)
			{
				try
				{
					await RegisterAsync(services, clientId, cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Resgrid.Framework.Logging.LogException(ex,
						$"Could not register the OAuth client '{clientId}'. Its app's token requests go on without a client_id, with the short refresh-token lifetime.");
				}
			}
		}

		private static async Task RegisterAsync(IServiceProvider services, string clientId, CancellationToken cancellationToken)
		{
			var descriptor = Describe(clientId);
			using (var scope = services.CreateScope())
			{
				var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
				var existing = await manager.FindByClientIdAsync(clientId, cancellationToken);
				try
				{
					if (existing == null)
						await manager.CreateAsync(descriptor, cancellationToken);
					else if (!await MatchesAsync(manager, existing, descriptor, cancellationToken))
						await manager.UpdateAsync(existing, descriptor, cancellationToken);
					return;
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// Possibly another instance writing the same client: settled below, from a clean scope.
					if (existing != null && ex is not OpenIddictExceptions.ConcurrencyException)
						throw;
				}
			}

			using (var scope = services.CreateScope())
			{
				var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
				var current = await manager.FindByClientIdAsync(clientId, cancellationToken);
				if (current == null || !await MatchesAsync(manager, current, descriptor, cancellationToken))
					throw new InvalidOperationException($"The OAuth client '{clientId}' was not registered as described.");
			}
		}

		private static async Task<bool> MatchesAsync(IOpenIddictApplicationManager manager, object application,
			OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
		{
			if (!await manager.HasClientTypeAsync(application, descriptor.ClientType, cancellationToken))
				return false;

			var permissions = await manager.GetPermissionsAsync(application, cancellationToken);
			return permissions.ToHashSet(StringComparer.Ordinal).SetEquals(descriptor.Permissions);
		}
	}
}

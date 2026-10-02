using System;
using Microsoft.AspNetCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using Resgrid.Model.Security;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// The OpenIddict token endpoints and grants the API serves, shared by Startup and the HTTP tests so both run the same
	/// configuration.
	/// </summary>
	public static class ResgridTokenEndpoints
	{
		public const string TokenPath = "/api/v4/connect/token";

		/// <summary>The legacy SSO exchange (plan section 7.7.4), which answers with a token response like the token endpoint.</summary>
		public const string ExternalTokenPath = "/api/v4/connect/external-token";

		/// <summary>
		/// The grant every request to <see cref="ExternalTokenPath"/> carries. App builds post there without a
		/// <c>grant_type</c>, so the server supplies it; the route only ever performs the SSO exchange.
		/// </summary>
		public const string ExternalTokenGrantType = "urn:resgrid:params:oauth:grant-type:external_token";

		public static OpenIddictServerBuilder UseResgridTokenEndpoints(this OpenIddictServerBuilder options)
		{
			options.RegisterScopes(OpenIddict.Abstractions.OpenIddictConstants.Scopes.Profile, OpenIddict.Abstractions.OpenIddictConstants.Scopes.Email,
				OpenIddict.Abstractions.OpenIddictConstants.Scopes.OfflineAccess, "mobile", "web");

			// The legacy exchange signs in through OpenIddict, which only issues tokens from a registered token endpoint.
			// Without it every exchange that passed its checks ended in a 500 (workbook section 12, slice 9).
			options.SetTokenEndpointUris(TokenPath, ExternalTokenPath);

			options.AllowClientCredentialsFlow()
				.AllowPasswordFlow()
				.AllowRefreshTokenFlow()
				.AllowCustomFlow("web_session")
				// One-use completion of a login MFA transaction (passkey workbook section 7.1).
				.AllowCustomFlow(MfaLoginTransactions.CompletionGrantType)
				.AllowCustomFlow(ExternalTokenGrantType);

			options.AddEventHandler<OpenIddictServerEvents.ExtractTokenRequestContext>(handler => handler
				.UseInlineHandler(context =>
				{
					// Whatever grant_type a caller sent here is replaced: this path never refreshes, never takes a password,
					// and never completes an MFA transaction, so nothing else is validated or honored on it.
					var path = context.Transaction.GetHttpRequest()?.Path;
					if (context.Request != null && path.HasValue &&
						string.Equals(path.Value.Value?.TrimEnd('/'), ExternalTokenPath, StringComparison.OrdinalIgnoreCase))
						context.Request.GrantType = ExternalTokenGrantType;
					return default;
				})
				.SetOrder(OpenIddictServerAspNetCoreHandlers.ExtractPostRequest<OpenIddictServerEvents.ExtractTokenRequestContext>.Descriptor.Order + 1_000)
				.SetType(OpenIddictServerHandlerType.Custom));

			return options;
		}
	}
}

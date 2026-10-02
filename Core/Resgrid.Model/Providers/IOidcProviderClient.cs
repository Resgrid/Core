using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// The HTTP side of brokered OIDC (passkey plan section 7.7.2): the IdP's discovery document and signing keys, and the
	/// authorization-code exchange. Every URL must be https. Validation of what comes back is the caller's job.
	/// </summary>
	public interface IOidcProviderClient
	{
		/// <summary>The IdP's metadata, cached; <paramref name="forceRefresh"/> refetches after a key rotation.</summary>
		Task<OidcProviderMetadata> GetMetadataAsync(string authority, bool forceRefresh = false, CancellationToken cancellationToken = default);

		/// <summary>Posts the code exchange form to the token endpoint and returns the id_token, or the IdP's error.</summary>
		Task<OidcCodeExchangeResult> ExchangeCodeAsync(string tokenEndpoint, IReadOnlyDictionary<string, string> form,
			CancellationToken cancellationToken = default);
	}

	public sealed class OidcProviderMetadata
	{
		public string Issuer { get; init; }
		public string AuthorizationEndpoint { get; init; }
		public string TokenEndpoint { get; init; }

		/// <summary>The IdP's JSON Web Key Set, as published.</summary>
		public string JwksJson { get; init; }
	}

	public sealed class OidcCodeExchangeResult
	{
		public string IdToken { get; init; }
		public string Error { get; init; }
	}
}

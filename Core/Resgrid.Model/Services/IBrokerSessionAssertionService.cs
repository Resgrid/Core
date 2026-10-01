using System;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Mints and validates broker session assertions (passkey workbook section 6.2) with a dedicated ES256 certificate
	/// that is neither the grant key nor an OpenIddict key. Web and API hold the private key and mint; the broker holds
	/// the public certificate and validates. Missing key material means "cannot mint" or "cannot validate", never success.
	/// </summary>
	public interface IBrokerSessionAssertionService
	{
		bool CanMint { get; }

		bool CanValidate { get; }

		/// <summary>
		/// Signs an assertion for the given facts. The service sets the id, issue time and expiry; the caller supplies the
		/// user, session, generation, department, client, lock version and request digest.
		/// </summary>
		string Mint(BrokerSessionAssertion facts);

		/// <summary>
		/// Verifies signature, issuer, audience, lifetime and the request digest, and returns the asserted facts. Anything
		/// but <see cref="BrokerSessionAssertionOutcome.Valid"/> must deny the request.
		/// </summary>
		BrokerSessionAssertionOutcome Validate(string token, string expectedRequestDigest, out BrokerSessionAssertion assertion,
			DateTime? utcNow = null);
	}
}

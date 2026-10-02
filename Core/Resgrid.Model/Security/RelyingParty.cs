using System.Collections.Generic;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// One client's WebAuthn relying party (workbook section 5). Every client has its own RP ID, which is what binds a
	/// passkey to the app that registered it (plan section 1.1 item 11).
	/// </summary>
	public sealed class RelyingPartyDescriptor
	{
		public UserSessionClientApplication ClientApplication { get; init; }

		public string RpId { get; init; }

		/// <summary>Exact allowed origins: https origins under the RP ID and Android apk-key-hash origins.</summary>
		public IReadOnlyCollection<string> Origins { get; init; }
	}

	/// <summary>Value-free readiness of the passkey configuration: problems name the client and rule, never secrets.</summary>
	public sealed class PasskeyReadiness
	{
		public bool IsReady { get; init; }

		public IReadOnlyList<string> Problems { get; init; }
	}
}

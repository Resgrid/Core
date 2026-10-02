using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Services.Models.v4.Passkeys
{
	/// <summary>A started WebAuthn ceremony (workbook section 7.1): the request id to send back and the options for the platform.</summary>
	public class PasskeyCeremonyResult : StandardApiResponseV4Base
	{
		public PasskeyCeremonyResultData Data { get; set; }
	}

	public class PasskeyCeremonyResultData
	{
		public string RequestId { get; set; }

		/// <summary>WebAuthn PublicKeyCredentialCreationOptions (registration) or PublicKeyCredentialRequestOptions (assertion) JSON.</summary>
		public JRaw Options { get; set; }
	}

	public class CompletePasskeyRegistrationInput
	{
		public string RequestId { get; set; }

		/// <summary>The platform's response: PublicKeyCredential.toJSON(), as an object or a JSON string.</summary>
		public JToken Credential { get; set; }

		/// <summary>Optional name for the passkey; at most 100 characters. A default naming the app is used when empty.</summary>
		public string DisplayName { get; set; }
	}

	public class PasskeyResult : StandardApiResponseV4Base
	{
		public PasskeyResultData Data { get; set; }
	}

	/// <summary>
	/// One passkey in the user's own inventory (plan section 6.5). Registration context, attachment and backup state are
	/// server-observed or client-reported hints, not proof of a device.
	/// </summary>
	public class PasskeyResultData
	{
		public string PasskeyId { get; set; }
		public string DisplayName { get; set; }

		/// <summary>The app the passkey works in: web, responder, unit, dispatch or ic.</summary>
		public string Client { get; set; }

		public string CreatedOn { get; set; }
		public string CreatedPlatform { get; set; }
		public string CreatedInstallation { get; set; }
		public string CreatedUserAgentFamily { get; set; }
		public bool CreatedOnSharedInstallation { get; set; }
		public string Attachment { get; set; }
		public bool BackupEligible { get; set; }
		public bool BackedUp { get; set; }
		public string LastUsedOn { get; set; }
		public string LastUsedClient { get; set; }
		public string LastUsedInstallation { get; set; }

		/// <summary>Responder passkeys only: whether it may approve other apps' requests. Null for other apps.</summary>
		public bool? ApprovalEnabled { get; set; }
	}

	public class PasskeyListResult : StandardApiResponseV4Base
	{
		public List<PasskeyResultData> Data { get; set; }
	}

	public class RenamePasskeyInput
	{
		public string PasskeyId { get; set; }
		public string DisplayName { get; set; }
	}

	public class RevokePasskeyInput
	{
		public string PasskeyId { get; set; }
	}

	public class RevokeAllPasskeysForClientInput
	{
		/// <summary>web, responder, unit, dispatch or ic.</summary>
		public string Client { get; set; }
	}

	public class SetPasskeyApprovalInput
	{
		public string PasskeyId { get; set; }
		public bool Enabled { get; set; }
	}

	public class PasskeyChangeResult : StandardApiResponseV4Base
	{
		public PasskeyChangeResultData Data { get; set; }
	}

	public class PasskeyChangeResultData
	{
		/// <summary>How many passkeys the change revoked (0 for a rename or approval change).</summary>
		public int Revoked { get; set; }

		/// <summary>How many sessions that signed in with a removed passkey were ended.</summary>
		public int SessionsEnded { get; set; }

		/// <summary>True when this session was one of them: the client signs in again.</summary>
		public bool CurrentSessionEnded { get; set; }
	}
}

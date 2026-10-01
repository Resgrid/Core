using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Models.v4.Sessions
{
	/// <summary>The caller's own session state (passkey plan section 12.5). A locked shared session may read it.</summary>
	public class CurrentSessionResult : StandardApiResponseV4Base
	{
		public CurrentSessionResultData Data { get; set; }
	}

	public class CurrentSessionResultData
	{
		public string Operator { get; set; }

		/// <summary>The app the session belongs to: <c>web</c>, <c>responder</c>, <c>unit</c>, <c>dispatch</c> or <c>command</c>.</summary>
		public string Client { get; set; }

		public bool Shared { get; set; }

		public bool Locked { get; set; }

		/// <summary>Send it back to unlock; it changes with every lock.</summary>
		public long LockVersion { get; set; }

		/// <summary><c>explicit</c> or <c>idle</c> while locked.</summary>
		public string LockReason { get; set; }

		public int IdleLockMinutes { get; set; }

		/// <summary>When the session locks unless the operator does something (ISO 8601 UTC); null while locked.</summary>
		public string IdleLocksAt { get; set; }

		/// <summary>When the shift ends whatever happens (ISO 8601 UTC). Warn before it.</summary>
		public string ShiftEndsAt { get; set; }

		public string InstallationLabel { get; set; }
	}

	public class SessionLockResult : StandardApiResponseV4Base
	{
		public SessionLockResultData Data { get; set; }
	}

	public class SessionLockResultData
	{
		public bool Locked { get; set; }

		public long LockVersion { get; set; }
	}

	public class EndShiftInput
	{
		/// <summary>True for <b>Switch operator</b>: the same as ending the shift, audited as a handoff.</summary>
		public bool SwitchOperator { get; set; }
	}

	public class EndShiftResult : StandardApiResponseV4Base
	{
		public EndShiftResultData Data { get; set; }
	}

	public class EndShiftResultData
	{
		/// <summary>Always true: discard this session's tokens and caches, then sign the next operator in normally.</summary>
		public bool Ended { get; set; }
	}

	/// <summary>How the locked session's operator can unlock it (plan section 12.5.3). Nothing verifies here.</summary>
	public class UnlockOptionsInput
	{
		/// <summary>The lock version the client last saw; a newer lock means a newer screen.</summary>
		public long LockVersion { get; set; }
	}

	public class UnlockOptionsResult : StandardApiResponseV4Base
	{
		public UnlockOptionsResultData Data { get; set; }
	}

	public class UnlockOptionsResultData
	{
		public string Operator { get; set; }

		public long LockVersion { get; set; }

		/// <summary>
		/// What this operator can unlock with here: <c>totp</c>, <c>passkey</c>, <c>passkey_approval</c> and <c>federated</c>
		/// (the identity provider's MFA, begun with <c>unlock-sso</c>). Empty means quick unlock is unavailable; end the shift
		/// and sign in normally.
		/// </summary>
		public List<string> Methods { get; set; }

		/// <summary>The method to offer first. Shared installations never start the passkey prompt on their own.</summary>
		public string Preferred { get; set; }

		/// <summary>Assertion options bound to this lock, when <c>passkey</c> is offered.</summary>
		public PasskeyCeremonyResultData Passkey { get; set; }
	}

	public class UnlockApprovalInput
	{
		public long LockVersion { get; set; }
	}

	public class CompleteUnlockInput
	{
		/// <summary>The lock version from <c>unlock-options</c>. A lock since then refuses the unlock.</summary>
		public long LockVersion { get; set; }

		/// <summary><c>totp</c>, <c>passkey</c>, <c>passkey_approval</c> or <c>federated</c>.</summary>
		public string Method { get; set; }

		/// <summary>For <c>totp</c>: the current authenticator code.</summary>
		public string Code { get; set; }

		/// <summary>For <c>passkey</c>: the request id from <c>unlock-options</c>.</summary>
		public string RequestId { get; set; }

		/// <summary>For <c>passkey</c>: the platform's assertion (PublicKeyCredential.toJSON()).</summary>
		public JToken Credential { get; set; }

		/// <summary>For <c>passkey_approval</c>: the approved request from <c>unlock-approval</c>.</summary>
		public string ApprovalRequestId { get; set; }

		/// <summary>For <c>federated</c>: the transaction from <c>unlock-sso</c>.</summary>
		public string SsoTransactionId { get; set; }

		/// <summary>For <c>federated</c>: the one-time <c>sso_code</c> the return target received.</summary>
		public string SsoCode { get; set; }

		/// <summary>For <c>federated</c>: the PKCE verifier whose challenge began the unlock.</summary>
		public string CodeVerifier { get; set; }
	}

	/// <summary>Begins an unlock through the department's identity provider with MFA (provider step-up, plan section 7.8).</summary>
	public class UnlockSsoInput
	{
		public long LockVersion { get; set; }

		/// <summary><c>ios</c>, <c>android</c>, <c>web</c> or <c>desktop</c>.</summary>
		public string Platform { get; set; }

		/// <summary>This app's registered return target.</summary>
		public string ReturnTarget { get; set; }

		/// <summary>Opaque client state returned with the code.</summary>
		public string State { get; set; }

		/// <summary>S256 PKCE challenge; the verifier goes to <c>complete-unlock</c>.</summary>
		public string CodeChallenge { get; set; }

		public string CodeChallengeMethod { get; set; }
	}

	public class CompleteUnlockResult : StandardApiResponseV4Base
	{
		public CurrentSessionResultData Data { get; set; }
	}
}

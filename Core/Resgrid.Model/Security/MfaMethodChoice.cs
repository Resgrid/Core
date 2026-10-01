using System.Collections.Generic;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// The second-factor choice for a user after the first factor (passkey plan section 7.5 rule 5): what the user has
	/// enrolled, what the department and deployment accept, and the default to show first. Every allowed method is an
	/// equal choice; the preference only orders them.
	/// </summary>
	public sealed class MfaMethodChoice
	{
		public IReadOnlyList<string> EnrolledMethods { get; init; }

		public IReadOnlyList<string> AllowedMethods { get; init; }

		/// <summary>The method to show first; null when nothing is both enrolled and allowed.</summary>
		public string Preferred { get; init; }

		public bool CanVerify => Preferred != null;
	}

	/// <summary>The API names of MFA methods; the same strings the version 2 grant uses for <c>mfa_method</c>.</summary>
	public static class MfaMethodNames
	{
		public const string Totp = "totp";
		public const string Passkey = "passkey";
		public const string PasskeyApproval = "passkey_approval";
		public const string Federated = "federated";

		public static string From(MfaEvidenceMethod method) => method switch
		{
			MfaEvidenceMethod.Totp => Totp,
			MfaEvidenceMethod.Passkey => Passkey,
			MfaEvidenceMethod.PasskeyApproval => PasskeyApproval,
			MfaEvidenceMethod.Federated => Federated,
			_ => null
		};

		/// <summary>The method a name stands for; TOTP for anything unknown, the default every surface assumes.</summary>
		public static MfaEvidenceMethod Parse(string name) => name switch
		{
			Passkey => MfaEvidenceMethod.Passkey,
			PasskeyApproval => MfaEvidenceMethod.PasskeyApproval,
			Federated => MfaEvidenceMethod.Federated,
			_ => MfaEvidenceMethod.Totp
		};
	}
}

namespace Resgrid.Model.Security
{
	/// <summary>
	/// Which rows of the passkey plan section 7.6 matrix a verification is for; each governs which second factors a
	/// department accepts. TOTP is accepted in every scope.
	/// </summary>
	public enum MfaMethodScope
	{
		/// <summary>Sign-in, department entry, generic step-up, chat export and shared unlock (rows 1-8, 12, 15).</summary>
		Login = 1,

		/// <summary>Security, SSO and MFA policy changes (row 13): the sign-in switches, never Responder approval.</summary>
		SecurityChange = 2,

		/// <summary>Protected Data Grants, ADP management and protected-workflow approvals (rows 9-11).</summary>
		Adp = 3,

		/// <summary>Account factor management (row 14): deployment gates only, never a department switch, approval or provider step-up.</summary>
		Account = 4
	}
}

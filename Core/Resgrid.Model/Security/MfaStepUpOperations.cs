using System;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// The named operations an API step-up is for (passkey plan section 11, <c>Mfa/StepUpOptions</c> and
	/// <c>Mfa/VerifyStepUp</c>). Each has a maximum evidence age; a step-up never authorizes anything by itself.
	/// </summary>
	public static class MfaStepUpOperations
	{
		/// <summary>Security, SSO, SCIM and MFA policy changes (section 7.6 row 13).</summary>
		public const string SecurityChange = "security_change";

		/// <summary>ADP enrollment, offboarding and security commands (section 7.6 row 10).</summary>
		public const string AdpManagement = "adp_management";

		/// <summary>Chat exports (section 7.6 row 8).</summary>
		public const string ChatExport = "chat_export";

		/// <summary>The user's own sign-in methods: registering and removing passkeys (section 7.6 row 14).</summary>
		public const string AccountSecurity = "account_security";

		/// <summary>Any other guarded action: the Web's generic step-up and the other 5-minute operations (section 7.6 rows 7 and 15).</summary>
		public const string SensitiveOperation = "sensitive_operation";

		public static bool IsKnown(string operation) =>
			operation == SecurityChange || operation == AdpManagement || operation == ChatExport || operation == AccountSecurity ||
			operation == SensitiveOperation;

		/// <summary>The operation a step-up for <paramref name="scope"/> is for, where it is one operation.</summary>
		public static string ForScope(MfaMethodScope scope) => scope switch
		{
			MfaMethodScope.SecurityChange => SecurityChange,
			MfaMethodScope.Adp => AdpManagement,
			MfaMethodScope.Account => AccountSecurity,
			_ => SensitiveOperation
		};

		/// <summary>Which department switches govern the acceptable methods for the operation (plan section 7.6).</summary>
		public static MfaMethodScope ScopeFor(string operation) => operation switch
		{
			SecurityChange => MfaMethodScope.SecurityChange,
			AdpManagement => MfaMethodScope.Adp,
			AccountSecurity => MfaMethodScope.Account,
			_ => MfaMethodScope.Login
		};

		/// <summary>How long the evidence from one step-up serves the operation.</summary>
		public static TimeSpan WindowFor(string operation) =>
			TimeSpan.FromMinutes(Math.Max(1, Config.TwoFactorConfig.SensitiveOperationWindowMinutes));
	}
}

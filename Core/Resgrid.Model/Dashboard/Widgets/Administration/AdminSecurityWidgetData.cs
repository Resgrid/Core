namespace Resgrid.Model.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.security (plan §7.11): MFA / passkey adoption, SSO / SCIM state, the security policy's headline switches and active
	/// sessions. Counts and switches only; no member is named.
	/// </summary>
	public sealed class AdminSecurityWidgetData
	{
		/// <summary>Current members (not deleted, not disabled).</summary>
		public int Members { get; set; }

		public int MfaEnabled { get; set; }

		public int PasskeyUsers { get; set; }

		/// <summary>Members with an authenticator or a passkey.</summary>
		public int StrongAuthentication { get; set; }

		/// <summary>StrongAuthentication / Members × 100; 0 with no members.</summary>
		public int StrongAuthenticationPercent { get; set; }

		public int ActiveSessions { get; set; }

		public int UsersWithSessions { get; set; }

		public int SsoProviders { get; set; }

		public bool SsoEnabled { get; set; }

		/// <summary>An SSO provider with SCIM provisioning on.</summary>
		public bool ScimEnabled { get; set; }

		/// <summary>The department security policy requires MFA at sign-in.</summary>
		public bool RequireMfa { get; set; }

		public bool RequireSso { get; set; }

		/// <summary>0 / null = unlimited.</summary>
		public int? MaxConcurrentSessions { get; set; }

		public int? SessionTimeoutMinutes { get; set; }
	}
}

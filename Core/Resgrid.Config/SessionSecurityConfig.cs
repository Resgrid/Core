namespace Resgrid.Config
{
	public static class SessionSecurityConfig
	{
		// Session tracking is required by the Web BFF and is safe for pre-feature
		// credentials because they are adopted lazily by the validation middleware.
		public static bool TrackingEnabled = true;
		public static bool LegacyAdoptionEnabled = true;
		public static string RequireSessionClaimForCredentialsIssuedAfterUtc = "";
		// Blank is intentionally disabled at launch. Set to an ISO-8601 UTC timestamp
		// only after previewing stored DepartmentSecurityPolicy session values.
		public static string DepartmentSessionPolicyEnforcementAfterUtc = "";
		public static int LastActivityWriteIntervalMinutes = 5;
		public static int RevokedSessionRetentionDays = 90;
		public static int PublicResetLinkLifetimeMinutes = 30;
		public static int PublicResetAccountLimitPerHour = 3;
		public static int PublicResetIpLimitPerHour = 10;
		public static int WebBffAccessTokenLifetimeMinutes = 5;
		// Eventing tokens are deliberately longer-lived than API tokens. Every hub sets
		// CloseOnAuthenticationExpiration, so this value is how often each connected client tears the
		// connection down and renegotiates -- a token mint plus an introspection round trip per client,
		// per expiry. SessionValidationHubFilter revalidates the session on every hub invocation, so a
		// revoked session is cut off well before its token expires; this lifetime only bounds how long a
		// purely passive connection can keep receiving broadcasts after revocation.
		public static int WebEventingAccessTokenLifetimeMinutes = 30;
		public static int ClientMetadataMaximumLength = 256;
		public static int UserAgentMaximumLength = 1024;
		// Optional local JSON CIDR database. Leave blank to display location as unavailable.
		public static string IpLocationDatabasePath = "";
	}
}

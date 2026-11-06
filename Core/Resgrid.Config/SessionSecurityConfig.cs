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
		// How long an emailed password reset link works. Each link is single-use and stops working as soon as the account's password
		// or security stamp changes, so the lifetime only has to cover a slow inbox (filtering and quarantine can hold a message for
		// half an hour) and a responder pulled away mid-reset. A link someone asked for themselves: 2 hours. A link an administrator
		// sends: 24 hours, because the member did not ask for it and may not see it until their next shift. Minimum 5 minutes.
		public static int PublicResetLinkLifetimeMinutes = 120;
		public static int AdministratorResetLinkLifetimeMinutes = 1440;
		public static int PublicResetAccountLimitPerHour = 3;
		public static int PublicResetIpLimitPerHour = 10;

		// Password sign-ins (Web LogOn and the API password grant) from one address pause for the window once its failures there
		// reach this many different usernames (password spraying, credential stuffing) or this many attempts in all. One account's
		// own repeated failures are the Identity lockout's job, so a device stuck on an old password never blocks its whole network.
		// 0 turns a limit off; both 0 turns the throttle off.
		public static int LoginThrottleWindowMinutes = 15;
		public static int LoginThrottleDistinctAccountLimit = 10;
		public static int LoginThrottleAttemptLimit = 100;

		// Per-address caps on the other signed-out account forms (PublicFormRateLimiter), each a fixed window; 0 turns one off.
		// Forgot-password requests keep PublicResetIpLimitPerHour and PublicResetAccountLimitPerHour above. Generous for a station's
		// shared network (a crew mistyping, a training class), tight for a script.
		public static int RegisterIpLimitPerHour = 10;        // registration posts, valid or not (each also needs a reCAPTCHA)
		public static int RegisterIpAccountsPerDay = 5;       // new accounts and departments actually created
		public static int PasswordResetIpLimitPerHour = 20;   // posts using an emailed reset link
		public static int FactorRecoveryIpLimitPerHour = 20;  // lost-authenticator recovery posts
		public static int InviteIpLimitPerHour = 20;          // invitation posts and lookups of codes that don't exist
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

		// How often each SignalR host rechecks the sessions behind its open connections and closes those that ended,
		// locked or passed their idle deadline (passkey workbook section 12, slice 16). Invocations are checked on every
		// call anyway; this bounds how long a passive connection keeps receiving broadcasts. 0 turns the sweep off.
		public static int ConnectionSweepIntervalSeconds = 30;
	}
}

namespace Resgrid.Model.Security
{
	/// <summary>
	/// The facts of the caller's session that a version 2 Protected Data Grant must match (passkey plan section 8.3). They
	/// come from the session record the request's session validation just loaded and checked, never from client-supplied
	/// labels or unvalidated claims, so a missing value means "not proven" and a version 2 grant fails closed.
	/// </summary>
	public sealed class ProtectedGrantSessionContext
	{
		/// <summary>HttpContext.Items key under which session validation leaves the validated session's facts.</summary>
		public const string HttpItemKey = "Resgrid.ProtectedGrantSession";

		public string SessionId { get; init; }

		/// <summary>Numeric <see cref="UserSessionClientApplication"/> recorded on the session.</summary>
		public int ClientApplication { get; init; }

		/// <summary>The account authentication generation the session was validated at.</summary>
		public long AuthenticationGeneration { get; init; }

		/// <summary>The shared-session lock version; null for a personal session.</summary>
		public long? SessionLockVersion { get; init; }

		/// <summary>True for a shared vehicle or workstation session (plan section 12.5).</summary>
		public bool SharedMode { get; init; }

		/// <summary>When a shared session last locked; anything begun for this session before then is void.</summary>
		public System.DateTime? SessionLockedOnUtc { get; init; }

		/// <summary>
		/// When the caller's credential (cookie ticket or access token) was issued, as session validation checked it against
		/// the account's credential cutoff. The broker repeats that check with the same value.
		/// </summary>
		public System.DateTime? CredentialIssuedOnUtc { get; init; }

		/// <summary>True when the session's first factor was an SSO sign-in (a grant's amr says <c>fed</c>, not <c>pwd</c>).</summary>
		public bool FederatedFirstFactor { get; init; }

		/// <summary>When the session (for a shared session, its shift) ends; no grant outlives it (plan section 9.2).</summary>
		public System.DateTime? SessionExpiresOnUtc { get; init; }

		/// <summary>The facts of a session that validation accepted; null when there is no tracked session.</summary>
		public static ProtectedGrantSessionContext From(UserSession session, System.DateTime? credentialIssuedOnUtc) =>
			session == null || string.IsNullOrWhiteSpace(session.UserSessionId)
				? null
				: new ProtectedGrantSessionContext
				{
					SessionId = session.UserSessionId,
					ClientApplication = session.ClientApplication,
					AuthenticationGeneration = session.AuthenticationGeneration,
					SessionLockVersion = SharedSessionRules.LockVersionOf(session),
					SharedMode = session.SharedMode,
					SessionLockedOnUtc = session.SharedMode ? session.LockedOnUtc : null,
					CredentialIssuedOnUtc = credentialIssuedOnUtc,
					FederatedFirstFactor = session.AuthenticationMethod == (int)UserSessionAuthenticationMethod.OidcSso ||
						session.AuthenticationMethod == (int)UserSessionAuthenticationMethod.SamlSso,
					SessionExpiresOnUtc = session.ExpiresOn == default ? null : System.DateTime.SpecifyKind(session.ExpiresOn, System.DateTimeKind.Utc)
				};
	}
}

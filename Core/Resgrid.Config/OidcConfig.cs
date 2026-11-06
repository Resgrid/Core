#pragma warning disable S2223 // Non-constant static fields should not be visible
#pragma warning disable CA2211 // Non-constant fields should not be visible
#pragma warning disable S1104 // Fields should not have public accessibility

namespace Resgrid.Config
{
	public static class OidcConfig
	{
		/// <summary>
		/// The underlying database engine for the OIDC database (Does not support Mongo)
		/// </summary>
		public static DatabaseTypes DatabaseType = DatabaseTypes.SqlServer;

		public static string Key = "";

		public static string ConnectionString = "";

		public static int AccessTokenExpiryMinutes = 1440;

		public static int RefreshTokenExpiryDays = 365;

		public static int NonMobileRefreshTokenExpiryDays = 2;

		/// <summary>
		/// Issue access tokens as short opaque handles kept in the OIDC token store, instead of self-contained tokens that
		/// carry every permission claim (about 10 KB, past a proxy's default 8 KB header limit). Turning it off is safe
		/// at any time: the API refuses outstanding handles with a 401 and clients use their refresh token.
		/// </summary>
		public static bool UseReferenceAccessTokens = true;

		/// <summary>
		/// How often each API instance prunes expired, revoked and redeemed tokens, and authorizations left with no valid
		/// token, from the OIDC store. 0 or less turns pruning off.
		/// </summary>
		public static int TokenPruneIntervalMinutes = 360;

		/// <summary>
		/// Nothing younger than this is pruned, whatever its state, so a replayed redeemed refresh token is still
		/// recognized as reuse for this long.
		/// </summary>
		public static int TokenPruneMinimumAgeDays = 14;

		/// <summary>
		/// Comma-separated, registered client IDs allowed to receive the longer mobile
		/// refresh-token lifetime (RefreshTokenExpiryDays, which is also the API session's lifetime).
		/// Anonymous requests and caller-supplied scopes never qualify. The default is the Resgrid apps'
		/// public clients, which the API registers at start (omni-plan section 8.6 item 5). They have no
		/// secret, so this names the apps rather than authenticating them. An empty value is ignored by
		/// the config loader: to give every client the short lifetime, set an id no client uses, e.g. "none".
		/// </summary>
		public static string TrustedLongLivedClientIds = "responder,unit,ic,dispatch,bigboard";

		public static string EncryptionCert = "";

		public static string SigningCert = "";
	}
}

#pragma warning restore CA2211 // Non-constant fields should not be visible
#pragma warning restore S2223 // Non-constant static fields should not be visible
#pragma warning restore S1104 // Fields should not have public accessibility

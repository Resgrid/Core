namespace Resgrid.Model.Security
{
	/// <summary>
	/// The value-free text of shared-session audits (passkey plan section 13): the operator is the audit's user, and the
	/// installation label is only a label. Nothing here carries a token, code or anything from the previous operator.
	/// </summary>
	public static class SharedSessionAudit
	{
		/// <summary>The last eight characters of a session id, enough for support to match it.</summary>
		public static string SessionSuffix(string sessionId) =>
			string.IsNullOrWhiteSpace(sessionId) || sessionId.Length <= 8 ? sessionId : sessionId.Substring(sessionId.Length - 8);

		public static string Describe(string action, UserSession session, string detail = null) =>
			$"Shared session {action}. Client={(UserSessionClientApplication)session.ClientApplication}; " +
			$"Installation={Bound(session.DeviceName, 128)}; LockVersion={session.LockVersion}" +
			(string.IsNullOrWhiteSpace(detail) ? "." : $"; {Bound(detail, 64)}.");

		private static string Bound(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value))
				return "Unknown";
			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Replace(";", ",").Trim();
			return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength);
		}
	}
}

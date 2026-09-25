using System;
using System.Globalization;

namespace Resgrid.Model
{
	/// <summary>Pure decisions shared by the owning sign-in/session consumers and administrative previews.</summary>
	public static class DepartmentSecurityPolicyDecisions
	{
		public static bool BlocksPasswordLogin(bool requireSso, bool enabledProvider, bool loginViaSso) => requireSso && enabledProvider && !loginViaSso;
		public static bool RequiresMfaCompletion(bool requireMfa, bool completed) => requireMfa && !completed;
		public static int MinimumPasswordLength(int configured) => Math.Max(8, configured);
		public static bool PasswordExpired(int days, DateTime? lastSetOn, DateTime nowUtc) =>
			days > 0 && lastSetOn.HasValue && nowUtc > lastSetOn.Value.AddDays(days);
		public static bool TryGetSessionGate(string configured, out DateTime gateUtc)
		{
			if (DateTimeOffset.TryParse(configured, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
			{
				gateUtc = parsed.UtcDateTime; return true;
			}
			gateUtc = default; return false;
		}
		public static bool IdleExpired(int minutes, DateTime lastActiveOn, DateTime nowUtc) => minutes > 0 && lastActiveOn <= nowUtc.AddMinutes(-minutes);
	}
}

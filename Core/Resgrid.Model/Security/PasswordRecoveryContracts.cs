using System;

namespace Resgrid.Model.Security
{
	public class PasswordRecoveryRequest
	{
		public string UserId { get; set; }
		public string Email { get; set; }
		public long AuthenticationGeneration { get; set; }
		public string SecurityStampHash { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
	}

	public class PasswordRecoveryIssueResult
	{
		public bool Issued { get; set; }
		public bool RateLimited { get; set; }
		public string Token { get; set; }
	}

	/// <summary>
	/// Outcome of resolving a recovery token. A missing, unreadable, or expired token is a normal outcome of
	/// this flow, not an error, so the lookup reports it as <see cref="Found"/> = false rather than handing
	/// back a null request that every caller has to remember to check. <see cref="Request"/> is only populated
	/// when <see cref="Found"/> is true.
	/// </summary>
	public class PasswordRecoveryLookupResult
	{
		public bool Found { get; set; }
		public PasswordRecoveryRequest Request { get; set; }

		public static PasswordRecoveryLookupResult NotFound() => new PasswordRecoveryLookupResult();

		public static PasswordRecoveryLookupResult ForRequest(PasswordRecoveryRequest request) =>
			new PasswordRecoveryLookupResult { Found = true, Request = request };
	}
}

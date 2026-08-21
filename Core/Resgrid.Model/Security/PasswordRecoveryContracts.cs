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
}

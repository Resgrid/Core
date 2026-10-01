namespace Resgrid.Web.Models.AccountViewModels
{
	/// <summary>Sign in with single sign-on (passkey plan section 7.7.3): the department code, or the username whose department it is.</summary>
	public class SsoLogOnViewModel
	{
		public string DepartmentCode { get; set; }

		public string Username { get; set; }

		public string ReturnUrl { get; set; }
	}

	/// <summary>
	/// The provider's return, as it arrived: posted on by this site's own page so that the browser's SameSite=Strict cookies go
	/// with it. Nothing in it is trusted until the post matches it to this browser's round trip.
	/// </summary>
	public class SsoReturnContinueViewModel
	{
		public string Code { get; set; }
		public string State { get; set; }
		public string Error { get; set; }
	}

	/// <summary>
	/// The popup that finished a protected-data provider step-up: the grant (or the reason there is none) for the reveal dialog
	/// that opened it, handed over on this site only. Nothing here is stored in the browser.
	/// </summary>
	public class SsoReturnGrantViewModel
	{
		public string GrantToken { get; set; }
		public string GrantId { get; set; }
		public string ExpiresOnUtc { get; set; }
		public int WindowMinutes { get; set; }
		public string Error { get; set; }
	}
}

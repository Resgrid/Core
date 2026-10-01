using System.Collections.Generic;

namespace Resgrid.Web.Models.AccountViewModels
{
	/// <summary>
	/// The second step of Web sign-in on the login transaction (passkey plan sections 7.1 and 7.5 rule 5): every method the
	/// account has and the department accepts, as equal choices with the preferred one first. The methods come from the
	/// server at display time; the form carries only the typed code, the return address and the remember-browser choice.
	/// </summary>
	public class LoginMfaViewModel
	{
		public string Code { get; set; }

		public string ReturnUrl { get; set; }

		public bool RememberBrowser { get; set; }

		/// <summary>The usable methods, preferred first: <c>totp</c>, <c>passkey</c>, <c>passkey_approval</c>.</summary>
		public IReadOnlyList<string> Methods { get; set; } = new List<string>();

		public string Preferred { get; set; }

		/// <summary>Whether a recovery code can finish this sign-in (an account with an authenticator app).</summary>
		public bool RecoveryAvailable { get; set; }
	}
}

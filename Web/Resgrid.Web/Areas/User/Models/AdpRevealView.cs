using System;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models
{
	/// <summary>
	/// Everything the shared ADP reveal partials need (plan 7.2). A server-rendered page never holds
	/// a Protected Data Grant — the banner, the step-up modal and the reveal call all live in the
	/// browser — so every host page repeats the same markup and the same nine localized error
	/// strings. This model lets both partials be dropped into a view with two lines.
	/// </summary>
	public class AdpRevealView
	{
		/// <summary>
		/// Localized subject line for the banner ("Protected call", "Protected unit record"). The
		/// host resolves it, because only the host knows what record the page is showing.
		/// </summary>
		public string BannerTitle { get; set; }

		/// <summary>The action that returns decrypted values for a grant-holding caller.</summary>
		public string RevealAction { get; set; }

		/// <summary>The controller hosting <see cref="RevealAction"/>.</summary>
		public string RevealController { get; set; }

		/// <summary>
		/// Form values identifying the record being revealed (callId, contactId, userId, unitId).
		/// Posted alongside the antiforgery token; the reveal action authorizes the SUBJECT named
		/// here on top of validating the grant — a grant proves the caller stepped up, never that
		/// they may read this particular record.
		/// </summary>
		public Dictionary<string, string> RevealData { get; set; } = new Dictionary<string, string>();

		/// <summary>
		/// UTC expiry of a grant the page already holds in a bound form's hidden field (the Records *Revealed
		/// actions render this way). The module only needs the expiry: it drives the warning that offers an
		/// in-place re-verification before the window closes, and the submit hold once it has.
		/// </summary>
		public DateTime? GrantExpiresOnUtc { get; set; }

		/// <summary>
		/// CSS selectors of forms whose submit must carry a live grant (RMS plan section 5.9.3). The module
		/// writes the grant into the form's hidden fields on submit and, when none is live, runs the step-up in
		/// place and re-dispatches the same submit afterwards so nothing typed is lost.
		/// </summary>
		public List<string> BindForms { get; set; } = new List<string>();
	}
}

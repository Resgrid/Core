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
	}
}

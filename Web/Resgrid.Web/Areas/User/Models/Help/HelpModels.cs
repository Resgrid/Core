using System.Collections.Generic;
using Resgrid.Model.Academy;

namespace Resgrid.Web.Areas.User.Models.Help
{
	/// <summary>The Help center search (A3): free keyword results over the Academy and the Admin Assist reference, and "How do I…?".</summary>
	public sealed class HelpSearchModel
	{
		public string Query { get; set; }
		public AcademyHelpSearchResult Result { get; set; }
		public bool Searched => !string.IsNullOrWhiteSpace(Query);
		public string AcademyHome { get; set; }
	}

	/// <summary>The guided tours page (A3): every tour with the member's progress, theirs first.</summary>
	public sealed class HelpToursModel
	{
		public List<AcademyTourView> Tours { get; set; } = new List<AcademyTourView>();
		public string Message { get; set; }
	}

	/// <summary>A release's "What's new" micro-lesson (A3), with the other releases listed.</summary>
	public sealed class HelpWhatsNewModel
	{
		public AcademyWhatsNewView Release { get; set; }
		public List<AcademyWhatsNewManifest> Releases { get; set; } = new List<AcademyWhatsNewManifest>();
	}
}

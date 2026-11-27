using System.Collections.Generic;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Areas.User.Models.Lms
{
	/// <summary>A ranked list of learning recommendations as one partial renders it (My Training and the Recommendations page; K3).</summary>
	public sealed class LmsRecommendationListModel : LmsProgramsPage
	{
		public LmsRecommendationSet Set { get; set; } = new LmsRecommendationSet();

		/// <summary>The member is the viewer: courses offer Continue / Open, paths open My Training.</summary>
		public bool IsSelf { get; set; }

		/// <summary>The viewer may open the learning path pages (managers and path assigners).</summary>
		public bool CanOpenPaths { get; set; }

		/// <summary>A distinct element id prefix.</summary>
		public string PanelId { get; set; } = "lms-recommended";
	}

	/// <summary>My Training's "Recommended" panel (rendered by the LmsRecommendations view component).</summary>
	public sealed class LmsRecommendedPanel : LmsProgramsPage
	{
		public LmsRecommendationListModel List { get; set; }

		/// <summary>The viewer sees recommendations for members in their scope too (the Recommendations page).</summary>
		public bool CanSeeOthers { get; set; }
	}

	/// <summary>Trainings → Programs → Recommendations: the viewer's own, or a member's in their scope (officers).</summary>
	public sealed class LmsRecommendationsView : LmsProgramsPage
	{
		public LmsRecommendationListModel List { get; set; }

		public List<LmsRecommendationMember> Members { get; set; } = new List<LmsRecommendationMember>();

		public string MemberUserId { get; set; }

		public string MemberName { get; set; }
	}
}

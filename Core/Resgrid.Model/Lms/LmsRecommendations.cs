using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>Why a course or path is recommended to a member (K3 path recommendations, §13a). Not persisted.</summary>
	public enum LmsRecommendationReasons
	{
		/// <summary>An open learning requirement past due and past its grace period.</summary>
		RequirementOverdue = 0,

		/// <summary>An open learning requirement due soon, behind pace, or inside its grace period.</summary>
		RequirementAtRisk = 1,

		/// <summary>An open learning requirement due later.</summary>
		RequirementDue = 2,

		/// <summary>A learning path the member is on and has not finished.</summary>
		PathInProgress = 3,

		/// <summary>A competency of a framework the member has no evidence for; the course is tagged with it.</summary>
		CompetencyMissing = 4,

		/// <summary>A competency whose evidence is older than the framework's age limit; the course is tagged with it.</summary>
		CompetencyAged = 5,

		/// <summary>The course is offered to one of the member's roles (its audience names the role).</summary>
		RoleAudience = 6
	}

	/// <summary>What a recommendation points at.</summary>
	public enum LmsRecommendationTargetKinds
	{
		/// <summary>An LMS course or a Resgrid Academy lesson (both are LmsCourses).</summary>
		Course = 0,

		/// <summary>A learning path.</summary>
		Path = 1
	}

	/// <summary>One deterministic reason to recommend one target.</summary>
	public sealed class LmsRecommendationSignal
	{
		public LmsRecommendationTargetKinds TargetKind { get; set; }

		public int TargetId { get; set; }

		public LmsRecommendationReasons Reason { get; set; }

		/// <summary>The requirement's due date (requirement reasons).</summary>
		public DateTime? DueUtc { get; set; }

		/// <summary>What the reason names: the requirement, the competency ("FF1-5.3 Ladders") or the role.</summary>
		public string Detail { get; set; }
	}

	/// <summary>One recommended course or path, ranked.</summary>
	public sealed class LmsRecommendation
	{
		public LmsRecommendationTargetKinds TargetKind { get; set; }

		public int TargetId { get; set; }

		public string Title { get; set; }

		public int Score { get; set; }

		/// <summary>The earliest due date among its requirement reasons.</summary>
		public DateTime? DueUtc { get; set; }

		/// <summary>Every reason, strongest first.</summary>
		public List<LmsRecommendationSignal> Reasons { get; set; } = new List<LmsRecommendationSignal>();

		/// <summary>The member's open enrollment in the course ("Continue"); null to enrol or open the catalog.</summary>
		public int? EnrollmentId { get; set; }

		public bool IsSystem { get; set; }
	}

	/// <summary>
	/// The deterministic ranking (K3: "deterministic ranking first"): pure and order-independent. A target's score is the weight of its
	/// strongest reason plus a small bonus per further distinct reason; ties go to the earliest due date, then the title, then the target.
	/// The same signals in any order always give the same list.
	/// </summary>
	public static class LmsRecommendationRanker
	{
		public const int DefaultLimit = 10;
		public const int ExtraReasonBonus = 5;
		public const int MaxExtraReasons = 3;

		public static int Weight(LmsRecommendationReasons reason) => reason switch
		{
			LmsRecommendationReasons.RequirementOverdue => 100,
			LmsRecommendationReasons.RequirementAtRisk => 80,
			LmsRecommendationReasons.RequirementDue => 60,
			LmsRecommendationReasons.PathInProgress => 50,
			LmsRecommendationReasons.CompetencyMissing => 40,
			LmsRecommendationReasons.CompetencyAged => 30,
			LmsRecommendationReasons.RoleAudience => 20,
			_ => 0
		};

		/// <summary>Whether a reason keeps a course the member already completed on the list (an unmet requirement still needs it).</summary>
		public static bool KeepsCompleted(LmsRecommendationReasons reason) =>
			reason == LmsRecommendationReasons.RequirementOverdue || reason == LmsRecommendationReasons.RequirementAtRisk || reason == LmsRecommendationReasons.RequirementDue ||
			reason == LmsRecommendationReasons.CompetencyAged;

		/// <param name="signals">Every reason found.</param>
		/// <param name="titles">The title of each target that may be shown (a target without one is dropped: unpublished, deleted, not visible).</param>
		/// <param name="completedCourseIds">Courses the member has completed (dropped unless a reason keeps them).</param>
		/// <param name="openEnrollments">The member's open enrollment per course.</param>
		/// <param name="systemCourseIds">Resgrid Academy courses.</param>
		public static List<LmsRecommendation> Rank(IEnumerable<LmsRecommendationSignal> signals, IReadOnlyDictionary<(LmsRecommendationTargetKinds, int), string> titles,
			ISet<int> completedCourseIds = null, IReadOnlyDictionary<int, int> openEnrollments = null, ISet<int> systemCourseIds = null, int limit = DefaultLimit)
		{
			var result = new List<LmsRecommendation>();
			if (signals == null || titles == null)
				return result;

			foreach (var group in signals.Where(s => s != null && s.TargetId > 0 && Enum.IsDefined(typeof(LmsRecommendationReasons), s.Reason))
				.GroupBy(s => (s.TargetKind, s.TargetId)))
			{
				if (!titles.TryGetValue(group.Key, out var title) || string.IsNullOrWhiteSpace(title))
					continue;

				var reasons = group
					.GroupBy(s => (s.Reason, Detail: s.Detail ?? string.Empty))
					.Select(g => g.OrderBy(s => s.DueUtc ?? DateTime.MaxValue).First())
					.OrderByDescending(s => Weight(s.Reason)).ThenBy(s => s.DueUtc ?? DateTime.MaxValue).ThenBy(s => s.Detail ?? string.Empty, StringComparer.Ordinal)
					.ToList();

				var isCourse = group.Key.TargetKind == LmsRecommendationTargetKinds.Course;
				if (isCourse && completedCourseIds != null && completedCourseIds.Contains(group.Key.TargetId) && !reasons.Any(r => KeepsCompleted(r.Reason)))
					continue;

				var distinct = reasons.Select(r => r.Reason).Distinct().Count();
				result.Add(new LmsRecommendation
				{
					TargetKind = group.Key.TargetKind,
					TargetId = group.Key.TargetId,
					Title = title.Trim(),
					Score = Weight(reasons[0].Reason) + ExtraReasonBonus * Math.Min(MaxExtraReasons, distinct - 1),
					DueUtc = reasons.Where(r => r.DueUtc.HasValue).Select(r => r.DueUtc).OrderBy(d => d).FirstOrDefault(),
					Reasons = reasons,
					EnrollmentId = isCourse && openEnrollments != null && openEnrollments.TryGetValue(group.Key.TargetId, out var enrollmentId) ? enrollmentId : null,
					IsSystem = isCourse && systemCourseIds != null && systemCourseIds.Contains(group.Key.TargetId)
				});
			}

			return result
				.OrderByDescending(r => r.Score)
				.ThenBy(r => r.DueUtc ?? DateTime.MaxValue)
				.ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
				.ThenBy(r => r.Title, StringComparer.Ordinal)
				.ThenBy(r => (int)r.TargetKind)
				.ThenBy(r => r.TargetId)
				.Take(Math.Max(1, limit))
				.ToList();
		}
	}

	/// <summary>A member's recommendations, or why there are none to show.</summary>
	public sealed class LmsRecommendationSet
	{
		public LmsErrors Error { get; set; }

		public string UserId { get; set; }

		public List<LmsRecommendation> Items { get; set; } = new List<LmsRecommendation>();

		/// <summary>An AI explanation may be asked for (Enhanced AI with Ai.Knowledge, the model configured, Advanced Data Protection off).</summary>
		public bool CanExplain { get; set; }

		public static LmsRecommendationSet Fail(LmsErrors error, string userId = null) => new LmsRecommendationSet { Error = error, UserId = userId };
	}

	/// <summary>A member an officer may see recommendations for.</summary>
	public sealed class LmsRecommendationMember
	{
		public string UserId { get; set; }

		public string Name { get; set; }
	}

	/// <summary>The optional AI explanation: one short sentence per recommendation, written only from its reasons.</summary>
	public sealed class LmsRecommendationExplanation
	{
		/// <summary><see cref="KnowledgeOutcomes"/> Answered, or why not (NotEntitled, Unconfigured, KeptLocal, BudgetExhausted, Busy, Unavailable, Invalid).</summary>
		public string Outcome { get; set; }

		/// <summary>The explanation per recommendation ("Course:12", "Path:3").</summary>
		public Dictionary<string, string> Texts { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

		public string ExchangeId { get; set; }

		public static string Key(LmsRecommendationTargetKinds kind, int id) => kind + ":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}
}

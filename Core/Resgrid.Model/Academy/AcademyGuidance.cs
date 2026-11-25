using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Academy
{
	/// <summary>
	/// A department's live learning requirement on an Academy lesson, directly or through a path (round 5F): what worker 88 needs to honour
	/// the department's choice on a major update (the requirement's AcademyUpdatePolicy, LmsMajorChangePolicies, and its due days).
	/// </summary>
	public sealed class AcademyLessonRequirement
	{
		public int LmsRequirementId { get; set; }
		public int DepartmentId { get; set; }

		/// <summary>LmsMajorChangePolicies: 0 nothing, 1 notify (the default), 2 re-assign.</summary>
		public int AcademyUpdatePolicy { get; set; } = 1;

		public int DueDays { get; set; }
	}

	/// <summary>A member's remembered state of one guidance item (AcademyGuidanceStates.State). Persisted: append only.</summary>
	public enum AcademyGuidanceStatuses
	{
		/// <summary>Started or seen, still shown.</summary>
		Active = 0,

		/// <summary>The member closed it; it stays closed (until a tour's revision rises).</summary>
		Dismissed = 1,

		/// <summary>Every task done, or the "What's new" lesson read to the end.</summary>
		Completed = 2
	}

	/// <summary>
	/// A3 in-product guidance remembered per member (M0397): one row per member and guidance key ("tour:member", "whatsnew:2026.12"), with
	/// the tour tasks done and the manifest revision the state belongs to. Department-owned (purged with the department); it holds no content.
	/// </summary>
	[Table("AcademyGuidanceStates")]
	public class AcademyGuidanceState : IEntity
	{
		public const int GuidanceKeyMaxLength = 100;
		public const int TasksDoneMaxLength = 1000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int AcademyGuidanceStateId { get; set; }

		public int DepartmentId { get; set; }

		[Required]
		public string UserId { get; set; }

		[Required]
		[MaxLength(GuidanceKeyMaxLength)]
		public string GuidanceKey { get; set; }

		/// <summary><see cref="AcademyGuidanceStatuses"/>.</summary>
		public int State { get; set; }

		/// <summary>The tour's manifest revision the state belongs to; a newer revision starts over.</summary>
		public int Revision { get; set; }

		/// <summary>Comma separated task keys the member has done (tours only).</summary>
		[MaxLength(TasksDoneMaxLength)]
		public string TasksDone { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		public HashSet<string> DoneTasks => new HashSet<string>((TasksDone ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()),
			StringComparer.OrdinalIgnoreCase);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return AcademyGuidanceStateId; }
			set { AcademyGuidanceStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "AcademyGuidanceStates";

		[NotMapped]
		public string IdName => "AcademyGuidanceStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DoneTasks" };
	}

	/// <summary>
	/// A major revision of an Academy lesson waiting for its policy (M0397, system-level, not department-owned): the seed records it when it
	/// publishes the new major version; worker 88 then notifies, or re-assigns, the completed learners of every department that assigned
	/// the lesson, department by department (<see cref="AppliedThroughDepartmentId"/> is the cursor, so a stopped run resumes without
	/// telling anyone twice).
	/// </summary>
	[Table("AcademyLessonUpdates")]
	public class AcademyLessonUpdate : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int AcademyLessonUpdateId { get; set; }

		[Required]
		[MaxLength(100)]
		public string SystemKey { get; set; }

		public int LmsCourseId { get; set; }

		public int LmsCourseVersionId { get; set; }

		public int Revision { get; set; }

		/// <summary>The LMS major-change policy value (Notify 1, Reassign 2).</summary>
		public int Policy { get; set; }

		public int? ReassignDueDays { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>The highest department id already handled; departments are processed in ascending id order.</summary>
		public int? AppliedThroughDepartmentId { get; set; }

		public DateTime? CompletedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return AcademyLessonUpdateId; }
			set { AcademyLessonUpdateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "AcademyLessonUpdates";

		[NotMapped]
		public string IdName => "AcademyLessonUpdateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One task of a tour as the member sees it.</summary>
	public sealed class AcademyTourTaskView
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Hint { get; set; }
		public string PageUrl { get; set; }
		public string LessonId { get; set; }
		public string LessonUrl { get; set; }
		public bool Done { get; set; }
	}

	/// <summary>A guided tour in the member's language with their progress.</summary>
	public sealed class AcademyTourView
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Intro { get; set; }
		public int Revision { get; set; }
		public List<AcademyTourTaskView> Tasks { get; set; } = new List<AcademyTourTaskView>();
		public bool Dismissed { get; set; }
		public bool IsMine { get; set; }
		public int DoneCount => Tasks.Count(t => t.Done);
		public bool Completed => Tasks.Count > 0 && Tasks.All(t => t.Done);
	}

	public sealed class AcademyWhatsNewItemView
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public string PageUrl { get; set; }
		public string LessonUrl { get; set; }
	}

	/// <summary>A release's "What's new" micro-lesson in the member's language.</summary>
	public sealed class AcademyWhatsNewView
	{
		public string Release { get; set; }
		public DateTime PublishedOn { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public double Minutes { get; set; }
		public List<AcademyWhatsNewItemView> Items { get; set; } = new List<AcademyWhatsNewItemView>();
		public bool Dismissed { get; set; }
	}

	/// <summary>What the dashboard card shows a member: their role's tour while it is open, and the newest unread release lesson.</summary>
	public sealed class AcademyGuidanceView
	{
		public AcademyTourView Tour { get; set; }
		public AcademyWhatsNewView WhatsNew { get; set; }
		public bool IsEmpty => Tour == null && WhatsNew == null;
	}

	/// <summary>Who is asking, as the guidance service needs it (resolved by the caller from claims).</summary>
	public sealed class AcademyGuidanceActor
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Locale { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public bool MayAssignTraining { get; set; }
	}

	public enum AcademyHelpHitKinds
	{
		Lesson = 0,
		Feature = 1,
		Reference = 2
	}

	/// <summary>One Help-menu search result: an Academy lesson, a product feature, or an Admin Assist reference article.</summary>
	public sealed class AcademyHelpHit
	{
		public AcademyHelpHitKinds Kind { get; set; }
		public string Id { get; set; }
		public string Title { get; set; }
		public string Excerpt { get; set; }

		/// <summary>The lesson on the Academy, or the feature's page on the public documentation site.</summary>
		public string Url { get; set; }

		/// <summary>The in-product page (a feature's page; a setting's page for administrators).</summary>
		public string PageUrl { get; set; }

		public double Minutes { get; set; }
		public double Score { get; set; }
	}

	/// <summary>Free keyword search over the Academy and the Admin Assist reference corpus (A3, no AI).</summary>
	public sealed class AcademyHelpSearchResult
	{
		public string Query { get; set; }
		public List<AcademyHelpHit> Lessons { get; set; } = new List<AcademyHelpHit>();
		public List<AcademyHelpHit> Docs { get; set; } = new List<AcademyHelpHit>();
		public bool HasResults => Lessons.Count > 0 || Docs.Count > 0;
	}
}

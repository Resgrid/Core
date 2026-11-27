using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Model.Certifications;
using Resgrid.Model.Helpers;

namespace Resgrid.Model.Lms
{
	// Round 9A (LMS add-on): certifications backed by training. A department defines, per certification type, the set of items a member
	// must all complete within the cycle window to be issued or to renew the certification (LMS courses, a course set / learning path,
	// baseline trainings, a supervised practical an authorized instructor recorded, skill evaluations, CE hours of a category). The engine
	// issues or renews automatically when the last item completes and keeps an issuance ledger, so a void of a completion behind an issue
	// or renewal takes it back (the previous expiry restored, a new record withdrawn) unless another completion still covers it.

	#region Enums

	/// <summary>Where an automatic issue or renewal came from (LmsCertificationIssuances.Source). Persisted: append only.</summary>
	public enum LmsCertificationIssueSources
	{
		/// <summary>An LMS course whose version issues the certification was completed (SourceRefId "lms-enrollment:{id}").</summary>
		CourseCompletion = 0,

		/// <summary>A learning path that issues the certification was finished (SourceRefId "lms-path:{pathId}").</summary>
		PathCompletion = 1,

		/// <summary>The certification type's requirement set was completed (SourceRefId "lms-cert-set:{setId}").</summary>
		RequirementSet = 2,

		/// <summary>A cycle's renewal hours were met and the next cycle started (SourceRefId "cert-cycle:{previousCertificationId}").</summary>
		CycleAutoRenew = 3
	}

	/// <summary>What the engine did to the member's record (LmsCertificationIssuances.Action). Persisted: append only.</summary>
	public enum LmsCertificationIssueActions
	{
		/// <summary>A new record was created.</summary>
		Issued = 0,

		/// <summary>The existing record's expiry was moved later (and an expired one reactivated).</summary>
		Renewed = 1,

		/// <summary>A new record for the next cycle was created beside the current one (cycle auto-renew).</summary>
		NextCycle = 2
	}

	/// <summary>Where an issuance stands (LmsCertificationIssuances.State). Persisted: append only.</summary>
	public enum LmsCertificationIssuanceStates
	{
		/// <summary>In force: its completions are still valid.</summary>
		Live = 0,

		/// <summary>A void took it back: the record returned to its earlier state (or a new record was withdrawn).</summary>
		RolledBack = 1,

		/// <summary>A void took it back while a later issuance stood on the same record: the record kept the later state.</summary>
		Unwound = 2,

		/// <summary>A void would have taken it back, but the record was changed by hand since: left for an officer (audited).</summary>
		ManualChangeKept = 3
	}

	/// <summary>Where a member stands on a certification's requirement set (the progress card and the officers' list).</summary>
	public enum LmsCertificationProgressStates
	{
		/// <summary>The member holds no live record of the type.</summary>
		NotHeld = 0,

		/// <summary>Held, not expiring within the due-soon window.</summary>
		Current = 1,

		/// <summary>Held, expiring within the due-soon window with items still open.</summary>
		DueSoon = 2,

		/// <summary>Expired, inside the grace period: completing the set still renews this cycle.</summary>
		InGrace = 3,

		/// <summary>Expired past the grace period (completing the set issues it again).</summary>
		Lapsed = 4,

		/// <summary>Suspended or revoked by an officer: the engine never issues or renews over that.</summary>
		Blocked = 5,

		/// <summary>Every item is complete for the current window (the certification is, or is about to be, issued or renewed).</summary>
		Complete = 6
	}

	#endregion Enums

	#region Entities

	/// <summary>
	/// A certification type's requirement set (M0405; one per type, LMS). Who it applies to follows the type: members who hold it (renewal)
	/// and, when <see cref="IssueToNonHolders"/>, anyone who completes every item (first issue). Items in LmsCertificationRequirementItems.
	/// </summary>
	[Table("LmsCertificationRequirementSets")]
	public class LmsCertificationRequirementSet : IEntity
	{
		public const int DefaultDueSoonDays = 30;
		public const int MaxDueSoonDays = 180;
		public const int MaxItems = 25;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCertificationRequirementSetId { get; set; }

		public int DepartmentId { get; set; }

		public int DepartmentCertificationTypeId { get; set; }

		public bool IsActive { get; set; } = true;

		/// <summary>A member who completes every item but holds no live record is issued the certification.</summary>
		public bool IssueToNonHolders { get; set; } = true;

		/// <summary>A holder who completes every item within the renewal window is renewed.</summary>
		public bool RenewHolders { get; set; } = true;

		/// <summary>Holders are told this many days before expiry when items are still open (0: not told).</summary>
		public int DueSoonDays { get; set; } = DefaultDueSoonDays;

		/// <summary>Members are told (push + email) when the set issues, renews or a void takes the certification back, and when items are due.</summary>
		public bool NotifyMembers { get; set; } = true;

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		/// <summary>The set's items (read separately; never cascaded by the ORM).</summary>
		[NotMapped]
		[JsonIgnore]
		public List<LmsCertificationRequirementItem> Items { get; set; } = new List<LmsCertificationRequirementItem>();

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCertificationRequirementSetId; }
			set { LmsCertificationRequirementSetId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCertificationRequirementSets";

		[NotMapped]
		public string IdName => "LmsCertificationRequirementSetId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Items" };
	}

	/// <summary>
	/// One item of a requirement set (M0405). The item kinds are the learning requirement kinds (<see cref="LmsRequirementKinds"/>: Course,
	/// Training, CategoryHours, Path = a course set or series, Evaluations = passed skill evaluations of a sheet code). A supervised practical
	/// ("slow speed course", a live-fire evolution, a hazmat entry drill) is a Training or Course item with
	/// <see cref="RequireRecordedByInstructor"/>: only a completion an authorized instructor recorded (round 7B) counts.
	/// </summary>
	[Table("LmsCertificationRequirementItems")]
	public class LmsCertificationRequirementItem : IEntity
	{
		public const int MaxLabelLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCertificationRequirementItemId { get; set; }

		public int DepartmentId { get; set; }

		public int LmsCertificationRequirementSetId { get; set; }

		/// <summary><see cref="LmsRequirementKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary>The course, training, category or path id.</summary>
		public int? TargetRefId { get; set; }

		/// <summary>The skill sheet code of an Evaluations item (matched case-insensitively).</summary>
		public string TargetCode { get; set; }

		public decimal? RequiredHours { get; set; }

		public int? RequiredCount { get; set; }

		/// <summary>Only a completion an authorized instructor recorded for the member counts (a supervised practical).</summary>
		public bool RequireRecordedByInstructor { get; set; }

		/// <summary>The name members see (e.g. "Slow speed course"); the target's own name when empty.</summary>
		public string Label { get; set; }

		public int SortOrder { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCertificationRequirementItemId; }
			set { LmsCertificationRequirementItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCertificationRequirementItems";

		[NotMapped]
		public string IdName => "LmsCertificationRequirementItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		/// <summary>The amount the item asks for: hours, evaluations, or one completion.</summary>
		[NotMapped]
		[JsonIgnore]
		public decimal RequiredAmount =>
			Kind == (int)LmsRequirementKinds.CategoryHours ? Math.Max(0m, RequiredHours ?? 0m)
			: Kind == (int)LmsRequirementKinds.Evaluations ? Math.Max(1, RequiredCount ?? 1)
			: 1m;
	}

	/// <summary>
	/// The issuance ledger (M0406): one row per automatic issue or renewal the training engine made, with the record's state before it and
	/// the learning records that satisfied it. A void of one of those records re-evaluates the row: another valid completion keeps it (the
	/// basis is updated), otherwise the record goes back to the state before (or a record the engine created is withdrawn). Department-owned;
	/// no foreign keys (§17.3).
	/// </summary>
	[Table("LmsCertificationIssuances")]
	public class LmsCertificationIssuance : IEntity
	{
		public const int MaxBasisLength = 2000;
		public const int MaxReasonLength = 500;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsCertificationIssuanceId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public int PersonnelCertificationId { get; set; }

		public int DepartmentCertificationTypeId { get; set; }

		/// <summary><see cref="LmsCertificationIssueSources"/>.</summary>
		public int Source { get; set; }

		/// <summary>The course, path, set or previous cycle behind it ("lms-enrollment:12", "lms-path:4", "lms-cert-set:3", "cert-cycle:301").</summary>
		public string SourceRefId { get; set; }

		/// <summary><see cref="LmsCertificationIssueActions"/>.</summary>
		public int Action { get; set; }

		/// <summary>The record's status before (null when the record was created by this issuance).</summary>
		public int? PreviousStatus { get; set; }

		public DateTime? PreviousExpiresOn { get; set; }

		public DateTime? PreviousRecievedOn { get; set; }

		public int NewStatus { get; set; }

		public DateTime? NewExpiresOn { get; set; }

		/// <summary>The learning record ids that satisfied it, comma separated (a void of any of them re-evaluates the row).</summary>
		public string BasisRecordIds { get; set; }

		/// <summary>When the completion that triggered it happened (UTC).</summary>
		public DateTime CompletedOn { get; set; }

		public DateTime IssuedOn { get; set; }

		/// <summary>Who caused it (the member, the recorder or the officer; "system" for the worker).</summary>
		public string IssuedByUserId { get; set; }

		/// <summary><see cref="LmsCertificationIssuanceStates"/>.</summary>
		public int State { get; set; }

		public DateTime? RolledBackOn { get; set; }

		public string RolledBackByUserId { get; set; }

		public string RollbackReason { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsCertificationIssuanceId; }
			set { LmsCertificationIssuanceId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsCertificationIssuances";

		[NotMapped]
		public string IdName => "LmsCertificationIssuanceId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsLive", "Basis" };

		[NotMapped]
		[JsonIgnore]
		public bool IsLive => State == (int)LmsCertificationIssuanceStates.Live;

		/// <summary>The basis record ids as a set.</summary>
		[NotMapped]
		[JsonIgnore]
		public IReadOnlyCollection<int> Basis => LmsCertificationIssuanceRules.ParseIds(BasisRecordIds);
	}

	#endregion Entities

	#region Views and inputs

	/// <summary>One item of a requirement set as the editor posts it.</summary>
	public sealed class LmsCertificationRequirementItemInput
	{
		public int Kind { get; set; }
		public int? TargetRefId { get; set; }
		public string TargetCode { get; set; }
		public decimal? RequiredHours { get; set; }
		public int? RequiredCount { get; set; }
		public bool RequireRecordedByInstructor { get; set; }
		public string Label { get; set; }
	}

	/// <summary>A requirement set as the editor posts it.</summary>
	public sealed class LmsCertificationRequirementSetInput
	{
		public int DepartmentCertificationTypeId { get; set; }
		public bool IsActive { get; set; } = true;
		public bool IssueToNonHolders { get; set; } = true;
		public bool RenewHolders { get; set; } = true;
		public int DueSoonDays { get; set; } = LmsCertificationRequirementSet.DefaultDueSoonDays;
		public bool NotifyMembers { get; set; } = true;
		public List<LmsCertificationRequirementItemInput> Items { get; set; } = new List<LmsCertificationRequirementItemInput>();
	}

	/// <summary>One item of a member's progress.</summary>
	public sealed class LmsCertificationItemProgress
	{
		public int ItemId { get; set; }
		public int Kind { get; set; }
		public string Label { get; set; }
		public bool RequireRecordedByInstructor { get; set; }
		public bool Done { get; set; }
		public decimal Progress { get; set; }
		public decimal Required { get; set; }
		public DateTime? LastCompletedUtc { get; set; }
		/// <summary>The last counting completion was recorded by an instructor (the card says so).</summary>
		public bool LastRecordedByOther { get; set; }
		/// <summary>The learning records that make up the item within the window (the issuance basis).</summary>
		public List<int> RecordIds { get; set; } = new List<int>();
		/// <summary>The course, training or path id the member can open next (null for hours and evaluations).</summary>
		public int? TargetRefId { get; set; }
	}

	/// <summary>A member's progress on one certification type's requirement set ("3 of 4 done").</summary>
	public sealed class LmsCertificationProgress
	{
		public int SetId { get; set; }
		public int CertificationTypeId { get; set; }
		public string CertificationTypeName { get; set; }
		public string CertificationTypeCode { get; set; }
		public string UserId { get; set; }
		/// <summary>The record the progress renews (live or in grace), when there is one.</summary>
		public int? CertificationId { get; set; }
		public DateTime? ExpiresOn { get; set; }
		public int? DaysUntilExpiry { get; set; }
		/// <summary>The department-local dates in which completions count.</summary>
		public DateTime WindowStart { get; set; }
		public DateTime WindowEnd { get; set; }
		public List<LmsCertificationItemProgress> Items { get; set; } = new List<LmsCertificationItemProgress>();
		public int Done => Items.Count(i => i.Done);
		public int Total => Items.Count;
		public bool Complete => Total > 0 && Done == Total;
		/// <summary>When the last item completed (UTC): the issue or renewal date.</summary>
		public DateTime? CompletedOnUtc => Complete ? Items.Max(i => i.LastCompletedUtc) : null;
		public LmsCertificationProgressStates State { get; set; }
		/// <summary>The expiry an issue or renewal today would give (local date), when the type expires.</summary>
		public DateTime? NextExpiresOn { get; set; }
		/// <summary>The record the engine would act on is a renewal (else a first issue).</summary>
		public bool IsRenewal { get; set; }
		public string MemberName { get; set; }
		/// <summary>The learning record ids of every done item (the issuance basis).</summary>
		public List<int> BasisRecordIds => Items.Where(i => i.Done).SelectMany(i => i.RecordIds).Distinct().OrderBy(i => i).ToList();
	}

	/// <summary>A requirement set with its type and items for the officers' list.</summary>
	public sealed class LmsCertificationRequirementSetRow
	{
		public DepartmentCertificationType Type { get; set; }
		public LmsCertificationRequirementSet Set { get; set; }
		public List<(LmsCertificationRequirementItem Item, string Name)> Items { get; set; } = new List<(LmsCertificationRequirementItem, string)>();
		public int Holders { get; set; }
		public int DueSoon { get; set; }
		public int InGrace { get; set; }
	}

	/// <summary>Counts from one requirement-set run (worker 94 or one member's evaluation).</summary>
	public sealed class LmsCertificationRequirementRunResult
	{
		public int Departments { get; set; }
		public int Issued { get; set; }
		public int Renewed { get; set; }
		public int DueNotices { get; set; }
		public int Skipped { get; set; }
		public int Failures { get; set; }
	}

	/// <summary>Counts from one void re-evaluation.</summary>
	public sealed class LmsCertificationVoidResult
	{
		public int RolledBack { get; set; }
		public int Kept { get; set; }
		public int Unwound { get; set; }
		public int LeftForReview { get; set; }
	}

	/// <summary>What one automatic issue or renewal asks for.</summary>
	public sealed class LmsCertificationIssueRequest
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public int CertificationTypeId { get; set; }
		/// <summary>When the completion that triggers it happened (UTC); converted to the department's local date.</summary>
		public DateTime CompletedOnUtc { get; set; }
		public LmsCertificationIssueSources Source { get; set; }
		public string SourceRefId { get; set; }
		public IReadOnlyCollection<int> BasisRecordIds { get; set; } = Array.Empty<int>();
		/// <summary>Who caused it; null for the worker.</summary>
		public string ActorUserId { get; set; }
		/// <summary>Tell the member (push + email) when it issues or renews.</summary>
		public bool NotifyMember { get; set; } = true;
		/// <summary>A renewal only (a requirement set with first issues turned off): no record, nothing done.</summary>
		public bool RenewOnly { get; set; }
	}

	/// <summary>The outcome of an automatic issue or renewal.</summary>
	public sealed class LmsCertificationIssueResult
	{
		public int? CertificationId { get; set; }
		public LmsCertificationIssueActions? Action { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>Why nothing changed ("already_current", "blocked", "not_renewable", "already_expired", "type_invalid"); null when it changed.</summary>
		public string Skipped { get; set; }
		public LmsCertificationIssuance Issuance { get; set; }
		public bool Changed => Action.HasValue;
	}

	#endregion Views and inputs

	#region Rules

	/// <summary>Pure rules of the issuance ledger and the requirement sets (no I/O).</summary>
	public static class LmsCertificationIssuanceRules
	{
		public const string SetRefPrefix = "lms-cert-set:";
		public const string PathRefPrefix = "lms-path:";
		public const string CycleRefPrefix = "cert-cycle:";

		public static string SetRef(int setId) => SetRefPrefix + setId.ToString(CultureInfo.InvariantCulture);
		public static string PathRef(int pathId) => PathRefPrefix + pathId.ToString(CultureInfo.InvariantCulture);
		public static string CycleRef(int previousCertificationId) => CycleRefPrefix + previousCertificationId.ToString(CultureInfo.InvariantCulture);

		/// <summary>The id after a "prefix:" reference; 0 when it is not one.</summary>
		public static int RefId(string sourceRefId, string prefix)
		{
			if (string.IsNullOrWhiteSpace(sourceRefId) || !sourceRefId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				return 0;
			return int.TryParse(sourceRefId.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
		}

		public static IReadOnlyCollection<int> ParseIds(string csv) =>
			(csv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(p => int.TryParse(p.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();

		/// <summary>The basis ids as stored text, newest kept when the column would overflow.</summary>
		public static string FormatIds(IEnumerable<int> ids)
		{
			var list = (ids ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().OrderByDescending(i => i).ToList();
			var text = string.Join(",", list.OrderBy(i => i).Select(i => i.ToString(CultureInfo.InvariantCulture)));
			while (text.Length > LmsCertificationIssuance.MaxBasisLength && list.Count > 1)
			{
				list.RemoveAt(list.Count - 1);
				text = string.Join(",", list.OrderBy(i => i).Select(i => i.ToString(CultureInfo.InvariantCulture)));
			}
			return text.Length == 0 ? null : text;
		}

		/// <summary>A record the engine never issues or renews over: suspended or revoked by an officer.</summary>
		public static bool IsBlocked(PersonnelCertification record) =>
			record != null && !record.IsDeleted && (record.Status == (int)PersonnelCertificationStatuses.Suspended || record.Status == (int)PersonnelCertificationStatuses.Revoked);

		/// <summary>
		/// The record an issue or renewal of the type acts on: the member's newest non-deleted record of the type (latest expiry, never-expiring
		/// first), or null for a first issue.
		/// </summary>
		public static PersonnelCertification CurrentRecord(IEnumerable<PersonnelCertification> records, int certificationTypeId, string userId) =>
			(records ?? Enumerable.Empty<PersonnelCertification>())
				.Where(c => c != null && !c.IsDeleted && c.DepartmentCertificationTypeId == certificationTypeId && string.Equals(c.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(c => c.ExpiresOn ?? DateTime.MaxValue).ThenByDescending(c => c.PersonnelCertificationId).FirstOrDefault();

		/// <summary>Why an item is invalid ("lms_cert_req_*"), or null.</summary>
		public static string ValidateItem(LmsCertificationRequirementItemInput item)
		{
			if (item == null || !Enum.IsDefined(typeof(LmsRequirementKinds), item.Kind))
				return "lms_cert_req_item_invalid";
			var kind = (LmsRequirementKinds)item.Kind;
			switch (kind)
			{
				case LmsRequirementKinds.Evaluations:
					if (string.IsNullOrWhiteSpace(item.TargetCode))
						return "lms_cert_req_item_code_required";
					if (item.RequiredCount.HasValue && (item.RequiredCount.Value < 1 || item.RequiredCount.Value > 100))
						return "lms_cert_req_item_count_invalid";
					break;
				case LmsRequirementKinds.CategoryHours:
					if (!item.TargetRefId.HasValue || item.TargetRefId.Value <= 0)
						return "lms_cert_req_item_target_required";
					if (!item.RequiredHours.HasValue || item.RequiredHours.Value <= 0m || item.RequiredHours.Value > 9999m)
						return "lms_cert_req_item_hours_invalid";
					break;
				default:
					if (!item.TargetRefId.HasValue || item.TargetRefId.Value <= 0)
						return "lms_cert_req_item_target_required";
					break;
			}
			if (item.RequireRecordedByInstructor && kind != LmsRequirementKinds.Training && kind != LmsRequirementKinds.Course)
				return "lms_cert_req_item_practical_kind";
			if (!string.IsNullOrEmpty(item.Label) && item.Label.Trim().Length > LmsCertificationRequirementItem.MaxLabelLength)
				return "lms_cert_req_item_label_too_long";
			return null;
		}
	}

	/// <summary>
	/// The one place a member's progress on a requirement set is computed (round 9A), shared by My Training, the certification record, the
	/// officers' list, v4 and the engine: every item counts only completions inside the counting window (CertificationValidity) on
	/// department-local dates; a supervised practical counts only a completion an instructor recorded; hours and evaluations add up.
	/// </summary>
	public static class LmsCertificationRequirementEvaluator
	{
		/// <summary>The facts of one item: <paramref name="facts"/> are the member's facts of the item's kind (any target).</summary>
		public static IEnumerable<LmsRequirementFact> FactsFor(LmsCertificationRequirementItem item, IEnumerable<LmsRequirementFact> facts)
		{
			var all = facts ?? Enumerable.Empty<LmsRequirementFact>();
			if (item.Kind == (int)LmsRequirementKinds.Evaluations)
				return all.Where(f => f != null && string.Equals((f.Code ?? string.Empty).Trim(), (item.TargetCode ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
			return all.Where(f => f != null && item.TargetRefId.HasValue && f.RefId == item.TargetRefId.Value);
		}

		/// <summary>One item's progress from its facts between two UTC moments (inclusive).</summary>
		public static LmsCertificationItemProgress EvaluateItem(LmsCertificationRequirementItem item, string label, IEnumerable<LmsRequirementFact> itemFacts, DateTime fromUtc,
			DateTime toUtc)
		{
			var counting = (itemFacts ?? Enumerable.Empty<LmsRequirementFact>())
				.Where(f => f != null && f.CompletedOn >= fromUtc && f.CompletedOn <= toUtc &&
				            (!item.RequireRecordedByInstructor || f.CompletionMethod == (int)LearningCompletionMethods.RecordedByOther))
				.OrderBy(f => f.CompletedOn).ToList();
			var required = item.RequiredAmount;
			var counted = item.Kind == (int)LmsRequirementKinds.CategoryHours || item.Kind == (int)LmsRequirementKinds.Evaluations;
			var progress = counted ? counting.Sum(f => f.Amount <= 0m && item.Kind == (int)LmsRequirementKinds.Evaluations ? 1m : f.Amount) : counting.Count > 0 ? 1m : 0m;
			var done = progress >= required && counting.Count > 0;

			// The completion that made the item: the earliest point where the amount was reached (the latest one for a single completion).
			DateTime? completedOn = null;
			var used = new List<LmsRequirementFact>();
			if (done)
			{
				if (!counted)
				{
					var last = counting.Last();
					used.Add(last);
					completedOn = last.CompletedOn;
				}
				else
				{
					var total = 0m;
					foreach (var fact in counting)
					{
						total += fact.Amount <= 0m && item.Kind == (int)LmsRequirementKinds.Evaluations ? 1m : fact.Amount;
						used.Add(fact);
						if (total >= required)
						{
							completedOn = fact.CompletedOn;
							break;
						}
					}
				}
			}

			var shown = counting.LastOrDefault();
			return new LmsCertificationItemProgress
			{
				ItemId = item.LmsCertificationRequirementItemId,
				Kind = item.Kind,
				Label = label,
				RequireRecordedByInstructor = item.RequireRecordedByInstructor,
				Done = done,
				Progress = progress,
				Required = required,
				LastCompletedUtc = done ? completedOn : shown?.CompletedOn,
				LastRecordedByOther = (done ? used.LastOrDefault() : shown)?.CompletionMethod == (int)LearningCompletionMethods.RecordedByOther,
				RecordIds = (done ? used : new List<LmsRequirementFact>())
					.SelectMany(f => (f.RecordIds ?? new List<int>()).Concat(f.LearningRecordId.HasValue ? new[] { f.LearningRecordId.Value } : Array.Empty<int>()))
					.Where(id => id > 0).Distinct().ToList(),
				TargetRefId = item.Kind == (int)LmsRequirementKinds.CategoryHours || item.Kind == (int)LmsRequirementKinds.Evaluations ? null : item.TargetRefId
			};
		}

		/// <summary>
		/// A member's progress on a set: the counting window from the member's current record (CertificationValidity), every item evaluated in
		/// it, and the state the cards show. <paramref name="facts"/> are the member's facts by item id; <paramref name="records"/> their
		/// records of every type (only the set's type is read).
		/// </summary>
		public static LmsCertificationProgress Evaluate(LmsCertificationRequirementSet set, DepartmentCertificationType type, IReadOnlyList<LmsCertificationRequirementItem> items,
			Func<LmsCertificationRequirementItem, string> label, IReadOnlyDictionary<int, List<LmsRequirementFact>> facts, string userId, IEnumerable<PersonnelCertification> records,
			Department department, DateTime utcNow)
		{
			var localToday = TrainingDueDates.LocalDate(utcNow, department);
			var current = LmsCertificationIssuanceRules.CurrentRecord(records, type.DepartmentCertificationTypeId, userId);
			var blocked = LmsCertificationIssuanceRules.IsBlocked(current);
			var renewable = current != null && !blocked && CertificationValidity.IsRenewable(type, current, localToday) && LiveOrExpired(current) ? current : null;
			var (start, end) = CertificationValidity.CountingWindow(type, renewable, localToday);
			var fromUtc = TrainingDueDates.StartOfLocalDayUtc(start, department);
			var toUtc = end >= localToday ? utcNow : TrainingDueDates.EndOfLocalDayUtc(end, department);

			var progress = new LmsCertificationProgress
			{
				SetId = set.LmsCertificationRequirementSetId,
				CertificationTypeId = type.DepartmentCertificationTypeId,
				CertificationTypeName = type.Type,
				CertificationTypeCode = type.Code,
				UserId = userId,
				CertificationId = current?.PersonnelCertificationId,
				ExpiresOn = current?.ExpiresOn?.Date,
				DaysUntilExpiry = current?.ExpiresOn == null ? (int?)null : (int)(current.ExpiresOn.Value.Date - localToday).TotalDays,
				WindowStart = start,
				WindowEnd = end,
				IsRenewal = renewable != null
			};
			foreach (var item in (items ?? new List<LmsCertificationRequirementItem>()).OrderBy(i => i.SortOrder).ThenBy(i => i.LmsCertificationRequirementItemId))
			{
				var mine = facts != null && facts.TryGetValue(item.LmsCertificationRequirementItemId, out var found) ? found : new List<LmsRequirementFact>();
				progress.Items.Add(EvaluateItem(item, label?.Invoke(item) ?? item.Label, mine, fromUtc, toUtc));
			}

			if (progress.Complete && progress.CompletedOnUtc.HasValue)
				progress.NextExpiresOn = CertificationValidity.ExpiryFor(type, TrainingDueDates.LocalDate(progress.CompletedOnUtc.Value, department), renewable);
			else
				progress.NextExpiresOn = CertificationValidity.ExpiryFor(type, localToday, renewable);

			if (blocked)
				progress.State = LmsCertificationProgressStates.Blocked;
			else if (progress.Complete)
				progress.State = LmsCertificationProgressStates.Complete;
			else if (current == null || !LiveOrExpired(current))
				progress.State = LmsCertificationProgressStates.NotHeld;
			else if (current.ExpiresOn.HasValue && current.ExpiresOn.Value.Date < localToday)
				progress.State = renewable != null ? LmsCertificationProgressStates.InGrace : LmsCertificationProgressStates.Lapsed;
			else if (current.ExpiresOn.HasValue && set.DueSoonDays > 0 && progress.DaysUntilExpiry <= set.DueSoonDays)
				progress.State = LmsCertificationProgressStates.DueSoon;
			else
				progress.State = LmsCertificationProgressStates.Current;
			return progress;
		}

		/// <summary>A record in force, waiting for verification, a trainee's, or expired (an expired one can still be renewed in its grace).</summary>
		public static bool LiveOrExpired(PersonnelCertification record) =>
			record != null && !record.IsDeleted && (record.Status == (int)PersonnelCertificationStatuses.Active || record.Status == (int)PersonnelCertificationStatuses.PendingVerification ||
			record.Status == (int)PersonnelCertificationStatuses.Trainee || record.Status == (int)PersonnelCertificationStatuses.Expired);
	}

	#endregion Rules
}

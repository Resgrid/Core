using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where a dispatch case review stands (DispatchCaseReviews.Status, M0391). Stored; never renumber.</summary>
	public enum DispatchCaseReviewStatuses
	{
		/// <summary>Sampled into the officers' queue; nothing scored yet.</summary>
		Queued = 0,

		/// <summary>A reviewer saved scores as a draft; the dispatcher does not see it yet.</summary>
		InReview = 1,

		/// <summary>Scored and sent to the dispatcher (notification and "My case reviews"); the scores can no longer change.</summary>
		Sent = 2,

		/// <summary>The dispatcher acknowledged the review (once).</summary>
		Acknowledged = 3
	}

	/// <summary>How a question asked or an instruction given complied with the protocol version as written. Stored in ItemsJson; never renumber.</summary>
	public enum DispatchCaseCompliance
	{
		NotScored = 0,

		/// <summary>Asked (or given) as written: 100 points.</summary>
		Compliant = 1,

		/// <summary>Asked or given, but reworded, out of order or incomplete: 50 points.</summary>
		Partial = 2,

		/// <summary>Not asked, not given, or the answer was recorded wrongly: 0 points.</summary>
		NotCompliant = 3,

		/// <summary>Did not apply on this call (the caller had already answered it, for example): left out of the maths.</summary>
		NotApplicable = 4
	}

	/// <summary>
	/// Protocols P5 QA: one reviewed dispatch case — a call protocol whose case flow (P4, <see cref="CallProtocol.CaseJson"/>) was sampled
	/// for review and scored against a Records QA rubric (<see cref="RmsQualityRubric"/>, reused: criteria, weights and the weighted 0–100
	/// score). The questions asked and instructions given are snapshotted at sampling (<see cref="DispatchCaseReviewItem"/>) and scored one
	/// by one; the per-kind compliance counts are stored so reports add points, not percentages. One review per call protocol
	/// (UX_DispatchCaseReviews_CallProtocol). Department-owned with no foreign keys; UnlinkedDepartmentCleanup and ProtocolDepartmentCleanup
	/// remove it.
	/// </summary>
	[Table("DispatchCaseReviews")]
	public class DispatchCaseReview : IEntity
	{
		public const int CommentsMaxLength = 4000;
		public const int DispatcherCommentMaxLength = 2000;
		public const int NoteMaxLength = 1000;
		public const int CallNameMaxLength = 200;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchCaseReviewId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public int CallProtocolId { get; set; }

		public int DispatchProtocolId { get; set; }

		/// <summary>The protocol version the call cites (the case was run against it).</summary>
		public int? ProtocolVersionId { get; set; }

		/// <summary>The determinant in force on the call when it was sampled.</summary>
		public string Determinant { get; set; }

		/// <summary>The dispatcher had overridden the computed determinant (with a reason) when the call was sampled.</summary>
		public bool Overridden { get; set; }

		/// <summary>Who ran the case: the member who attached the protocol to the call, else the call's reporting member. Gets the feedback.</summary>
		public string DispatcherUserId { get; set; }

		public DateTime CallLoggedOn { get; set; }

		public string CallNumber { get; set; }

		public string CallName { get; set; }

		public string ProtocolCode { get; set; }

		public string ProtocolName { get; set; }

		public string VersionLabel { get; set; }

		/// <summary><see cref="DispatchCaseReviewStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The Records QA rubric scored against (RmsQualityRubrics, DefinitionKey <see cref="DispatchCaseRubrics.DefinitionKey"/>).</summary>
		public string RmsQualityRubricId { get; set; }

		public string RubricName { get; set; }

		/// <summary>The rubric's criteria at sampling (<see cref="RmsQualityCriterion"/> list): editing the rubric never changes a review.</summary>
		public string CriteriaJson { get; set; }

		/// <summary>The reviewer's per-criterion scores (<see cref="RmsQualityFinding"/> list, 0–100), derived criteria included once computed.</summary>
		public string FindingsJson { get; set; }

		/// <summary>The questions asked and instructions given, in order, with their compliance (<see cref="DispatchCaseReviewItem"/> list).</summary>
		public string ItemsJson { get; set; }

		/// <summary>The weighted 0–100 score (<see cref="DispatchCaseScoring.Overall"/>); null until it can be computed.</summary>
		public int? Score { get; set; }

		/// <summary>Scored questions that applied, and their points (100 compliant, 50 partial, 0 not compliant).</summary>
		public int QuestionCount { get; set; }

		public int QuestionPoints { get; set; }

		public int InstructionCount { get; set; }

		public int InstructionPoints { get; set; }

		/// <summary>The officer the queue item is assigned to (by the sampler, or a reviewer who took it).</summary>
		public string AssignedToUserId { get; set; }

		public string SampledByUserId { get; set; }

		public DateTime SampledOn { get; set; }

		public string ReviewerUserId { get; set; }

		public string ReviewerComments { get; set; }

		public DateTime? SentOn { get; set; }

		public DateTime? AcknowledgedOn { get; set; }

		/// <summary>The dispatcher's one reply.</summary>
		public string DispatcherComment { get; set; }

		public DateTime? DispatcherCommentOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public bool IsSent => Status == (int)DispatchCaseReviewStatuses.Sent || Status == (int)DispatchCaseReviewStatuses.Acknowledged;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchCaseReviewId; }
			set { DispatchCaseReviewId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchCaseReviews";

		[NotMapped]
		public string IdName => "DispatchCaseReviewId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsSent" };
	}

	/// <summary>One question asked or instruction given on the case, as it stood in the cited version, and the reviewer's verdict.</summary>
	public sealed class DispatchCaseReviewItem
	{
		[JsonProperty("key")] public string NodeKey { get; set; }

		/// <summary><see cref="ProtocolDecisionNodeKinds"/>: Question or Instruction.</summary>
		[JsonProperty("kind")] public int Kind { get; set; }

		/// <summary><see cref="ProtocolCaseStages"/> of a question.</summary>
		[JsonProperty("stage")] public int? Stage { get; set; }

		[JsonProperty("text")] public string Text { get; set; }

		/// <summary>The answer recorded on the call, in words (questions).</summary>
		[JsonProperty("answer")] public string Answer { get; set; }

		/// <summary><see cref="DispatchCaseCompliance"/>.</summary>
		[JsonProperty("compliance")] public int Compliance { get; set; }

		[JsonProperty("note")] public string Note { get; set; }

		[JsonIgnore] public bool IsQuestion => Kind == (int)ProtocolDecisionNodeKinds.Question;

		[JsonIgnore] public bool IsInstruction => Kind == (int)ProtocolDecisionNodeKinds.Instruction;

		/// <summary>The questions asked and instructions given on a recorded case, in order (outcomes are scored through the determinant criterion).</summary>
		public static List<DispatchCaseReviewItem> FromCase(CallProtocolCase record) =>
			(record?.Steps ?? new List<ProtocolCaseStep>())
				.Where(s => s != null && (s.Kind == (int)ProtocolDecisionNodeKinds.Question || s.Kind == (int)ProtocolDecisionNodeKinds.Instruction))
				.Select(s => new DispatchCaseReviewItem { NodeKey = s.NodeKey, Kind = s.Kind, Stage = s.Stage, Text = s.Text, Answer = s.Answer })
				.ToList();

		/// <summary>
		/// The items as a review stores them: the answers recorded on the call are caller content (ADP catalog 39) and live only in the call's
		/// case record, so a review never keeps a copy.
		/// </summary>
		public static List<DispatchCaseReviewItem> WithoutAnswers(IEnumerable<DispatchCaseReviewItem> items)
		{
			var list = (items ?? Enumerable.Empty<DispatchCaseReviewItem>()).Where(i => i != null).ToList();
			foreach (var item in list)
				item.Answer = null;
			return list;
		}

		/// <summary>The items with each question's answer as the case record (resolved for this reader) holds it.</summary>
		public static List<DispatchCaseReviewItem> FillAnswers(IEnumerable<DispatchCaseReviewItem> items, CallProtocolCase record)
		{
			var list = (items ?? Enumerable.Empty<DispatchCaseReviewItem>()).Where(i => i != null).ToList();
			if (record?.Steps == null)
				return list;
			foreach (var item in list.Where(i => i.NodeKey != null && string.IsNullOrEmpty(i.Answer)))
				item.Answer = record.Steps.FirstOrDefault(s => s != null && string.Equals(s.NodeKey, item.NodeKey, StringComparison.Ordinal))?.Answer;
			return list;
		}

		public static List<DispatchCaseReviewItem> Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<DispatchCaseReviewItem>();
			try
			{
				return (JsonConvert.DeserializeObject<List<DispatchCaseReviewItem>>(json, new JsonSerializerSettings { MaxDepth = 8 }) ?? new List<DispatchCaseReviewItem>())
					.Where(i => i != null).ToList();
			}
			catch (JsonException)
			{
				return new List<DispatchCaseReviewItem>();
			}
		}

		public static string ToJson(IEnumerable<DispatchCaseReviewItem> items) =>
			JsonConvert.SerializeObject((items ?? Enumerable.Empty<DispatchCaseReviewItem>()).ToList(), Formatting.None, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
	}

	/// <summary>
	/// The "dispatch case" rubric template on the Records QA rubric store (P5 QA reuses RmsQualityRubrics: criteria with weights, findings
	/// 0–100, the weighted score). A dispatch case rubric is marked by its DefinitionKey, which can never be a Records definition key (a colon
	/// is not allowed there), so Records sampling never picks it up and the Records rubric list leaves it out. Two criterion keys are derived
	/// rather than scored by hand: <see cref="QuestionsKey"/> and <see cref="InstructionsKey"/> take the compliance of the questions asked and
	/// the instructions given.
	/// </summary>
	public static class DispatchCaseRubrics
	{
		public const string DefinitionKey = "protocol:dispatch-case";
		public const string QuestionsKey = "questions";
		public const string InstructionsKey = "instructions";
		public const int MaxCriteria = 50;

		public static bool IsDispatchRubric(RmsQualityRubric rubric) =>
			rubric != null && string.Equals(rubric.DefinitionKey, DefinitionKey, StringComparison.Ordinal);

		public static bool IsDerived(string key) =>
			string.Equals(key, QuestionsKey, StringComparison.Ordinal) || string.Equals(key, InstructionsKey, StringComparison.Ordinal);

		/// <summary>The template's criteria: case entry, key questions (derived), determinant, instructions (derived), caller care.</summary>
		public static List<RmsQualityCriterion> Template(DispatchCaseRubricTexts texts)
		{
			texts ??= DispatchCaseRubricTexts.English;
			return new List<RmsQualityCriterion>
			{
				new RmsQualityCriterion { Key = "case_entry", Text = texts.CaseEntry, Weight = 2 },
				new RmsQualityCriterion { Key = QuestionsKey, Text = texts.Questions, Weight = 3 },
				new RmsQualityCriterion { Key = "determinant", Text = texts.Determinant, Weight = 3 },
				new RmsQualityCriterion { Key = InstructionsKey, Text = texts.Instructions, Weight = 3 },
				new RmsQualityCriterion { Key = "caller_care", Text = texts.CallerCare, Weight = 1 }
			};
		}

		/// <summary>
		/// Criteria as stored, the Records rules (RecordsQualityReviewService.SaveRubricAsync): text required and trimmed, keys trimmed or
		/// issued (c1, c2…), weights 1–10, at most 50, keys unique. Null when there is no criterion or the keys collide.
		/// </summary>
		public static List<RmsQualityCriterion> Normalize(IEnumerable<RmsQualityCriterion> criteria)
		{
			var items = (criteria ?? Enumerable.Empty<RmsQualityCriterion>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Text))
				.Select((c, i) => new RmsQualityCriterion
				{
					Key = string.IsNullOrWhiteSpace(c.Key) ? $"c{i + 1}" : c.Key.Trim(),
					Text = c.Text.Trim().Length > 500 ? c.Text.Trim().Substring(0, 500) : c.Text.Trim(),
					Weight = Math.Clamp(c.Weight <= 0 ? 1 : c.Weight, 1, 10)
				}).ToList();
			if (items.Count == 0 || items.Count > MaxCriteria)
				return null;
			return items.Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count ? items : null;
		}
	}

	/// <summary>The template criteria in the creating administrator's language.</summary>
	public sealed class DispatchCaseRubricTexts
	{
		public static DispatchCaseRubricTexts English { get; } = new DispatchCaseRubricTexts();

		public string Name { get; set; } = "Dispatch case review";
		public string CaseEntry { get; set; } = "Case entry: address, call-back number and chief complaint confirmed";
		public string Questions { get; set; } = "Questions asked as written, in order";
		public string Determinant { get; set; } = "The right determinant (an override has a sound reason)";
		public string Instructions { get; set; } = "Instructions given as written";
		public string CallerCare { get; set; } = "Calm, clear and courteous with the caller";
	}

	/// <summary>
	/// The scoring maths of a dispatch case review. Items score 100 (compliant), 50 (partial) or 0 (not compliant); not applicable and
	/// unscored items are left out. A derived criterion takes its kind's compliance (points / applicable items) and is left out when no item
	/// of that kind applied. The overall score is the Records QA formula: the weight-averaged criteria, rounded as Math.Round does.
	/// </summary>
	public static class DispatchCaseScoring
	{
		public static int? Points(DispatchCaseCompliance compliance)
		{
			switch (compliance)
			{
				case DispatchCaseCompliance.Compliant: return 100;
				case DispatchCaseCompliance.Partial: return 50;
				case DispatchCaseCompliance.NotCompliant: return 0;
				default: return null;
			}
		}

		/// <summary>Applicable scored items of a kind, and their points.</summary>
		public static (int Count, int Points) Tally(IEnumerable<DispatchCaseReviewItem> items, ProtocolDecisionNodeKinds kind)
		{
			var count = 0;
			var points = 0;
			foreach (var item in items ?? Enumerable.Empty<DispatchCaseReviewItem>())
			{
				if (item == null || item.Kind != (int)kind)
					continue;
				var value = Points((DispatchCaseCompliance)item.Compliance);
				if (!value.HasValue)
					continue;
				count++;
				points += value.Value;
			}
			return (count, points);
		}

		/// <summary>Points over applicable items as a percentage (one decimal); null when nothing applied.</summary>
		public static double? Percent(int count, long points) => count <= 0 ? (double?)null : Math.Round(points / (double)count, 1);

		/// <summary>The value a derived criterion takes from the items (rounded to a whole point); null when no item of its kind applied.</summary>
		public static int? Derived(string key, IEnumerable<DispatchCaseReviewItem> items)
		{
			var kind = string.Equals(key, DispatchCaseRubrics.QuestionsKey, StringComparison.Ordinal) ? ProtocolDecisionNodeKinds.Question
				: string.Equals(key, DispatchCaseRubrics.InstructionsKey, StringComparison.Ordinal) ? ProtocolDecisionNodeKinds.Instruction
				: (ProtocolDecisionNodeKinds?)null;
			if (kind == null)
				return null;
			var (count, points) = Tally(items, kind.Value);
			var percent = Percent(count, points);
			return percent.HasValue ? (int)Math.Round(percent.Value) : (int?)null;
		}

		/// <summary>
		/// The weighted 0–100 score: hand-scored criteria from the findings, derived criteria from the items. Null when a hand-scored criterion
		/// has no finding (not scored yet) or no criterion carries a value.
		/// </summary>
		public static int? Overall(IReadOnlyList<RmsQualityCriterion> criteria, IReadOnlyList<RmsQualityFinding> findings, IReadOnlyList<DispatchCaseReviewItem> items)
		{
			var weight = 0;
			long sum = 0;
			foreach (var criterion in criteria ?? new List<RmsQualityCriterion>())
			{
				if (criterion == null)
					continue;
				int? value;
				if (DispatchCaseRubrics.IsDerived(criterion.Key))
				{
					value = Derived(criterion.Key, items);
					if (!value.HasValue)
						continue;
				}
				else
				{
					var finding = findings?.FirstOrDefault(f => f != null && string.Equals(f.Key, criterion.Key, StringComparison.Ordinal));
					if (finding == null)
						return null;
					value = Math.Clamp(finding.Score, 0, 100);
				}

				var w = Math.Max(criterion.Weight, 1);
				weight += w;
				sum += (long)w * value.Value;
			}

			return weight == 0 ? (int?)null : (int)Math.Round(sum / (double)weight);
		}

		/// <summary>What still needs a score before a review can be sent: unscored items, and hand-scored criteria without a finding.</summary>
		public static List<string> Missing(IReadOnlyList<RmsQualityCriterion> criteria, IReadOnlyList<RmsQualityFinding> findings, IReadOnlyList<DispatchCaseReviewItem> items)
		{
			var missing = new List<string>();
			foreach (var item in items ?? new List<DispatchCaseReviewItem>())
				if (item != null && item.Compliance == (int)DispatchCaseCompliance.NotScored)
					missing.Add(item.NodeKey);
			foreach (var criterion in criteria ?? new List<RmsQualityCriterion>())
				if (criterion != null && !DispatchCaseRubrics.IsDerived(criterion.Key) &&
				    findings?.Any(f => f != null && string.Equals(f.Key, criterion.Key, StringComparison.Ordinal)) != true)
					missing.Add(criterion.Key);
			return missing;
		}
	}

	/// <summary>A call protocol that may be sampled: a case recorded on one of the department's calls (repository projection, no JSON).</summary>
	public sealed class DispatchCaseCandidate
	{
		public int CallProtocolId { get; set; }
		public int CallId { get; set; }
		public int DispatchProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }
		public string Determinant { get; set; }
		public string AttachedByUserId { get; set; }
		public string ReportingUserId { get; set; }
		public DateTime LoggedOn { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public bool Overridden { get; set; }

		/// <summary>A review already exists for this call protocol (excluded from every sample).</summary>
		public bool AlreadyReviewed { get; set; }

		/// <summary>The dispatcher of record: who attached the protocol, else who reported the call.</summary>
		public string DispatcherUserId => !string.IsNullOrWhiteSpace(AttachedByUserId) ? AttachedByUserId : ReportingUserId;
	}

	/// <summary>What the repository filters candidates by (the department and dates always; the rest when set).</summary>
	public sealed class DispatchCaseCandidateFilter
	{
		public DateTime FromUtc { get; set; }
		public DateTime ToUtc { get; set; }
		public int? DispatchProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }

		/// <summary>A determinant or its prefix ("09-E" finds 09-E-01), compared like the column (case-insensitive).</summary>
		public string Determinant { get; set; }

		public string DispatcherUserId { get; set; }
	}

	/// <summary>A sampling request from the officers' Sample page or the API.</summary>
	public sealed class DispatchCaseSampleRequest
	{
		public DateTime FromUtc { get; set; }
		public DateTime ToUtc { get; set; }
		public int? DispatchProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }
		public string Determinant { get; set; }
		public string DispatcherUserId { get; set; }

		/// <summary>A random sample of this many cases (1–<see cref="DispatchCaseSampling.MaxSample"/>), unless <see cref="Percent"/> is set.</summary>
		public int? Count { get; set; }

		/// <summary>A random sample of this percentage of the eligible cases (1–100, rounded up, capped at <see cref="DispatchCaseSampling.MaxSample"/>).</summary>
		public int? Percent { get; set; }

		/// <summary>The dispatch case rubric; null uses the department's first active one (the template is created when there is none).</summary>
		public string RubricId { get; set; }

		/// <summary>Put the sampled cases in this officer's queue; that officer's own calls are then left out.</summary>
		public string AssignToUserId { get; set; }

		/// <summary>The rubric template's texts in the requester's language, used when the template has to be created.</summary>
		public DispatchCaseRubricTexts TemplateTexts { get; set; }
	}

	/// <summary>How a sample went: candidates found, how many were left out and why, and the reviews queued.</summary>
	public sealed class DispatchCaseSampleResult
	{
		public DispatchCaseReviewResults Result { get; set; }
		public int Candidates { get; set; }
		public int AlreadyReviewed { get; set; }
		public int NoDispatcher { get; set; }
		public int AssigneesOwn { get; set; }
		public int Eligible { get; set; }
		public int SampleSize { get; set; }
		public List<DispatchCaseReview> Sampled { get; set; } = new List<DispatchCaseReview>();
		public bool IsPreview { get; set; }
	}

	/// <summary>
	/// The sampling rules (P5 QA): calls by protocol, version, determinant, dispatcher and dates; never a call protocol already reviewed, never
	/// one with no dispatcher to give feedback to, and never the assignee's own calls; then a random N or percentage, drawn by ordering the
	/// eligible cases by SHA-256 of a fresh seed and the call protocol id (the order cannot be steered by whoever runs it).
	/// </summary>
	public static class DispatchCaseSampling
	{
		public const int MaxSample = 100;
		public const int MaxCandidates = 20000;
		public const int MaxRangeDays = 366;

		public static List<DispatchCaseCandidate> Eligible(IEnumerable<DispatchCaseCandidate> candidates, string assignToUserId, DispatchCaseSampleResult tally = null)
		{
			var eligible = new List<DispatchCaseCandidate>();
			foreach (var candidate in candidates ?? Enumerable.Empty<DispatchCaseCandidate>())
			{
				if (candidate == null)
					continue;
				if (candidate.AlreadyReviewed)
				{
					if (tally != null) tally.AlreadyReviewed++;
					continue;
				}
				if (string.IsNullOrWhiteSpace(candidate.DispatcherUserId))
				{
					if (tally != null) tally.NoDispatcher++;
					continue;
				}
				if (!string.IsNullOrWhiteSpace(assignToUserId) && DispatchCaseReviewRules.IsOwnCase(assignToUserId, candidate.AttachedByUserId, candidate.ReportingUserId))
				{
					if (tally != null) tally.AssigneesOwn++;
					continue;
				}
				eligible.Add(candidate);
			}
			return eligible;
		}

		/// <summary>How many to draw: the percentage of the eligible (rounded up) or the count, at least 1, at most <see cref="MaxSample"/> and the eligible.</summary>
		public static int Size(int eligible, int? count, int? percent)
		{
			if (eligible <= 0)
				return 0;
			var wanted = percent.HasValue && percent.Value > 0
				? (int)Math.Ceiling(eligible * Math.Min(percent.Value, 100) / 100.0)
				: Math.Max(count ?? 1, 1);
			return Math.Min(Math.Min(wanted, MaxSample), eligible);
		}

		public static List<DispatchCaseCandidate> Pick(IEnumerable<DispatchCaseCandidate> eligible, int size, string seed)
		{
			if (size <= 0)
				return new List<DispatchCaseCandidate>();
			return (eligible ?? Enumerable.Empty<DispatchCaseCandidate>())
				.OrderBy(c => Hash(seed + "|" + c.CallProtocolId.ToString(CultureInfo.InvariantCulture)), StringComparer.Ordinal)
				.ThenBy(c => c.CallProtocolId)
				.Take(size)
				.ToList();
		}

		public static string NewSeed() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

		private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
	}

	/// <summary>The answer of a review operation. Never persisted.</summary>
	public enum DispatchCaseReviewResults
	{
		Done = 0,
		NotFound = 1,
		/// <summary>The member may not review dispatch cases (or is not the review's dispatcher).</summary>
		Forbidden = 2,
		/// <summary>A reviewer never reviews (or is assigned) their own call.</summary>
		OwnCase = 3,
		/// <summary>The review was sent; its scores no longer change, and it cannot leave the queue.</summary>
		AlreadySent = 4,
		/// <summary>Something still needs a score before the review can be sent.</summary>
		Incomplete = 5,
		AlreadyAcknowledged = 6,
		/// <summary>The dispatcher already replied (one reply).</summary>
		AlreadyCommented = 7,
		/// <summary>The review has not been sent to the dispatcher yet.</summary>
		NotSent = 8,
		/// <summary>Someone else saved the review first, or it is assigned to another reviewer.</summary>
		Conflict = 9,
		CommentRequired = 10,
		/// <summary>No usable dispatch case rubric.</summary>
		NoRubric = 11,
		/// <summary>The request itself is not valid (dates, sizes, a score out of range).</summary>
		Invalid = 12
	}

	/// <summary>Who may do what with dispatch case reviews.</summary>
	public static class DispatchCaseReviewRules
	{
		/// <summary>
		/// The member's own case: they attached the protocol, reported the call, or overrode its determinant. A reviewer never scores it and a
		/// sample assigned to them leaves it out.
		/// </summary>
		public static bool IsOwnCase(string userId, params string[] caseUserIds) =>
			!string.IsNullOrWhiteSpace(userId) && (caseUserIds ?? Array.Empty<string>()).Any(id => !string.IsNullOrWhiteSpace(id) && string.Equals(id.Trim(), userId.Trim(), StringComparison.OrdinalIgnoreCase));

		public static bool IsDispatcherOf(DispatchCaseReview review, string userId) =>
			review != null && !string.IsNullOrWhiteSpace(userId) && string.Equals(review.DispatcherUserId, userId, StringComparison.OrdinalIgnoreCase);

		/// <summary>The dispatcher sees a review only once it was sent, and only their own.</summary>
		public static bool DispatcherMaySee(DispatchCaseReview review, string userId) => IsDispatcherOf(review, userId) && review.IsSent;

		public static bool CanAcknowledge(DispatchCaseReview review, string userId) =>
			IsDispatcherOf(review, userId) && review.Status == (int)DispatchCaseReviewStatuses.Sent;

		public static bool CanComment(DispatchCaseReview review, string userId) =>
			IsDispatcherOf(review, userId) && review.IsSent && string.IsNullOrWhiteSpace(review.DispatcherComment);

		public static bool IsOpen(DispatchCaseReview review) =>
			review != null && (review.Status == (int)DispatchCaseReviewStatuses.Queued || review.Status == (int)DispatchCaseReviewStatuses.InReview);
	}

	/// <summary>What a reviewer posts: each item's compliance and note by node key, each hand-scored criterion's score and note, comments.</summary>
	public sealed class DispatchCaseScoreInput
	{
		public Dictionary<string, DispatchCaseItemScore> Items { get; set; } = new Dictionary<string, DispatchCaseItemScore>(StringComparer.Ordinal);
		public List<RmsQualityFinding> Findings { get; set; } = new List<RmsQualityFinding>();
		public string Comments { get; set; }
	}

	public sealed class DispatchCaseItemScore
	{
		public int Compliance { get; set; }
		public string Note { get; set; }
	}

	/// <summary>A review with everything the review page shows: the case as recorded on the call, the version's graph, the timeline.</summary>
	public sealed class DispatchCaseReviewDetail
	{
		public DispatchCaseReview Review { get; set; }

		/// <summary>ADP catalog 39: the call's case is sealed and this read held no grant, so the answers and reasons are withheld (REDACTED).</summary>
		public bool CaseWithheld { get; set; }
		public List<DispatchCaseReviewItem> Items { get; set; } = new List<DispatchCaseReviewItem>();
		public List<RmsQualityCriterion> Criteria { get; set; } = new List<RmsQualityCriterion>();
		public List<RmsQualityFinding> Findings { get; set; } = new List<RmsQualityFinding>();

		/// <summary>The case as it stands on the call now (an override after sampling shows here).</summary>
		public CallProtocolCase Case { get; set; }

		/// <summary>The cited version's case flow, when it is valid.</summary>
		public ProtocolDecisionGraph Graph { get; set; }

		public DateTime? AttachedOn { get; set; }
		public int? CallPriority { get; set; }
		public ProtocolStepLog StepLog { get; set; }

		public bool IsReviewer { get; set; }
		public bool IsDispatcher { get; set; }
		public bool IsOwnCase { get; set; }
		public bool CanScore { get; set; }
		public bool CanAcknowledge { get; set; }
		public bool CanComment { get; set; }
		public double? QuestionCompliance { get; set; }
		public double? InstructionCompliance { get; set; }
	}

	/// <summary>The officers' queue counts.</summary>
	public sealed class DispatchCaseReviewSummary
	{
		public int Queued { get; set; }
		public int InReview { get; set; }
		public int AwaitingAcknowledgment { get; set; }
		public int Acknowledged { get; set; }
		public int AssignedToMe { get; set; }
	}

	/// <summary>The compliance report's filter: sent reviews of calls logged in the window.</summary>
	public sealed class DispatchCaseReportFilter
	{
		public DateTime FromUtc { get; set; }
		public DateTime ToUtc { get; set; }
		public int? DispatchProtocolId { get; set; }
		public int? ProtocolVersionId { get; set; }
		public string DispatcherUserId { get; set; }
	}

	/// <summary>One line of the compliance report.</summary>
	public sealed class DispatchCaseComplianceRow
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public int Reviews { get; set; }
		public double? AverageScore { get; set; }
		public double? QuestionCompliance { get; set; }
		public double? InstructionCompliance { get; set; }
		public int Overrides { get; set; }
		public int Acknowledged { get; set; }
		public int QuestionCount { get; set; }
		public long QuestionPoints { get; set; }
		public int InstructionCount { get; set; }
		public long InstructionPoints { get; set; }
	}

	/// <summary>Compliance by protocol, version, dispatcher and month, and (for one protocol) by question and instruction.</summary>
	public sealed class DispatchCaseComplianceReport
	{
		public DispatchCaseReportFilter Filter { get; set; }
		public DispatchCaseComplianceRow Total { get; set; } = new DispatchCaseComplianceRow { Key = "total" };
		public List<DispatchCaseComplianceRow> ByProtocol { get; set; } = new List<DispatchCaseComplianceRow>();
		public List<DispatchCaseComplianceRow> ByVersion { get; set; } = new List<DispatchCaseComplianceRow>();
		public List<DispatchCaseComplianceRow> ByDispatcher { get; set; } = new List<DispatchCaseComplianceRow>();
		public List<DispatchCaseComplianceRow> ByMonth { get; set; } = new List<DispatchCaseComplianceRow>();
		public List<DispatchCaseComplianceRow> ByItem { get; set; } = new List<DispatchCaseComplianceRow>();
		public List<DispatchCaseReview> Reviews { get; set; } = new List<DispatchCaseReview>();
	}

	/// <summary>
	/// The report aggregation (pure): points are added across reviews before dividing, so a review with ten questions weighs ten times one
	/// with a single question in question compliance, while the average score is the mean of the reviews' scores.
	/// </summary>
	public static class DispatchCaseReports
	{
		public static DispatchCaseComplianceReport Aggregate(IReadOnlyList<DispatchCaseReview> reviews, DispatchCaseReportFilter filter,
			Func<string, string> dispatcherName, Func<DateTime, DateTime> toLocal)
		{
			var rows = (reviews ?? new List<DispatchCaseReview>()).Where(r => r != null && r.IsSent).ToList();
			dispatcherName ??= id => id;
			toLocal ??= d => d;
			var report = new DispatchCaseComplianceReport { Filter = filter, Reviews = rows.OrderByDescending(r => r.CallLoggedOn).ToList() };
			report.Total = Row("total", null, rows);
			report.ByProtocol = rows.GroupBy(r => r.DispatchProtocolId)
				.Select(g => Row(g.Key.ToString(CultureInfo.InvariantCulture), Label(g.First().ProtocolCode, g.First().ProtocolName), g.ToList()))
				.OrderBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
			report.ByVersion = rows.GroupBy(r => r.ProtocolVersionId ?? 0)
				.Select(g => Row(g.Key.ToString(CultureInfo.InvariantCulture), Label(g.First().ProtocolCode, g.First().ProtocolName) + (string.IsNullOrWhiteSpace(g.First().VersionLabel) ? string.Empty : " v" + g.First().VersionLabel), g.ToList()))
				.OrderBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
			report.ByDispatcher = rows.GroupBy(r => (r.DispatcherUserId ?? string.Empty).ToUpperInvariant())
				.Select(g => Row(g.First().DispatcherUserId, dispatcherName(g.First().DispatcherUserId) ?? g.First().DispatcherUserId, g.ToList()))
				.OrderBy(r => r.AverageScore ?? 101).ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
			report.ByMonth = rows.GroupBy(r => toLocal(r.CallLoggedOn).ToString("yyyy-MM", CultureInfo.InvariantCulture))
				.Select(g => Row(g.Key, g.Key, g.ToList()))
				.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();

			// Per question and instruction only for one protocol: node keys mean something only inside one protocol's graph.
			if (filter?.DispatchProtocolId != null)
			{
				var tallies = new Dictionary<string, DispatchCaseComplianceRow>(StringComparer.Ordinal);
				foreach (var review in rows.OrderBy(r => r.SentOn ?? r.CallLoggedOn))
				{
					foreach (var item in DispatchCaseReviewItem.Parse(review.ItemsJson))
					{
						var points = DispatchCaseScoring.Points((DispatchCaseCompliance)item.Compliance);
						if (string.IsNullOrWhiteSpace(item.NodeKey) || !points.HasValue)
							continue;
						if (!tallies.TryGetValue(item.NodeKey, out var row))
							tallies[item.NodeKey] = row = new DispatchCaseComplianceRow { Key = item.NodeKey };
						row.Label = item.Text;
						row.Reviews++;
						if (item.IsQuestion) { row.QuestionCount++; row.QuestionPoints += points.Value; }
						else { row.InstructionCount++; row.InstructionPoints += points.Value; }
					}
				}
				foreach (var row in tallies.Values)
				{
					row.QuestionCompliance = DispatchCaseScoring.Percent(row.QuestionCount, row.QuestionPoints);
					row.InstructionCompliance = DispatchCaseScoring.Percent(row.InstructionCount, row.InstructionPoints);
				}
				report.ByItem = tallies.Values.OrderBy(r => r.QuestionCompliance ?? r.InstructionCompliance ?? 101).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
			}

			return report;
		}

		private static string Label(string code, string name) => string.IsNullOrWhiteSpace(code) ? name ?? string.Empty : code + " " + (name ?? string.Empty);

		private static DispatchCaseComplianceRow Row(string key, string label, IReadOnlyList<DispatchCaseReview> reviews)
		{
			var scored = reviews.Where(r => r.Score.HasValue).ToList();
			var row = new DispatchCaseComplianceRow
			{
				Key = key,
				Label = label ?? key,
				Reviews = reviews.Count,
				AverageScore = scored.Count == 0 ? (double?)null : Math.Round(scored.Average(r => r.Score.Value), 1),
				Overrides = reviews.Count(r => r.Overridden),
				Acknowledged = reviews.Count(r => r.Status == (int)DispatchCaseReviewStatuses.Acknowledged),
				QuestionCount = reviews.Sum(r => r.QuestionCount),
				QuestionPoints = reviews.Sum(r => (long)r.QuestionPoints),
				InstructionCount = reviews.Sum(r => r.InstructionCount),
				InstructionPoints = reviews.Sum(r => (long)r.InstructionPoints)
			};
			row.QuestionCompliance = DispatchCaseScoring.Percent(row.QuestionCount, row.QuestionPoints);
			row.InstructionCompliance = DispatchCaseScoring.Percent(row.InstructionCount, row.InstructionPoints);
			return row;
		}
	}
}

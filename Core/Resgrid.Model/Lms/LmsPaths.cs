using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Lms
{
	/// <summary>What a path step asks the learner to finish. Persisted (LmsPathSteps.Kind): append only.</summary>
	public enum LmsPathStepKinds
	{
		/// <summary>An LMS course or a Resgrid Academy lesson (both are LmsCourses). TargetRefId is the LmsCourseId.</summary>
		Course = 0,

		/// <summary>A baseline training. TargetRefId is the TrainingId.</summary>
		Training = 1
	}

	/// <summary>A step's state for one learner.</summary>
	public enum LmsPathStepStates
	{
		/// <summary>A prerequisite is not done yet.</summary>
		Locked = 0,

		/// <summary>Unlocked and not done.</summary>
		Available = 1,

		Done = 2
	}

	/// <summary>
	/// A learning path (T3 item 4): ordered steps (courses, Academy lessons, baseline trainings) with prerequisites and optional elective
	/// groups; finishing it can issue a certification. Department paths (DepartmentId &gt; 0) are LMS; the Resgrid Academy role paths are
	/// system paths (DepartmentId 0, <see cref="SystemKey"/> "academy:path:{key}") seeded from <see cref="AcademyCourseManifest.RolePaths"/>,
	/// which run for every department (R-S2). A1's ordered-list assignments became these paths without re-assignment: progress is read from
	/// the learner's completions, so nothing was lost.
	/// </summary>
	[Table("LmsPaths")]
	public class LmsPath : IEntity
	{
		public const string SystemKeyPrefix = "academy:path:";

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsPathId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>The Academy role path's system key; null for department paths.</summary>
		public string SystemKey { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		/// <summary>Finishing the path issues or renews a certification of this type (the T2 certification issue path).</summary>
		public int? IssuesCertificationTypeId { get; set; }

		public bool IsDeleted { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsPathId; }
			set { LmsPathId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsPaths";

		[NotMapped]
		public string IdName => "LmsPathId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		[NotMapped]
		[JsonIgnore]
		public bool IsSystem => DepartmentId == 0 && !string.IsNullOrWhiteSpace(SystemKey);

		/// <summary>The PathKey its assignments carry (LmsPathAssignments.PathKey): the Academy key, or "dept-{id}".</summary>
		[NotMapped]
		[JsonIgnore]
		public string AssignmentKey => IsSystem ? SystemKey.Substring(SystemKeyPrefix.Length) : "dept-" + LmsPathId.ToString(CultureInfo.InvariantCulture);

		public static string SystemKeyFor(string academyPathKey) => SystemKeyPrefix + academyPathKey;
	}

	/// <summary>
	/// One step of a path. <see cref="SortOrder"/> is its 1-based position; <see cref="PrerequisiteOrders"/> lists the positions of earlier
	/// steps it needs (so a path cannot hold a cycle). Steps sharing an <see cref="ElectiveGroup"/> are electives: the learner needs
	/// <see cref="ElectiveRequired"/> of them. Progress is read from completions of the targets, never stored per step, so steps can be
	/// re-saved freely.
	/// </summary>
	[Table("LmsPathSteps")]
	public class LmsPathStep : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LmsPathStepId { get; set; }

		public int LmsPathId { get; set; }

		public int DepartmentId { get; set; }

		public int SortOrder { get; set; }

		/// <summary><see cref="LmsPathStepKinds"/>.</summary>
		public int Kind { get; set; }

		public int TargetRefId { get; set; }

		/// <summary>The Academy lesson's course system key, so a system step survives a re-seed.</summary>
		public string TargetSystemKey { get; set; }

		/// <summary>Comma-separated positions of earlier steps this one needs.</summary>
		public string PrerequisiteOrders { get; set; }

		public int? ElectiveGroup { get; set; }

		public int ElectiveRequired { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return LmsPathStepId; }
			set { LmsPathStepId = (int)value; }
		}

		[NotMapped]
		public string TableName => "LmsPathSteps";

		[NotMapped]
		public string IdName => "LmsPathStepId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		public IReadOnlyList<int> Prerequisites() => LmsPathRules.ParseOrders(PrerequisiteOrders).Where(o => o < SortOrder).ToList();
	}

	/// <summary>One step as a learner sees it.</summary>
	public sealed class LmsPathStepProgress
	{
		public LmsPathStep Step { get; set; }

		public LmsPathStepStates State { get; set; }

		public DateTime? CompletedOn { get; set; }
	}

	/// <summary>A learner's progress on a path.</summary>
	public sealed class LmsPathProgressResult
	{
		public List<LmsPathStepProgress> Steps { get; set; } = new List<LmsPathStepProgress>();

		/// <summary>Steps that count toward completion (required steps plus the electives each group needs).</summary>
		public int Needed { get; set; }

		/// <summary>Counted steps done.</summary>
		public int Done { get; set; }

		public bool Complete { get; set; }

		/// <summary>When the last counted step was done (the path's completion moment).</summary>
		public DateTime? CompletedOn { get; set; }

		public int Percent => Needed <= 0 ? 100 : (int)Math.Floor(Done * 100m / Needed);
	}

	/// <summary>The path rules as pure functions: prerequisites, electives, completion, and the step list's validation.</summary>
	public static class LmsPathRules
	{
		public const int MaxSteps = 60;

		public static IReadOnlyList<int> ParseOrders(string csv) =>
			(csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(p => int.TryParse(p.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).Where(v => v > 0).Distinct().OrderBy(v => v).ToList();

		public static string FormatOrders(IEnumerable<int> orders) =>
			string.Join(",", (orders ?? Enumerable.Empty<int>()).Where(o => o > 0).Distinct().OrderBy(o => o).Select(o => o.ToString(CultureInfo.InvariantCulture)));

		/// <summary>
		/// A learner's progress: a step is done when its target was completed; available when every prerequisite is done; locked
		/// otherwise. The path is complete when every required step is done and each elective group has the electives it needs.
		/// <paramref name="completions"/> maps (kind, target id) to the learner's latest completion of it.
		/// </summary>
		public static LmsPathProgressResult Compute(IEnumerable<LmsPathStep> steps, IReadOnlyDictionary<(int Kind, int RefId), DateTime> completions)
		{
			var result = new LmsPathProgressResult();
			var ordered = (steps ?? Enumerable.Empty<LmsPathStep>()).Where(s => s != null).OrderBy(s => s.SortOrder).ThenBy(s => s.LmsPathStepId).ToList();
			var doneOn = new Dictionary<int, DateTime>();
			foreach (var step in ordered)
				if (completions != null && completions.TryGetValue((step.Kind, step.TargetRefId), out var on))
					doneOn[step.SortOrder] = on;

			foreach (var step in ordered)
			{
				var progress = new LmsPathStepProgress { Step = step };
				if (doneOn.TryGetValue(step.SortOrder, out var on))
				{
					progress.State = LmsPathStepStates.Done;
					progress.CompletedOn = on;
				}
				else
				{
					progress.State = step.Prerequisites().All(doneOn.ContainsKey) ? LmsPathStepStates.Available : LmsPathStepStates.Locked;
				}

				result.Steps.Add(progress);
			}

			var counted = new List<DateTime>();
			var required = result.Steps.Where(p => !p.Step.ElectiveGroup.HasValue).ToList();
			result.Needed = required.Count;
			result.Done = required.Count(p => p.State == LmsPathStepStates.Done);
			counted.AddRange(required.Where(p => p.CompletedOn.HasValue).Select(p => p.CompletedOn.Value));
			var complete = result.Done == required.Count;

			foreach (var group in result.Steps.Where(p => p.Step.ElectiveGroup.HasValue).GroupBy(p => p.Step.ElectiveGroup.Value))
			{
				var need = Math.Clamp(group.Max(p => p.Step.ElectiveRequired), 1, group.Count());
				var done = group.Where(p => p.State == LmsPathStepStates.Done).OrderBy(p => p.CompletedOn).ToList();
				result.Needed += need;
				result.Done += Math.Min(need, done.Count);
				counted.AddRange(done.Take(need).Select(p => p.CompletedOn.Value));
				if (done.Count < need)
					complete = false;
			}

			result.Complete = complete && result.Steps.Count > 0;
			result.CompletedOn = result.Complete && counted.Count > 0 ? counted.Max() : (DateTime?)null;
			return result;
		}

		/// <summary>Null when the steps make a valid path; otherwise the reason (an <see cref="LmsErrors"/> name for the view).</summary>
		public static string Validate(IReadOnlyList<LmsPathStep> steps)
		{
			if (steps == null || steps.Count == 0)
				return "PathNoSteps";
			if (steps.Count > MaxSteps)
				return "PathTooManySteps";
			var positions = steps.Select(s => s.SortOrder).ToList();
			if (positions.Distinct().Count() != positions.Count || positions.Any(p => p < 1 || p > steps.Count))
				return "PathInvalidOrder";
			if (steps.Select(s => (s.Kind, s.TargetRefId)).Distinct().Count() != steps.Count)
				return "PathDuplicateStep";
			foreach (var step in steps)
			{
				if (step.TargetRefId <= 0 || !Enum.IsDefined(typeof(LmsPathStepKinds), step.Kind))
					return "PathInvalidStep";
				if (ParseOrders(step.PrerequisiteOrders).Any(o => o >= step.SortOrder))
					return "PathPrerequisiteNotEarlier";
			}

			foreach (var group in steps.Where(s => s.ElectiveGroup.HasValue).GroupBy(s => s.ElectiveGroup.Value))
				if (group.Max(s => s.ElectiveRequired) < 1 || group.Max(s => s.ElectiveRequired) > group.Count())
					return "PathElectiveCount";

			return null;
		}

		/// <summary>The Academy role path as steps: the lessons in manifest order, every later lesson needing the first ("start here").</summary>
		public static List<LmsPathStep> AcademySteps(AcademyRolePath path, Func<string, LmsCourse> courseForSystemKey)
		{
			var steps = new List<LmsPathStep>();
			if (path == null || courseForSystemKey == null)
				return steps;
			foreach (var lessonId in path.LessonIds)
			{
				var systemKey = AcademyCourseManifest.SystemKeyFor(lessonId);
				var course = courseForSystemKey(systemKey);
				if (course == null)
					continue;
				var order = steps.Count + 1;
				steps.Add(new LmsPathStep
				{
					DepartmentId = 0,
					SortOrder = order,
					Kind = (int)LmsPathStepKinds.Course,
					TargetRefId = course.LmsCourseId,
					TargetSystemKey = systemKey,
					PrerequisiteOrders = order == 1 ? null : "1"
				});
			}

			return steps;
		}
	}
}

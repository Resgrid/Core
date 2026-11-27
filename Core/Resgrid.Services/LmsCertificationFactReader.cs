using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;

namespace Resgrid.Services
{
	/// <summary>
	/// Round 9A: the completions behind certification requirement items, read once per kind through the learning-requirement fact queries
	/// (T3): course completions (enrolments), baseline training completions, category hours and passed skill evaluations from the ledger, and
	/// finished learning paths (course sets) computed with the path rules. Optionally for one member, and never counting the voided records
	/// a re-evaluation names. Shared by the issuance engine and the requirement-set service (no service dependency between them).
	/// </summary>
	public sealed class LmsCertificationFactReader
	{
		private readonly ILmsRequirementFactsRepository _facts;
		private readonly ILmsPathStepsRepository _steps;

		public LmsCertificationFactReader(ILmsRequirementFactsRepository facts, ILmsPathStepsRepository steps)
		{
			_facts = facts;
			_steps = steps;
		}

		/// <summary>The facts of every item, keyed by item id (all members, or <paramref name="userId"/> only).</summary>
		public async Task<Dictionary<int, List<LmsRequirementFact>>> ForItemsAsync(int departmentId, IReadOnlyCollection<LmsCertificationRequirementItem> items, string userId,
			ICollection<int> excludedRecordIds, DateTime hoursFromUtc)
		{
			var result = new Dictionary<int, List<LmsRequirementFact>>();
			var list = (items ?? Array.Empty<LmsCertificationRequirementItem>()).Where(i => i != null).ToList();
			if (list.Count == 0)
				return result;

			IEnumerable<int> Targets(LmsRequirementKinds kind) => list.Where(i => i.Kind == (int)kind && i.TargetRefId.HasValue).Select(i => i.TargetRefId.Value).Distinct();
			var byKind = new Dictionary<LmsRequirementKinds, List<LmsRequirementFact>>();
			var courseIds = Targets(LmsRequirementKinds.Course).ToList();
			if (courseIds.Count > 0)
				byKind[LmsRequirementKinds.Course] = await _facts.GetCourseCompletionsWithRecordsAsync(departmentId, courseIds) ?? new List<LmsRequirementFact>();
			var trainingIds = Targets(LmsRequirementKinds.Training).ToList();
			if (trainingIds.Count > 0)
				byKind[LmsRequirementKinds.Training] = await _facts.GetTrainingCompletionsAsync(departmentId, trainingIds) ?? new List<LmsRequirementFact>();
			var categoryIds = Targets(LmsRequirementKinds.CategoryHours).ToList();
			if (categoryIds.Count > 0)
				byKind[LmsRequirementKinds.CategoryHours] = await _facts.GetCategoryHoursAsync(departmentId, categoryIds, hoursFromUtc) ?? new List<LmsRequirementFact>();
			var codes = list.Where(i => i.Kind == (int)LmsRequirementKinds.Evaluations && !string.IsNullOrWhiteSpace(i.TargetCode)).Select(i => i.TargetCode.Trim()).Distinct().ToList();
			if (codes.Count > 0)
				byKind[LmsRequirementKinds.Evaluations] = await _facts.GetEvaluationsAsync(departmentId, codes) ?? new List<LmsRequirementFact>();
			var pathIds = Targets(LmsRequirementKinds.Path).ToList();
			if (pathIds.Count > 0)
				byKind[LmsRequirementKinds.Path] = await PathCompletionsAsync(departmentId, pathIds, userId, excludedRecordIds);

			foreach (var item in list)
			{
				var facts = byKind.TryGetValue((LmsRequirementKinds)item.Kind, out var found) ? found : new List<LmsRequirementFact>();
				result[item.LmsCertificationRequirementItemId] = LmsCertificationRequirementEvaluator.FactsFor(item, Filter(facts, userId, excludedRecordIds)).ToList();
			}

			return result;
		}

		/// <summary>The members' latest completion of each course (an enrolment completed), never counting an excluded record.</summary>
		public async Task<List<LmsRequirementFact>> CourseCompletionsAsync(int departmentId, int lmsCourseId, string userId, ICollection<int> excludedRecordIds) =>
			Filter(await _facts.GetCourseCompletionsWithRecordsAsync(departmentId, new[] { lmsCourseId }) ?? new List<LmsRequirementFact>(), userId, excludedRecordIds).ToList();

		/// <summary>
		/// Finished learning paths: each member's latest completion of every step (course or training), then the path rules; the fact carries
		/// every step record that made the path (a void of any of them re-evaluates what the path issued).
		/// </summary>
		public async Task<List<LmsRequirementFact>> PathCompletionsAsync(int departmentId, IReadOnlyCollection<int> pathIds, string userId, ICollection<int> excludedRecordIds)
		{
			var facts = new List<LmsRequirementFact>();
			var ids = (pathIds ?? Array.Empty<int>()).Distinct().ToList();
			if (ids.Count == 0)
				return facts;
			var steps = (await _steps.GetForPathsAsync(ids) ?? new List<LmsPathStep>()).ToLookup(s => s.LmsPathId);
			var all = steps.SelectMany(g => g).ToList();
			var stepFacts = new List<(int Kind, LmsRequirementFact Fact)>();
			var courseIds = all.Where(s => s.Kind == (int)LmsPathStepKinds.Course).Select(s => s.TargetRefId).Distinct().ToList();
			if (courseIds.Count > 0)
				stepFacts.AddRange(Filter(await _facts.GetCourseCompletionsWithRecordsAsync(departmentId, courseIds) ?? new List<LmsRequirementFact>(), userId, excludedRecordIds)
					.Select(f => ((int)LmsPathStepKinds.Course, f)));
			var trainingIds = all.Where(s => s.Kind == (int)LmsPathStepKinds.Training).Select(s => s.TargetRefId).Distinct().ToList();
			if (trainingIds.Count > 0)
				stepFacts.AddRange(Filter(await _facts.GetTrainingCompletionsAsync(departmentId, trainingIds) ?? new List<LmsRequirementFact>(), userId, excludedRecordIds)
					.Select(f => ((int)LmsPathStepKinds.Training, f)));

			foreach (var member in stepFacts.GroupBy(f => f.Fact.UserId, StringComparer.OrdinalIgnoreCase))
			{
				var latest = new Dictionary<(int, int), LmsRequirementFact>();
				foreach (var (kind, fact) in member)
				{
					var key = (kind, fact.RefId);
					if (!latest.TryGetValue(key, out var seen) || fact.CompletedOn > seen.CompletedOn)
						latest[key] = fact;
				}

				var dates = latest.ToDictionary(p => p.Key, p => p.Value.CompletedOn);
				foreach (var pathId in ids)
				{
					var pathSteps = steps[pathId].ToList();
					if (pathSteps.Count == 0)
						continue;
					var progress = LmsPathRules.Compute(pathSteps, dates);
					if (!progress.Complete || !progress.CompletedOn.HasValue)
						continue;
					var records = progress.Steps.Where(s => s.State == LmsPathStepStates.Done)
						.Select(s => latest.TryGetValue((s.Step.Kind, s.Step.TargetRefId), out var f) ? f.LearningRecordId : null)
						.Where(id => id.HasValue).Select(id => id.Value).Distinct().ToList();
					facts.Add(new LmsRequirementFact { UserId = member.Key, RefId = pathId, CompletedOn = progress.CompletedOn.Value, RecordIds = records });
				}
			}

			return facts;
		}

		private static IEnumerable<LmsRequirementFact> Filter(IEnumerable<LmsRequirementFact> facts, string userId, ICollection<int> excluded) =>
			(facts ?? Enumerable.Empty<LmsRequirementFact>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.UserId) &&
				(userId == null || string.Equals(f.UserId, userId, StringComparison.OrdinalIgnoreCase)) &&
				(excluded == null || excluded.Count == 0 || (!(f.LearningRecordId.HasValue && excluded.Contains(f.LearningRecordId.Value)) &&
				                                              !(f.RecordIds != null && f.RecordIds.Any(excluded.Contains)))));
	}
}

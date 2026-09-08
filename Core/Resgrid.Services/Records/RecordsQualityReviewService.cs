using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Post-finalization quality review (RMS plan section 4.7, RMS-4 optional module). A reviewer scores a deterministic
	/// sample of finalized Records against a department rubric; findings attach to the Record as a non-mutating QA note
	/// and trend by author, unit and definition. Findings never alter the finalized revision — a substantive
	/// correction is an amendment, which the lifecycle already handles.
	/// </summary>
	public class RecordsQualityReviewService : IRecordsQualityReviewService
	{
		private static readonly int[] FinalStates = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Submitted, (int)RmsRecordState.Accepted };

		private readonly RecordsPreventionGate _gate;
		private readonly IRmsQualityRubricsRepository _rubrics;
		private readonly IRmsQualityReviewsRepository _reviews;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordUnitResponsesRepository _units;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsProtectionService _protection;
		private readonly IRmsAccessAuditsRepository _audits;

		public RecordsQualityReviewService(RecordsPreventionGate gate, IRmsQualityRubricsRepository rubrics, IRmsQualityReviewsRepository reviews, IRmsOperationalRecordsRepository records,
			IRmsRecordUnitResponsesRepository units, IRecordsAuthorizationService authorization, IRecordsProtectionService protection, IRmsAccessAuditsRepository audits)
		{
			_gate = gate; _rubrics = rubrics; _reviews = reviews; _records = records; _units = units; _authorization = authorization; _protection = protection; _audits = audits;
		}

		public Task<bool> IsModuleEnabledAsync(int departmentId) => _gate.IsEnabledAsync(departmentId, RecordsPreventionModule.QualityReview);

		private async Task RequireReviewerAsync(int departmentId, string userId)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.QualityReview);
			await _gate.RequireViewerAsync(departmentId, userId);
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ReviewRecords)) throw new UnauthorizedAccessException("The review permission is required.");
		}

		private async Task RequireAdminAsync(int departmentId, string userId)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.QualityReview);
			if (!await _gate.IsDepartmentAdminAsync(departmentId, userId)) throw new UnauthorizedAccessException("Only a department administrator manages rubrics.");
		}

		public async Task<List<RmsQualityRubric>> GetRubricsAsync(int departmentId, string userId, bool includeInactive)
		{
			await RequireReviewerAsync(departmentId, userId);
			return (await _rubrics.GetForDepartmentAsync(departmentId, includeInactive))?.ToList() ?? new List<RmsQualityRubric>();
		}

		public async Task<RmsQualityRubric> GetRubricAsync(int departmentId, string userId, string rubricId)
		{
			await RequireReviewerAsync(departmentId, userId);
			var rubric = await _rubrics.GetByIdForDepartmentAsync(departmentId, rubricId);
			return rubric == null || rubric.DeletedOn != null ? null : rubric;
		}

		public async Task<RmsQualityRubric> SaveRubricAsync(int departmentId, string userId, RmsQualityRubric input, List<RmsQualityCriterion> criteria, CancellationToken cancellationToken = default)
		{
			await RequireAdminAsync(departmentId, userId);
			if (input == null) throw new ArgumentNullException(nameof(input));
			var items = (criteria ?? new List<RmsQualityCriterion>()).Where(c => c != null && !string.IsNullOrWhiteSpace(c.Text)).Select((c, i) => new RmsQualityCriterion { Key = string.IsNullOrWhiteSpace(c.Key) ? $"c{i + 1}" : c.Key.Trim(), Text = c.Text.Trim(), Weight = Math.Clamp(c.Weight <= 0 ? 1 : c.Weight, 1, 10) }).ToList();
			if (items.Count == 0) throw new ArgumentException("A rubric needs at least one criterion.");
			if (items.Count > 50) throw new ArgumentException("A rubric holds at most 50 criteria.");
			if (items.Select(i => i.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Count) throw new ArgumentException("Criterion keys must be unique.");
			var now = DateTime.UtcNow;
			var entity = string.IsNullOrWhiteSpace(input.RmsQualityRubricId) ? null : await _rubrics.GetByIdForDepartmentAsync(departmentId, input.RmsQualityRubricId);
			var isNew = entity == null;
			if (isNew) entity = new RmsQualityRubric { RmsQualityRubricId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 1 };
			else entity.RowVersion++;
			entity.Name = RecordsPreventionGate.Require(input.Name, 200, "A rubric needs a name.");
			entity.DefinitionKey = RecordsPreventionGate.Trim(input.DefinitionKey, 100); entity.CriteriaJson = JsonConvert.SerializeObject(items);
			entity.SampleSize = Math.Clamp(input.SampleSize <= 0 ? 10 : input.SampleSize, 1, 200); entity.IsActive = input.IsActive; entity.ModifiedOn = now;
			if (isNew) await _rubrics.InsertAsync(entity, cancellationToken, true); else await _rubrics.UpdateAsync(entity, cancellationToken, true);
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, isNew ? "QA rubric created" : "QA rubric updated", entity.RmsQualityRubricId, new { entity.Name, criteria = items.Count, entity.SampleSize }, cancellationToken: cancellationToken);
			return entity;
		}

		public static List<RmsQualityCriterion> ParseCriteria(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsQualityCriterion>();
			try { return JsonConvert.DeserializeObject<List<RmsQualityCriterion>>(json) ?? new List<RmsQualityCriterion>(); }
			catch (JsonException) { return new List<RmsQualityCriterion>(); }
		}

		public static List<RmsQualityFinding> ParseFindings(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return new List<RmsQualityFinding>();
			try { return JsonConvert.DeserializeObject<List<RmsQualityFinding>>(json) ?? new List<RmsQualityFinding>(); }
			catch (JsonException) { return new List<RmsQualityFinding>(); }
		}

		/// <summary>
		/// Deterministic sampling: candidates are finalized operational records since the window start, not yet reviewed,
		/// ordered by SHA-256(rubric, day, record id). The same day and rubric produce the same sample, so two reviewers
		/// clicking "sample" do not double up, and the ordering cannot be steered by whoever runs it.
		/// </summary>
		public async Task<List<RmsQualityReview>> SampleAsync(int departmentId, string userId, string rubricId, DateTime sinceUtc, CancellationToken cancellationToken = default)
		{
			await RequireReviewerAsync(departmentId, userId);
			var rubric = await _rubrics.GetByIdForDepartmentAsync(departmentId, rubricId);
			if (rubric == null || rubric.DeletedOn != null || !rubric.IsActive) throw new ArgumentException("Choose an active rubric.");
			var criteria = ParseCriteria(rubric.CriteriaJson);
			if (criteria.Count == 0) throw new InvalidOperationException("The rubric has no criteria.");
			var candidates = ((await _records.GetFinalizedSinceAsync(departmentId, sinceUtc)) ?? Enumerable.Empty<RmsOperationalRecord>())
				.Where(r => r.DeletedOn == null && FinalStates.Contains(r.State) && (rubric.DefinitionKey == null || string.Equals(r.DefinitionKey, rubric.DefinitionKey, StringComparison.OrdinalIgnoreCase)))
				.ToList();
			if (candidates.Count == 0) return new List<RmsQualityReview>();
			var reviewed = ((await _reviews.GetReviewedRecordIdsAsync(departmentId, candidates.Select(c => c.RmsOperationalRecordId))) ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
			var day = DateTime.UtcNow.ToString("yyyyMMdd");
			var picked = candidates.Where(c => !reviewed.Contains(c.RmsOperationalRecordId)).OrderBy(c => Hash($"{rubricId}|{day}|{c.RmsOperationalRecordId}"), StringComparer.Ordinal).Take(rubric.SampleSize).ToList();
			if (picked.Count == 0) return new List<RmsQualityReview>();
			var units = ((await _units.GetForRecordsAsync(departmentId, picked.Select(p => p.RmsOperationalRecordId))) ?? Enumerable.Empty<RmsRecordUnitResponse>()).GroupBy(u => u.RecordId).ToDictionary(g => g.Key, g => g.First().UnitId);
			var now = DateTime.UtcNow;
			var result = new List<RmsQualityReview>();
			foreach (var record in picked)
			{
				var review = new RmsQualityReview
				{
					RmsQualityReviewId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), RmsQualityRubricId = rubricId, RecordId = record.RmsOperationalRecordId, RecordKind = (int)RmsRecordKind.Operational,
					RevisionId = record.CurrentRevisionId, DefinitionKey = record.DefinitionKey, RecordNumber = record.RecordNumber, AuthorUserId = record.AuthorUserId, UnitId = units.TryGetValue(record.RmsOperationalRecordId, out var unitId) ? unitId : (int?)null,
					SampledOn = now, CriteriaJson = rubric.CriteriaJson, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				};
				await _protection.ProtectQualityReviewAsync(departmentId, review, null, userId, cancellationToken);
				await _reviews.InsertAsync(review, cancellationToken, true);
				result.Add(review);
			}
			await _gate.AuditAsync(departmentId, userId, RmsAccessAuditAction.Admin, "QA sample drawn", rubricId, new { sampled = result.Count, candidates = candidates.Count, since = sinceUtc }, cancellationToken: cancellationToken);
			return result;
		}

		private static string Hash(string value)
		{
			using var sha = SHA256.Create();
			return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
		}

		public async Task<List<RmsQualityReview>> GetPendingAsync(int departmentId, string userId, int take)
		{
			await RequireReviewerAsync(departmentId, userId);
			var rows = (await _reviews.GetPendingAsync(departmentId, take))?.ToList() ?? new List<RmsQualityReview>();
			var visible = new List<RmsQualityReview>();
			foreach (var row in rows)
				if (await _authorization.CanUserViewRecordAsync(userId, row.RecordId, departmentId)) visible.Add(row);
			return visible;
		}

		public async Task<RmsQualityReview> GetReviewAsync(int departmentId, string userId, string reviewId)
		{
			await RequireReviewerAsync(departmentId, userId);
			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId);
			if (review == null) return null;
			if (!await _authorization.CanUserViewRecordAsync(userId, review.RecordId, departmentId)) throw new UnauthorizedAccessException("You cannot view the reviewed record.");
			await _protection.RevealQualityReviewsAsync(departmentId, new[] { review });
			return review;
		}

		public async Task<RmsQualityReview> ScoreAsync(int departmentId, string userId, string reviewId, List<RmsQualityFinding> findings, string note, bool amendmentRecommended, CancellationToken cancellationToken = default)
		{
			await RequireReviewerAsync(departmentId, userId);
			var review = await _reviews.GetByIdForDepartmentAsync(departmentId, reviewId) ?? throw new ArgumentException("The review does not exist.");
			if (!await _authorization.CanUserViewRecordAsync(userId, review.RecordId, departmentId)) throw new UnauthorizedAccessException("You cannot view the reviewed record.");
			if (review.ScoredOn.HasValue) throw new InvalidOperationException("The review was already scored.");
			if (string.Equals(review.AuthorUserId, userId, StringComparison.Ordinal)) throw new InvalidOperationException("An author does not score their own record.");
			var criteria = ParseCriteria(review.CriteriaJson);
			findings = (findings ?? new List<RmsQualityFinding>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Key)).ToList();
			var missing = criteria.Where(c => !findings.Any(f => f.Key == c.Key)).Select(c => c.Key).ToList();
			if (missing.Count > 0) throw new ArgumentException($"Every criterion needs a score: {string.Join(", ", missing)}.");
			foreach (var f in findings) { f.Score = Math.Clamp(f.Score, 0, 100); f.Note = RecordsPreventionGate.Trim(f.Note, 2000); }
			var weight = criteria.Sum(c => c.Weight);
			var score = weight == 0 ? 0 : (int)Math.Round(criteria.Sum(c => c.Weight * findings.First(f => f.Key == c.Key).Score) / (double)weight);
			var now = DateTime.UtcNow;
			var existing = new RmsQualityReview { FindingsJson = review.FindingsJson, Note = review.Note };
			review.ReviewerUserId = userId; review.ScoredOn = now; review.Score = score; review.FindingsJson = JsonConvert.SerializeObject(findings.Where(f => criteria.Any(c => c.Key == f.Key)).ToList());
			review.Note = RecordsPreventionGate.Trim(note, 8000); review.AmendmentRecommended = amendmentRecommended; review.ModifiedOn = now; review.RowVersion++;
			var plaintext = PlaintextSnapshot<RmsQualityReview>.Take(review, RmsProtectedFields.QualityReviews);
			await _protection.ProtectQualityReviewAsync(departmentId, review, existing, userId, cancellationToken);
			await _reviews.UpdateAsync(review, cancellationToken, true);
			plaintext.Restore();
			// The QA note is attached to the record's audit trail, never to the record itself.
			await _audits.InsertAsync(new RmsAccessAudit { DepartmentId = departmentId, RecordId = review.RecordId, RevisionId = review.RevisionId, ActorUserId = userId, Action = (int)RmsAccessAuditAction.Read, Successful = true, OccurredOn = now, Purpose = "Quality review scored", OriginClient = (int)RmsOriginClient.Web, DetailJson = JsonConvert.SerializeObject(new { reviewId, score, amendmentRecommended }) }, cancellationToken, true);
			return review;
		}

		public async Task<List<RmsQualityReview>> GetForRecordAsync(int departmentId, string userId, string recordId)
		{
			await _gate.RequireEnabledAsync(departmentId, RecordsPreventionModule.QualityReview);
			if (!await _authorization.CanUserViewRecordAsync(userId, recordId, departmentId)) throw new UnauthorizedAccessException("You cannot view that record.");
			var rows = (await _reviews.GetForRecordAsync(departmentId, recordId))?.ToList() ?? new List<RmsQualityReview>();
			await _protection.RevealQualityReviewsAsync(departmentId, rows);
			return rows;
		}

		public async Task<RecordsQualityTrends> GetTrendsAsync(int departmentId, string userId, DateTime sinceUtc)
		{
			await RequireReviewerAsync(departmentId, userId);
			var scored = ((await _reviews.GetScoredSinceAsync(departmentId, sinceUtc, 5000)) ?? Enumerable.Empty<RmsQualityReview>()).Where(r => r.Score.HasValue).ToList();
			var trends = new RecordsQualityTrends { Since = sinceUtc, Scored = scored.Count, Sampled = scored.Count + ((await _reviews.GetPendingAsync(departmentId, 1000))?.Count() ?? 0), AverageScore = scored.Count == 0 ? 0 : Math.Round(scored.Average(r => r.Score.Value), 1) };
			List<RecordsQualityTrendRow> Rows(Func<RmsQualityReview, string> key) => scored.Where(r => key(r) != null).GroupBy(key).Select(g => new RecordsQualityTrendRow { Key = g.Key, Label = g.Key, Reviews = g.Count(), AverageScore = Math.Round(g.Average(r => r.Score.Value), 1), AmendmentsRecommended = g.Count(r => r.AmendmentRecommended) }).OrderBy(r => r.AverageScore).ToList();
			trends.ByAuthor = Rows(r => r.AuthorUserId);
			trends.ByUnit = Rows(r => r.UnitId?.ToString());
			trends.ByDefinition = Rows(r => r.DefinitionKey);
			// Criterion trend needs the plaintext findings; concealed rows (no grant) are skipped rather than counted as zero.
			await _protection.RevealQualityReviewsAsync(departmentId, scored);
			var byCriterion = new Dictionary<string, (int Count, long Total, string Text)>(StringComparer.Ordinal);
			foreach (var review in scored)
			{
				var criteria = ParseCriteria(review.CriteriaJson).ToDictionary(c => c.Key, c => c.Text, StringComparer.Ordinal);
				foreach (var finding in ParseFindings(review.FindingsJson))
				{
					(int Count, long Total, string Text) current = byCriterion.TryGetValue(finding.Key, out var v) ? v : (0, 0L, criteria.TryGetValue(finding.Key, out var text) ? text : finding.Key);
					byCriterion[finding.Key] = (current.Count + 1, current.Total + finding.Score, current.Text);
				}
			}
			trends.ByCriterion = byCriterion.Select(kv => new RecordsQualityTrendRow { Key = kv.Key, Label = kv.Value.Text, Reviews = kv.Value.Count, AverageScore = kv.Value.Count == 0 ? 0 : Math.Round(kv.Value.Total / (double)kv.Value.Count, 1) }).OrderBy(r => r.AverageScore).ToList();
			return trends;
		}
	}
}

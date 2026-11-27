using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;

namespace Resgrid.Tests.TrainingT1
{
	/// <summary>An in-memory ledger with the database's idempotency rule (one live record per key; voided records free it).</summary>
	public class FakeLearningRecordsRepository : ILearningRecordsRepository
	{
		public readonly List<LearningRecord> Rows = new List<LearningRecord>();
		private int _next = 1;

		private static bool SameKey(LearningRecord a, LearningRecord b) =>
			a.DepartmentId == b.DepartmentId && a.SourceKind == b.SourceKind && a.SourceRefId == b.SourceRefId &&
			string.Equals(a.UserId, b.UserId, StringComparison.OrdinalIgnoreCase) && string.Equals(a.ComponentKey ?? "", b.ComponentKey ?? "", StringComparison.Ordinal);

		private static LearningRecord Copy(LearningRecord r) => (LearningRecord)typeof(LearningRecord).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(r, null);

		public IEnumerable<LearningRecord> Live => Rows.Where(r => !r.IsVoided);

		public Task<LearningRecord> GetByIdForDepartmentAsync(int departmentId, int learningRecordId) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.LearningRecordId == learningRecordId).Select(Copy).FirstOrDefault());

		public Task<List<LearningRecord>> GetByIdsForDepartmentAsync(int departmentId, IEnumerable<int> learningRecordIds) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && learningRecordIds.Contains(r.LearningRecordId)).Select(Copy).ToList());

		public Task<List<LearningRecord>> GetBySourceAsync(int departmentId, int sourceKind, string sourceRefId) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.SourceKind == sourceKind && r.SourceRefId == sourceRefId).Select(Copy).ToList());

		public Task<HashSet<string>> GetExistingSourceRefIdsAsync(int departmentId, int sourceKind, IEnumerable<string> sourceRefIds) =>
			Task.FromResult(new HashSet<string>(Live.Where(r => r.DepartmentId == departmentId && r.SourceKind == sourceKind && sourceRefIds.Contains(r.SourceRefId)).Select(r => r.SourceRefId)));

		public Task<List<LearningRecord>> GetBySourceRefIdsAsync(int departmentId, int sourceKind, IEnumerable<string> sourceRefIds) =>
			Task.FromResult(Live.Where(r => r.DepartmentId == departmentId && r.SourceKind == sourceKind && sourceRefIds.Contains(r.SourceRefId)).Select(Copy).ToList());

		public Task<List<LearningRecord>> GetBySessionKeyAsync(int departmentId, string sessionKey) =>
			Task.FromResult(Live.Where(r => r.DepartmentId == departmentId && r.SessionKey == sessionKey && sessionKey != null).Select(Copy).ToList());

		public Task<List<LearningRecord>> QueryAsync(LearningRecordQuery query)
		{
			IEnumerable<LearningRecord> rows = Rows.Where(r => r.DepartmentId == query.DepartmentId);
			if (query.UserIds != null && query.UserIds.Count > 0) rows = rows.Where(r => query.UserIds.Contains(r.UserId, StringComparer.OrdinalIgnoreCase));
			if (query.From.HasValue) rows = rows.Where(r => r.CompletedOn >= query.From.Value);
			if (query.To.HasValue) rows = rows.Where(r => r.CompletedOn < query.To.Value);
			if (query.UncategorizedOnly) rows = rows.Where(r => !r.CategoryId.HasValue);
			else if (query.CategoryIds != null && query.CategoryIds.Count > 0) rows = rows.Where(r => r.CategoryId.HasValue && query.CategoryIds.Contains(r.CategoryId.Value));
			if (query.SourceKinds != null && query.SourceKinds.Count > 0) rows = rows.Where(r => query.SourceKinds.Contains(r.SourceKind));
			if (query.Statuses != null && query.Statuses.Count > 0) rows = rows.Where(r => query.Statuses.Contains(r.Status));
			else if (!query.IncludeVoided) rows = rows.Where(r => !r.IsVoided);
			if (query.UnresolvedDuplicatesOnly) rows = rows.Where(r => r.PossibleDuplicateOfId.HasValue && !r.DuplicateResolvedOn.HasValue);
			// Round 9B: review states (AwaitingReview also matches a submission without one) and the reviewer inbox's oldest-first order.
			if (query.ReviewStates != null && query.ReviewStates.Count > 0)
				rows = rows.Where(r => r.ReviewState.HasValue ? query.ReviewStates.Contains(r.ReviewState.Value) : query.ReviewStates.Contains((int)TrainingSubmissionReviewStates.AwaitingReview));
			rows = query.OldestSubmittedFirst
				? rows.OrderBy(r => r.SubmittedOn ?? r.CreatedOn).ThenBy(r => r.LearningRecordId)
				: rows.OrderByDescending(r => r.CompletedOn);
			return Task.FromResult(rows.Skip(query.Skip).Take(query.Take <= 0 ? 5000 : query.Take).Select(Copy).ToList());
		}

		public async Task<int> CountAsync(LearningRecordQuery query) => (await QueryAsync(query)).Count;

		public Task<List<LearningRecord>> GetForMemberInWindowAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc) =>
			Task.FromResult(Live.Where(r => r.DepartmentId == departmentId && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase) && r.CompletedOn >= fromUtc && r.CompletedOn < toUtc).Select(Copy).ToList());

		public Task<(LearningRecord Record, bool Inserted)> InsertIfAbsentAsync(LearningRecord record, CancellationToken cancellationToken = default)
		{
			var existing = Live.FirstOrDefault(r => SameKey(r, record));
			if (existing != null)
				return Task.FromResult((Copy(existing), false));
			record.LearningRecordId = _next++;
			Rows.Add(Copy(record));
			return Task.FromResult((record, true));
		}

		public Task<bool> VoidAsync(int departmentId, int learningRecordId, string reason, string userId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.LearningRecordId == learningRecordId && !r.IsVoided);
			if (row == null) return Task.FromResult(false);
			row.Status = (int)LearningRecordStatuses.Voided;
			row.VoidReason = reason;
			row.VoidedOn = utcNow;
			row.VoidedByUserId = userId;
			return Task.FromResult(true);
		}

		public Task<int> UpdateColumnsAsync(LearningRecord record, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.LearningRecordId == record.LearningRecordId);
			if (row == null) return Task.FromResult(0);
			foreach (var column in columns)
			{
				var property = typeof(LearningRecord).GetProperty(column);
				property.SetValue(row, property.GetValue(record));
			}
			return Task.FromResult(1);
		}

		public Task<int> SetCategoryAsync(int departmentId, IEnumerable<int> learningRecordIds, int? categoryId, CancellationToken cancellationToken = default)
		{
			var count = 0;
			foreach (var row in Live.Where(r => r.DepartmentId == departmentId && learningRecordIds.Contains(r.LearningRecordId)))
			{
				row.CategoryId = categoryId;
				count++;
			}
			return Task.FromResult(count);
		}

		public Task<List<LearningRecordGroup>> GetUncategorizedGroupsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc) =>
			Task.FromResult(Live.Where(r => r.DepartmentId == departmentId && !r.CategoryId.HasValue).GroupBy(r => (r.Title, r.CourseCode ?? ""))
				.Select(g => new LearningRecordGroup { Title = g.Key.Title, CourseCode = g.Key.Item2, Count = g.Count(), Hours = g.Sum(r => r.Hours), FirstOn = g.Min(r => r.CompletedOn), LastOn = g.Max(r => r.CompletedOn) }).ToList());

		public Task<int> CountUnresolvedDuplicatesAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc) =>
			Task.FromResult(Live.Count(r => r.DepartmentId == departmentId && r.HasUnresolvedDuplicate));

		public Task<List<int>> GetDepartmentIdsAwaitingReviewAsync(int take) =>
			Task.FromResult(Rows.Where(r => r.SourceKind == (int)LearningRecordSourceKinds.ExternalSubmission && r.Status == (int)LearningRecordStatuses.Pending &&
				(!r.ReviewState.HasValue || r.ReviewState == (int)TrainingSubmissionReviewStates.AwaitingReview)).Select(r => r.DepartmentId).Distinct().OrderBy(d => d).Take(take).ToList());

		public Task<LearningRecord> GetByVerificationCodeAsync(string verificationCode) =>
			Task.FromResult(Rows.FirstOrDefault(r => string.Equals(r.VerificationCode, verificationCode, StringComparison.OrdinalIgnoreCase)));

		// ---- IRepository members the services do not use on the ledger
		public Task<IEnumerable<LearningRecord>> GetAllAsync() => Task.FromResult<IEnumerable<LearningRecord>>(Rows);
		public Task<LearningRecord> GetByIdAsync(object id) => Task.FromResult(Rows.FirstOrDefault(r => r.LearningRecordId == (int)id));
		public Task<IEnumerable<LearningRecord>> GetAllByDepartmentIdAsync(int departmentId) => Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId));
		public Task<LearningRecord> InsertAsync(LearningRecord entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException("Use InsertIfAbsentAsync.");
		public Task<LearningRecord> UpdateAsync(LearningRecord entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException("Use UpdateColumnsAsync.");
		public Task<bool> DeleteAsync(LearningRecord entity, CancellationToken cancellationToken) => throw new NotSupportedException("The ledger is never deleted row by row.");
		public Task<LearningRecord> SaveOrUpdateAsync(LearningRecord entity, CancellationToken cancellationToken, bool firstLevelOnly = false) => throw new NotSupportedException();
		public Task<IEnumerable<LearningRecord>> GetAllByUserIdAsync(string userId) => Task.FromResult(Rows.Where(r => r.UserId == userId));
		public Task<bool> DeleteMultipleAsync(LearningRecord entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
	}
}

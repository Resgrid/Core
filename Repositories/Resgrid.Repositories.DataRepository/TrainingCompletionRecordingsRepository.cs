using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Round 7B (M0400): recorded completions. Portable SQL over RmsRepositoryBase; user ids are citext on PostgreSQL, so their parameters are
	/// cast (pg-citext-text-param-equality); id lists use InList (postgres-dapper-in-list).
	/// </summary>
	public class TrainingCompletionRecordingsRepository : RmsRepositoryBase<TrainingCompletionRecording>, ITrainingCompletionRecordingsRepository
	{
		public TrainingCompletionRecordingsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<TrainingCompletionRecording> GetByIdForDepartmentAsync(int departmentId, int trainingCompletionRecordingId) =>
			QueryFirstOrDefaultAsync<TrainingCompletionRecording>(
				$"SELECT * FROM {Tbl("TrainingCompletionRecordings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TrainingCompletionRecordingId")} = {P}Id",
				new { DepartmentId = departmentId, Id = trainingCompletionRecordingId });

		public async Task<List<TrainingCompletionRecording>> GetByIdsAsync(int departmentId, IEnumerable<int> trainingCompletionRecordingIds)
		{
			var ids = InListValue((trainingCompletionRecordingIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct());
			if (ids.Length == 0)
				return new List<TrainingCompletionRecording>();
			return (await QueryAsync<TrainingCompletionRecording>(
				$"SELECT * FROM {Tbl("TrainingCompletionRecordings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("TrainingCompletionRecordingId", "Ids")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public async Task<List<TrainingCompletionRecording>> GetForUserAsync(int departmentId, string userId) =>
			string.IsNullOrWhiteSpace(userId) ? new List<TrainingCompletionRecording>() :
			(await QueryAsync<TrainingCompletionRecording>(
				$"SELECT * FROM {Tbl("TrainingCompletionRecordings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("RecordedOn")} DESC, {Col("TrainingCompletionRecordingId")} DESC", new { DepartmentId = departmentId, UserId = userId.Trim() })).ToList();

		public async Task<List<TrainingCompletionRecording>> GetForTargetAsync(int departmentId, int targetKind, int targetId) =>
			(await QueryAsync<TrainingCompletionRecording>(
				$"SELECT * FROM {Tbl("TrainingCompletionRecordings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TargetKind")} = {P}Kind " +
				$"AND {Col("TargetId")} = {P}TargetId ORDER BY {Col("RecordedOn")} DESC, {Col("TrainingCompletionRecordingId")} DESC",
				new { DepartmentId = departmentId, Kind = targetKind, TargetId = targetId })).ToList();

		public async Task<List<TrainingCompletionRecording>> GetForSourceAsync(int departmentId, int sourceKind, string sourceRefId) =>
			string.IsNullOrWhiteSpace(sourceRefId) ? new List<TrainingCompletionRecording>() :
			(await QueryAsync<TrainingCompletionRecording>(
				$"SELECT * FROM {Tbl("TrainingCompletionRecordings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}Kind " +
				$"AND {Col("SourceRefId")} = {P}Ref ORDER BY {Col("RecordedOn")} DESC, {Col("TrainingCompletionRecordingId")} DESC",
				new { DepartmentId = departmentId, Kind = sourceKind, Ref = sourceRefId.Trim() })).ToList();
	}
}

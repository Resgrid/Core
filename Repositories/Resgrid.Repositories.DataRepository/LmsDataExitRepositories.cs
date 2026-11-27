using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Training T5 data exit (round 6D, M0395). Portable SQL over RmsRepositoryBase; the export rows never carry the zip, which lives in the
	// LMS store or, on an install without one, in LmsDataExportBlobs (read only by the download).

	public class LmsDataExportsRepository : RmsRepositoryBase<LmsDataExport>, ILmsDataExportsRepository
	{
		public LmsDataExportsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Take() => IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";

		public async Task<List<LmsDataExport>> GetForDepartmentAsync(int departmentId, int take = 50) =>
			(await QueryAsync<LmsDataExport>(
				$"SELECT * FROM {Tbl("LmsDataExports")} WHERE {Col("DepartmentId")} = {P}DepartmentId " +
				$"ORDER BY {Col("RequestedOn")} DESC, {Col("LmsDataExportId")} DESC {Take()}",
				new { DepartmentId = departmentId, Take = Math.Clamp(take, 1, 500) })).ToList();

		public Task<LmsDataExport> GetByIdForDepartmentAsync(int departmentId, int lmsDataExportId) =>
			QueryFirstOrDefaultAsync<LmsDataExport>(
				$"SELECT * FROM {Tbl("LmsDataExports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsDataExportId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsDataExportId });

		public async Task<List<LmsDataExport>> GetQueuedAsync(int take) =>
			(await QueryAsync<LmsDataExport>(
				$"SELECT * FROM {Tbl("LmsDataExports")} WHERE {Col("Status")} = {P}Queued ORDER BY {Col("RequestedOn")}, {Col("LmsDataExportId")} {Take()}",
				new { Queued = (int)LmsDataExportStatuses.Queued, Take = Math.Clamp(take, 1, 100) })).ToList();

		public async Task<bool> TryClaimAsync(int lmsDataExportId, DateTime utcNow, CancellationToken cancellationToken = default) =>
			await ExecuteAsync(
				$"UPDATE {Tbl("LmsDataExports")} SET {Col("Status")} = {P}Running, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				$"WHERE {Col("LmsDataExportId")} = {P}Id AND {Col("Status")} = {P}Queued",
				new { Id = lmsDataExportId, Now = DatabaseTimestamp(utcNow), Running = (int)LmsDataExportStatuses.Running, Queued = (int)LmsDataExportStatuses.Queued },
				cancellationToken) == 1;

		public Task<int> RequeueStaleAsync(DateTime claimedBeforeUtc, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"UPDATE {Tbl("LmsDataExports")} SET {Col("Status")} = {P}Queued, {Col("ClaimedOn")} = NULL " +
				$"WHERE {Col("Status")} = {P}Running AND {Col("ClaimedOn")} < {P}Before",
				new { Before = DatabaseTimestamp(claimedBeforeUtc), Running = (int)LmsDataExportStatuses.Running, Queued = (int)LmsDataExportStatuses.Queued },
				cancellationToken);

		public async Task<List<LmsDataExport>> GetExpiredUnpurgedAsync(DateTime utcNow, int take) =>
			(await QueryAsync<LmsDataExport>(
				$"SELECT * FROM {Tbl("LmsDataExports")} WHERE {Col("Status")} = {P}Completed AND {Col("PurgedOn")} IS NULL AND {Col("ExpiresOn")} <= {P}Now " +
				$"ORDER BY {Col("ExpiresOn")}, {Col("LmsDataExportId")} {Take()}",
				new { Completed = (int)LmsDataExportStatuses.Completed, Now = DatabaseTimestamp(utcNow), Take = Math.Clamp(take, 1, 500) })).ToList();
	}

	public class LmsDataExportBlobsRepository : RmsRepositoryBase<LmsDataExportBlob>, ILmsDataExportBlobsRepository
	{
		public LmsDataExportBlobsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<byte[]> GetDataAsync(int departmentId, int lmsDataExportId) =>
			(await QueryAsync<LmsDataExportBlob>(
				$"SELECT * FROM {Tbl("LmsDataExportBlobs")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsDataExportId")} = {P}Id ORDER BY {Col("LmsDataExportBlobId")} DESC",
				new { DepartmentId = departmentId, Id = lmsDataExportId })).FirstOrDefault()?.Data;

		public Task<int> DeleteForExportAsync(int departmentId, int lmsDataExportId, CancellationToken cancellationToken = default) =>
			ExecuteAsync(
				$"DELETE FROM {Tbl("LmsDataExportBlobs")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("LmsDataExportId")} = {P}Id",
				new { DepartmentId = departmentId, Id = lmsDataExportId }, cancellationToken);
	}

	/// <summary>
	/// The data exit reader: SELECT * of one department's rows from the tables the export writes, in id order. Only allow-listed tables are
	/// read this way — none of them holds file bytes (learner uploads, evidence and certification files stay out; the export lists them
	/// as omitted). System-published (Academy) rows have DepartmentId 0 and never match a department.
	/// </summary>
	public class LmsDataExportSourceRepository : RmsRepositoryBase<LmsDataExport>, ILmsDataExportSourceRepository
	{
		private static readonly HashSet<string> AllowedTables = new HashSet<string>(StringComparer.Ordinal)
		{
			"LmsCourses", "LmsCourseVersions", "LmsCourseModules", "LmsLessons", "LmsCourseAudiences", "LmsMediaAssets", "LmsQuestionBanks", "LmsQuestions",
			"LmsAssessmentAttempts", "LmsEnrollments", "LearningRecords", "TrainingCategories", "LmsLessonPlans", "LmsLessonPlanVersions", "LmsSkillSheets",
			"LmsSkillSheetVersions", "LmsTaskBookDefinitions", "LmsRequirements", "LmsRequirementAudiences", "LmsTrainingPlans", "LmsTrainingPlanItems",
			"LmsPaths", "LmsPathSteps"
		};

		/// <summary>The tables the data exit reads (for the real-database checks).</summary>
		public static IReadOnlyCollection<string> ExportedTables => AllowedTables;

		public LmsDataExportSourceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<TRow>> GetDepartmentRowsAsync<TRow>(int departmentId, CancellationToken cancellationToken = default) where TRow : class, IEntity, new()
		{
			var shape = new TRow();
			if (!AllowedTables.Contains(shape.TableName))
				throw new InvalidOperationException("The data exit does not read " + shape.TableName + ".");
			if (departmentId <= 0)
				return new List<TRow>();

			return (await QueryAsync<TRow>(
				$"SELECT * FROM {Tbl(shape.TableName)} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col(shape.IdName)}",
				new { DepartmentId = departmentId }, cancellationToken)).ToList();
		}
	}
}

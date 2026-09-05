using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// NERIS incident report aggregate (registry M0164–M0166). Same dual-dialect style as RmsRepositories: every
	/// query starts at DepartmentId, drafts are RevisionId IS NULL, revision copies are immutable.
	/// </summary>
	public class RmsIncidentReportsRepository : RmsRepositoryBase<RmsIncidentReport>, IRmsIncidentReportsRepository
	{
		public RmsIncidentReportsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsIncidentReport> GetByIdForDepartmentAsync(int departmentId, string reportId)
		{
			return QueryFirstOrDefaultAsync<RmsIncidentReport>(
				$"SELECT * FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentReportId")} = {P}ReportId",
				new { DepartmentId = departmentId, ReportId = reportId });
		}

		public Task<RmsIncidentReport> GetByCallAsync(int departmentId, int callId, string reportingEntityId)
		{
			return QueryFirstOrDefaultAsync<RmsIncidentReport>(
				$"SELECT * FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("ReportingEntityId")} = {P}ReportingEntityId AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, CallId = callId, ReportingEntityId = reportingEntityId ?? string.Empty });
		}

		public Task<IEnumerable<RmsIncidentReport>> GetByCallAnyEntityAsync(int departmentId, int callId)
		{
			return QueryAsync<RmsIncidentReport>(
				$"SELECT * FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CallId")} = {P}CallId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")}",
				new { DepartmentId = departmentId, CallId = callId });
		}

		public Task<RmsIncidentReport> GetByIdempotencyKeyAsync(int departmentId, string idempotencyKey)
		{
			return QueryFirstOrDefaultAsync<RmsIncidentReport>(
				$"SELECT * FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IdempotencyKey")} = {P}Key",
				new { DepartmentId = departmentId, Key = idempotencyKey });
		}

		public Task<RmsIncidentReport> GetByNerisIncidentIdAsync(int departmentId, string nerisIncidentId)
		{
			return QueryFirstOrDefaultAsync<RmsIncidentReport>(
				$"SELECT * FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("NerisIncidentId")} = {P}NerisId AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, NerisId = nerisIncidentId });
		}

		public Task<IEnumerable<RmsIncidentReport>> GetRetentionCandidatesAsync(int departmentId, DateTime cutoffUtc, int take, string afterId = null)
		{
			// Closed states only: an open filing is never a retention candidate, however old.
			var states = new[] { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Accepted, (int)RmsRecordState.Voided, (int)RmsRecordState.Cancelled };
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("States", InListValue(states));
			parameters.Add("Cutoff", cutoffUtc);
			parameters.Add("AfterId", afterId);
			parameters.Add("Skip", 0);
			parameters.Add("Take", Math.Clamp(take, 1, 10000));
			return QueryAsync<RmsIncidentReport>(
				$@"SELECT * FROM {Tbl("RmsIncidentReports")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("State", "States")}
					AND {Col("FinalizedOn")} IS NOT NULL AND {Col("FinalizedOn")} < {P}Cutoff AND {Col("DeletedOn")} IS NULL
					AND {Col("PurgedOn")} IS NULL AND ({P}AfterId IS NULL OR {Col("RmsIncidentReportId")} > {P}AfterId)
					ORDER BY {Col("RmsIncidentReportId")} {Paging()}", parameters);
		}

		/// <summary>
		/// The predicate for a report query. Columns are aliased to <c>r</c> so the group-scope EXISTS can correlate
		/// back to the outer row: RmsRecordGroupScopes also has a DepartmentId, and an unqualified reference inside
		/// the subquery would bind to the subquery's own column and quietly drop the department correlation.
		/// </summary>
		private (string where, DynamicParameters parameters) Filter(int departmentId, RmsIncidentReportQuery query)
		{
			const string R = "r";
			string C(string column) => R + "." + Col(column);

			var where = new StringBuilder($"{C("DepartmentId")} = {P}DepartmentId AND {C("DeletedOn")} IS NULL AND {C("PurgedOn")} IS NULL");
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", departmentId);

			if (query.States != null && query.States.Count > 0)
			{
				where.Append($" AND {InList("State", "States", R)}");
				parameters.Add("States", InListValue(query.States));
			}
			if (query.Year.HasValue)
			{
				where.Append($" AND {YearOf(C("CreatedOn"))} = {P}Year");
				parameters.Add("Year", query.Year.Value);
			}
			if (query.CallId.HasValue)
			{
				where.Append($" AND {C("CallId")} = {P}CallId");
				parameters.Add("CallId", query.CallId.Value);
			}
			if (!string.IsNullOrWhiteSpace(query.OwnerUserId))
			{
				where.Append($" AND {C("OwnerUserId")} = {P}OwnerUserId");
				parameters.Add("OwnerUserId", query.OwnerUserId);
			}
			if (query.StationGroupId.HasValue)
			{
				where.Append($" AND {C("StationGroupId")} = {P}StationGroupId");
				parameters.Add("StationGroupId", query.StationGroupId.Value);
			}

			if (query.VisibleGroupIds != null)
			{
				// Mirrors RecordsAuthorizationService.CanUserViewRecordAsync for an incident report: always visible to
				// the author, owner or reviewer, otherwise only through an intersecting group scope. A member in no
				// groups still sees their own reports, which is why the group clause is appended conditionally.
				var viewer = query.ViewerUserId ?? string.Empty;
				where.Append(" AND (")
					.Append($"{C("AuthorUserId")} = {P}Viewer OR {C("OwnerUserId")} = {P}Viewer OR {C("ReviewerUserId")} = {P}Viewer");
				parameters.Add("Viewer", viewer);

				if (query.VisibleGroupIds.Count > 0)
				{
					where.Append($" OR EXISTS (SELECT 1 FROM {Tbl("RmsRecordGroupScopes")} s WHERE s.{Col("DepartmentId")} = {C("DepartmentId")} AND s.{Col("RecordId")} = {C("RmsIncidentReportId")} AND {InList("DepartmentGroupId", "VisibleGroupIds", "s")} AND {EffectiveGroupScope("s")})");
					parameters.Add("VisibleGroupIds", InListValue(query.VisibleGroupIds));
				}

				where.Append(")");
			}

			return (where.ToString(), parameters);
		}

		public Task<IEnumerable<RmsIncidentReport>> QueryAsync(int departmentId, RmsIncidentReportQuery query)
		{
			query ??= new RmsIncidentReportQuery();
			var (where, parameters) = Filter(departmentId, query);
			parameters.Add("Skip", Math.Max(0, query.Skip));
			parameters.Add("Take", Math.Clamp(query.Take, 1, 10000));
			return QueryAsync<RmsIncidentReport>(
				$"SELECT r.* FROM {Tbl("RmsIncidentReports")} r WHERE {where} ORDER BY r.{Col("CreatedOn")} DESC, r.{Col("RmsIncidentReportId")} {Paging()}", parameters);
		}

		public Task<int> CountAsync(int departmentId, RmsIncidentReportQuery query)
		{
			var (where, parameters) = Filter(departmentId, query ?? new RmsIncidentReportQuery());
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsIncidentReports")} r WHERE {where}", parameters);
		}

		public Task<IEnumerable<int>> GetYearsAsync(int departmentId)
		{
			return QueryAsync<int>(
				$"SELECT DISTINCT {YearOf(Col("CreatedOn"))} AS Y FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY Y DESC",
				new { DepartmentId = departmentId });
		}

		public Task<int> GetMaxRecordNumberSequenceAsync(int departmentId, string numberPrefix)
		{
			// Numbers are "{prefix}-{sequence}"; the sequence is the trailing numeric segment. A row whose suffix
			// is missing or non-numeric is ignored on both dialects: PostgreSQL's pattern simply does not match,
			// and SQL Server needs the CASE plus TRY_CAST or it raises a conversion error instead.
			return ScalarAsync<int>(
				IsPostgres
					? $"SELECT COALESCE(MAX(CAST(SUBSTRING({Col("RecordNumber")} FROM '[0-9]+$') AS INTEGER)), 0) FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordNumber")} LIKE {P}Prefix"
					: $"SELECT ISNULL(MAX(CASE WHEN CHARINDEX('-', REVERSE({Col("RecordNumber")})) > 1 THEN TRY_CAST(RIGHT({Col("RecordNumber")}, CHARINDEX('-', REVERSE({Col("RecordNumber")})) - 1) AS INT) END), 0) FROM {Tbl("RmsIncidentReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordNumber")} LIKE {P}Prefix",
				new { DepartmentId = departmentId, Prefix = numberPrefix + "-%" });
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string reportId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			await LockRecordsDepartmentAsync(departmentId, cancellationToken);
			await PreserveCurrentHoldMembershipAsync(departmentId, reportId, false, cancellationToken);
			var affected = await ExecuteAsync(
				$"UPDATE {Tbl("RmsIncidentReports")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentReportId")} = {P}ReportId AND {Col("RowVersion")} = {P}Expected AND {Col("PurgedOn")} IS NULL AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, ReportId = reportId, Expected = expectedRowVersion }, cancellationToken);
			return affected == 1;
		}
	}

	/// <summary>Draft/revision child rows: one working draft (RevisionId null) and immutable revision copies.</summary>
	public abstract class RmsIncidentChildRepository<T> : RmsRepositoryBase<T>, IRmsIncidentChildRepository<T> where T : class, IEntity
	{
		private readonly string _table;
		private readonly string _orderBy;

		protected RmsIncidentChildRepository(string table, string orderBy, IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory)
		{
			_table = table;
			_orderBy = orderBy;
		}

		public Task<IEnumerable<T>> GetForRecordAsync(int departmentId, string recordId, string revisionId)
		{
			var revisionClause = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			return QueryAsync<T>(
				$"SELECT * FROM {Tbl(_table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revisionClause} ORDER BY {Col(_orderBy)}",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId });
		}

		public Task<int> DeleteDraftForRecordAsync(int departmentId, string recordId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl(_table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("RevisionId")} IS NULL",
				new { DepartmentId = departmentId, RecordId = recordId }, cancellationToken);
		}
	}

	public class RmsSourceFactsRepository : RmsIncidentChildRepository<RmsSourceFact>, IRmsSourceFactsRepository
	{
		public RmsSourceFactsRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsSourceFacts", "FactKey", c, s, u, q) { }
	}

	public class RmsUnitResponsesRepository : RmsIncidentChildRepository<RmsUnitResponse>, IRmsUnitResponsesRepository
	{
		public RmsUnitResponsesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsUnitResponses", "Ordinal", c, s, u, q) { }
	}

	public class RmsIncidentTypesRepository : RmsIncidentChildRepository<RmsIncidentType>, IRmsIncidentTypesRepository
	{
		public RmsIncidentTypesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsIncidentTypes", "Ordinal", c, s, u, q) { }
	}

	public class RmsActionTacticsRepository : RmsIncidentChildRepository<RmsActionTactic>, IRmsActionTacticsRepository
	{
		public RmsActionTacticsRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsActionTactics", "Ordinal", c, s, u, q) { }
	}

	public class RmsAidsRepository : RmsIncidentChildRepository<RmsAid>, IRmsAidsRepository
	{
		public RmsAidsRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsAids", "Ordinal", c, s, u, q) { }
	}

	public class RmsLocationsRepository : RmsIncidentChildRepository<RmsLocation>, IRmsLocationsRepository
	{
		public RmsLocationsRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsLocations", "CreatedOn", c, s, u, q) { }
	}

	public class RmsNarrativesRepository : RmsIncidentChildRepository<RmsNarrative>, IRmsNarrativesRepository
	{
		public RmsNarrativesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsNarratives", "CreatedOn", c, s, u, q) { }
	}

	public class RmsIncidentModulesRepository : RmsIncidentChildRepository<RmsIncidentModule>, IRmsIncidentModulesRepository
	{
		public RmsIncidentModulesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsIncidentModules", "Ordinal", c, s, u, q) { }

		public Task<IEnumerable<RmsIncidentModule>> GetForRecordByKindAsync(int departmentId, string recordId, string revisionId, RmsIncidentModuleKind kind)
		{
			var revisionClause = revisionId == null ? $"{Col("RevisionId")} IS NULL" : $"{Col("RevisionId")} = {P}RevisionId";
			return QueryAsync<RmsIncidentModule>(
				$"SELECT * FROM {Tbl("RmsIncidentModules")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {revisionClause} AND {Col("ModuleKind")} = {P}ModuleKind ORDER BY {Col("Ordinal")}",
				new { DepartmentId = departmentId, RecordId = recordId, RevisionId = revisionId, ModuleKind = (int)kind });
		}
	}

	public class RmsIncidentResourcesRepository : RmsIncidentChildRepository<RmsIncidentResource>, IRmsIncidentResourcesRepository
	{
		public RmsIncidentResourcesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsIncidentResources", "Ordinal", c, s, u, q) { }
	}

	public class RmsCasualtyRescuesRepository : RmsIncidentChildRepository<RmsCasualtyRescue>, IRmsCasualtyRescuesRepository
	{
		public RmsCasualtyRescuesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsCasualtyRescues", "Ordinal", c, s, u, q) { }
	}

	public class RmsExposuresRepository : RmsIncidentChildRepository<RmsExposure>, IRmsExposuresRepository
	{
		public RmsExposuresRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsExposures", "Ordinal", c, s, u, q) { }
	}

	public class RmsIncidentPropertiesRepository : RmsIncidentChildRepository<RmsIncidentProperty>, IRmsIncidentPropertiesRepository
	{
		public RmsIncidentPropertiesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsIncidentProperties", "Ordinal", c, s, u, q) { }
	}

	public class RmsIncidentVehiclesRepository : RmsIncidentChildRepository<RmsIncidentVehicle>, IRmsIncidentVehiclesRepository
	{
		public RmsIncidentVehiclesRepository(IConnectionProvider c, SqlConfiguration s, IUnitOfWork u, IQueryFactory q) : base("RmsIncidentVehicles", "Ordinal", c, s, u, q) { }
	}

	/// <summary>The separate incident-analysis filing (registry M0167): one per incident report, own lifecycle and submissions.</summary>
	public class RmsIncidentAnalysesRepository : RmsRepositoryBase<RmsIncidentAnalysis>, IRmsIncidentAnalysesRepository
	{
		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string analysisId, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			var analysis = await GetByIdForDepartmentAsync(departmentId, analysisId);
			if (analysis == null || analysis.DeletedOn.HasValue) return false;
			await LockLiveContentParentAsync(departmentId, analysis.IncidentReportId, cancellationToken);
			return await ExecuteAsync($"UPDATE {Tbl("RmsIncidentAnalyses")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentAnalysisId")} = {P}Id AND {Col("RowVersion")} = {P}Expected AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = analysisId, Expected = expectedRowVersion }, cancellationToken) == 1;
		}
		public RmsIncidentAnalysesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsIncidentAnalysis> GetByIdForDepartmentAsync(int departmentId, string analysisId)
		{
			return QueryFirstOrDefaultAsync<RmsIncidentAnalysis>(
				$"SELECT * FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentAnalysisId")} = {P}AnalysisId",
				new { DepartmentId = departmentId, AnalysisId = analysisId });
		}

		public Task<RmsIncidentAnalysis> GetForReportAsync(int departmentId, string incidentReportId)
		{
			return QueryFirstOrDefaultAsync<RmsIncidentAnalysis>(
				$"SELECT * FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IncidentReportId")} = {P}ReportId AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, ReportId = incidentReportId });
		}

		public Task<IEnumerable<RmsIncidentAnalysis>> GetAwaitingIncidentAsync(int departmentId, int take)
		{
			// Finalized but never filed: the incident had no NERIS id when the analysis was finalized.
			return QueryAsync<RmsIncidentAnalysis>(
				$@"SELECT * FROM {Tbl("RmsIncidentAnalyses")}
					WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}State
					AND {Col("NerisAnalysisId")} IS NULL AND {Col("DeletedOn")} IS NULL
					ORDER BY {Col("FinalizedOn")} {Paging()}",
				new { DepartmentId = departmentId, State = (int)RmsIncidentAnalysisState.Finalized, Skip = 0, Take = Math.Clamp(take, 1, 1000) });
		}

		public Task<int> CountByStateAsync(int departmentId, RmsIncidentAnalysisState state)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}State AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, State = (int)state });
		}

		public Task<int> CountVisibleByStateAsync(int departmentId, RmsIncidentAnalysisState state, List<int> visibleGroupIds, string userId) =>
			ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsIncidentAnalyses")} a JOIN {Tbl("RmsIncidentReports")} r ON r.{Col("DepartmentId")} = a.{Col("DepartmentId")} AND r.{Col("RmsIncidentReportId")} = a.{Col("IncidentReportId")} WHERE a.{Col("DepartmentId")} = {P}DepartmentId AND a.{Col("State")} = {P}State AND a.{Col("DeletedOn")} IS NULL AND r.{Col("DeletedOn")} IS NULL AND r.{Col("PurgedOn")} IS NULL AND {VisibleRecord("r", "RmsIncidentReportId", visibleGroupIds, false)}",
				new { DepartmentId = departmentId, State = (int)state, VisibleGroupIds = InListValue(visibleGroupIds), Viewer = userId });
	}

	public class RmsValidationIssuesRepository : RmsRepositoryBase<RmsValidationIssue>, IRmsValidationIssuesRepository
	{
		public RmsValidationIssuesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsValidationIssue>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsValidationIssue>(
				$"SELECT * FROM {Tbl("RmsValidationIssues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("ResolvedOn")} IS NULL ORDER BY {Col("Severity")}, {Col("FieldPath")}",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public async Task ReplaceForRecordAsync(int departmentId, string recordId, RmsValidationSource source, IEnumerable<RmsValidationIssue> issues, CancellationToken cancellationToken = default)
		{
			await ExecuteAsync(
				$"DELETE FROM {Tbl("RmsValidationIssues")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {Col("Source")} = {P}Source",
				new { DepartmentId = departmentId, RecordId = recordId, Source = (int)source }, cancellationToken);

			foreach (var issue in issues ?? Enumerable.Empty<RmsValidationIssue>())
				await InsertAsync(issue, cancellationToken, true);
		}
	}

	public class RmsSubmissionsRepository : RmsRepositoryBase<RmsSubmission>, IRmsSubmissionsRepository
	{
		public async Task<bool> TryConfirmNotCreatedAsync(int departmentId, string submissionId, long expectedVersion, string destinationIdentity, DateTime now, CancellationToken cancellationToken = default)
		{
			await LockRecordsDepartmentAsync(departmentId, cancellationToken);
			return await ExecuteAsync($"UPDATE {Tbl("RmsSubmissions")} SET {Col("DestinationIdentity")} = {P}Destination, {Col("RequiresReconciliation")} = {P}False, {Col("CreatePendingReceipt")} = {P}False, {Col("State")} = {(int)RmsSubmissionState.Rejected}, {Col("NextAttemptOn")} = NULL, {Col("CompletedOn")} = {P}Now, {Col("LeaseOwner")} = NULL, {Col("LeaseExpiresOn")} = NULL, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSubmissionId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("ExternalId")} IS NULL AND ({Col("DestinationIdentity")} IS NULL OR {Col("DestinationIdentity")} = {P}Destination) AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} <= {P}Now) AND ({Col("RequiresReconciliation")} = {P}True OR {Col("CreatePendingReceipt")} = {P}True OR {Col("State")} IN ({(int)RmsSubmissionState.Failed}, {(int)RmsSubmissionState.Rejected}))",
				new { DepartmentId = departmentId, Id = submissionId, Version = expectedVersion, Destination = destinationIdentity, False = false, True = true, Now = now }, cancellationToken) == 1;
		}

		public async Task<bool> TryBindUnsentAsync(int departmentId, string submissionId, long expectedVersion, string destinationIdentity, DateTime now, CancellationToken cancellationToken = default)
		{
			await LockRecordsDepartmentAsync(departmentId, cancellationToken);
			return await ExecuteAsync($"UPDATE {Tbl("RmsSubmissions")} SET {Col("DestinationIdentity")} = {P}Destination, {Col("State")} = {(int)RmsSubmissionState.Queued}, {Col("NextAttemptOn")} = {P}Now, {Col("CompletedOn")} = NULL, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSubmissionId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("DestinationIdentity")} IS NULL AND {Col("SentOn")} IS NULL AND {Col("Attempts")} = 0 AND {Col("ExternalId")} IS NULL AND {Col("RequiresReconciliation")} = {P}False AND {Col("CreatePendingReceipt")} = {P}False AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} <= {P}Now)",
				new { DepartmentId = departmentId, Id = submissionId, Version = expectedVersion, Destination = destinationIdentity, False = false, Now = now }, cancellationToken) == 1;
		}
		public async Task<bool> TryReconcileReceiptAsync(int departmentId, string submissionId, long expectedVersion, string externalId, string destinationIdentity, DateTime now, CancellationToken cancellationToken = default)
		{
			await LockRecordsDepartmentAsync(departmentId, cancellationToken);
			return await ExecuteAsync($"UPDATE {Tbl("RmsSubmissions")} SET {Col("ExternalId")} = {P}ExternalId, {Col("DestinationIdentity")} = {P}Destination, {Col("RequiresReconciliation")} = {P}False, {Col("CreatePendingReceipt")} = {P}False, {Col("State")} = {(int)RmsSubmissionState.AwaitingDestination}, {Col("NextAttemptOn")} = {P}Now, {Col("CompletedOn")} = NULL, {Col("LeaseOwner")} = NULL, {Col("LeaseExpiresOn")} = NULL, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSubmissionId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} <= {P}Now) AND ({Col("RequiresReconciliation")} = {P}True OR {Col("CreatePendingReceipt")} = {P}True)",
				new { DepartmentId = departmentId, Id = submissionId, Version = expectedVersion, ExternalId = externalId, Destination = destinationIdentity, False = false, True = true, Now = now }, cancellationToken) == 1;
		}
		public async Task<bool> TryFenceLeaseAsync(int departmentId, string submissionId, long expectedVersion, string leaseOwner, DateTime now, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(leaseOwner)) return false;
			await LockRecordsDepartmentAsync(departmentId, cancellationToken);
			return await ExecuteAsync($"UPDATE {Tbl("RmsSubmissions")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSubmissionId")} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("LeaseOwner")} = {P}Owner AND {Col("LeaseExpiresOn")} > {P}Now AND {InList("State", "States")}",
				new { DepartmentId = departmentId, Id = submissionId, Version = expectedVersion, Owner = leaseOwner, Now = now,
					States = InListValue(new[] { (int)RmsSubmissionState.Queued, (int)RmsSubmissionState.AwaitingDestination, (int)RmsSubmissionState.Failed }) }, cancellationToken) == 1;
		}

		public RmsSubmissionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsSubmission> GetByIdForDepartmentAsync(int departmentId, string submissionId)
		{
			return QueryFirstOrDefaultAsync<RmsSubmission>(
				$"SELECT * FROM {Tbl("RmsSubmissions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsSubmissionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = submissionId });
		}

		public Task<IEnumerable<RmsSubmission>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsSubmission>(
				$"SELECT * FROM {Tbl("RmsSubmissions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("QueuedOn")} DESC",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<RmsSubmission> GetByIdempotencyKeyAsync(string idempotencyKey)
		{
			return QueryFirstOrDefaultAsync<RmsSubmission>(
				$"SELECT * FROM {Tbl("RmsSubmissions")} WHERE {Col("IdempotencyKey")} = {P}Key",
				new { Key = idempotencyKey });
		}

		public async Task<IEnumerable<RmsSubmission>> ClaimDueBatchAsync(string leaseOwner, TimeSpan leaseDuration, int batchSize, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var claimable = new[] { (int)RmsSubmissionState.Queued, (int)RmsSubmissionState.AwaitingDestination };
			var recoverable = $"s.{Col("State")} = {(int)RmsSubmissionState.Failed} AND s.{Col("RequiresReconciliation")} = {P}True AND EXISTS (SELECT 1 FROM {Tbl("RmsSubmissionExchanges")} e WHERE e.{Col("DepartmentId")} = s.{Col("DepartmentId")} AND e.{Col("SubmissionId")} = s.{Col("RmsSubmissionId")} AND e.{Col("Stage")} = 'Response' AND NOT EXISTS (SELECT 1 FROM {Tbl("RmsSubmissionExchanges")} a WHERE a.{Col("DepartmentId")} = e.{Col("DepartmentId")} AND a.{Col("SubmissionId")} = e.{Col("SubmissionId")} AND a.{Col("ExchangeId")} = e.{Col("ExchangeId")} AND a.{Col("Stage")} = 'Applied'))";
			var candidates = (await QueryAsync<RmsSubmission>(
				$"SELECT s.* FROM {Tbl("RmsSubmissions")} s WHERE ({InList("State", "States")} OR ({recoverable})) AND ({Col("NextAttemptOn")} IS NULL OR {Col("NextAttemptOn")} <= {P}Now) " +
				$"AND ({Col("LeaseExpiresOn")} IS NULL OR {Col("LeaseExpiresOn")} < {P}Now) ORDER BY {Col("QueuedOn")} {Paging()}",
				new { States = InListValue(claimable), True = true, Now = utcNow, Skip = 0, Take = Math.Clamp(batchSize, 1, 500) }, cancellationToken)).ToList();

			var claimed = new List<RmsSubmission>();
			var leaseUntil = utcNow.Add(leaseDuration);
			foreach (var candidate in candidates)
			{
				// Single-flight: the row version guards two workers racing for the same submission.
				var affected = await ExecuteAsync(
					$"UPDATE {Tbl("RmsSubmissions")} SET {Col("LeaseOwner")} = {P}Owner, {Col("LeaseExpiresOn")} = {P}Until, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
					$"WHERE {Col("RmsSubmissionId")} = {P}Id AND {Col("RowVersion")} = {P}Version",
					new { Owner = leaseOwner, Until = leaseUntil, Id = candidate.RmsSubmissionId, Version = candidate.RowVersion }, cancellationToken);
				if (affected != 1)
					continue;

				candidate.LeaseOwner = leaseOwner;
				candidate.LeaseExpiresOn = leaseUntil;
				candidate.RowVersion += 1;
				claimed.Add(candidate);
			}

			return claimed;
		}

		public Task<int> CountByStateAsync(int departmentId, int state)
		{
			return ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsSubmissions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}State",
				new { DepartmentId = departmentId, State = state });
		}

		public Task<int> SupersedeOpenForRecordAsync(int departmentId, string recordId, string exceptSubmissionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var open = new[] { (int)RmsSubmissionState.Queued, (int)RmsSubmissionState.InFlight, (int)RmsSubmissionState.AwaitingDestination };
			return ExecuteAsync(
				$"UPDATE {Tbl("RmsSubmissions")} SET {Col("State")} = {P}Superseded, {Col("CompletedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId AND {InList("State", "States")} AND {Col("RmsSubmissionId")} <> {P}Except",
				new { Superseded = (int)RmsSubmissionState.Superseded, Now = utcNow, DepartmentId = departmentId, RecordId = recordId, States = InListValue(open), Except = exceptSubmissionId ?? string.Empty }, cancellationToken);
		}
	}

	public class RmsSubmissionExchangesRepository : RmsRepositoryBase<RmsSubmissionExchange>, IRmsSubmissionExchangesRepository
	{
		public RmsSubmissionExchangesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }
		public Task<IEnumerable<RmsSubmissionExchange>> GetForSubmissionAsync(int departmentId, string submissionId) =>
			QueryAsync<RmsSubmissionExchange>($"SELECT * FROM {Tbl("RmsSubmissionExchanges")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubmissionId")} = {P}SubmissionId ORDER BY {Col("OccurredOn")}, {Col("RmsSubmissionExchangeId")}",
				new { DepartmentId = departmentId, SubmissionId = submissionId });
	}

	public class RmsSignaturesRepository : RmsRepositoryBase<RmsSignature>, IRmsSignaturesRepository
	{
		public RmsSignaturesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsSignature>> GetForRecordAsync(int departmentId, string recordId)
		{
			return QueryAsync<RmsSignature>(
				$"SELECT * FROM {Tbl("RmsSignatures")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("SignedOn")}",
				new { DepartmentId = departmentId, RecordId = recordId });
		}

		public Task<RmsSignature> GetForRevisionAsync(int departmentId, string revisionId, RmsSignatureIntent intent)
		{
			return QueryFirstOrDefaultAsync<RmsSignature>(
				$"SELECT * FROM {Tbl("RmsSignatures")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RevisionId")} = {P}RevisionId AND {Col("Intent")} = {P}Intent",
				new { DepartmentId = departmentId, RevisionId = revisionId, Intent = (int)intent });
		}
	}

	public class RmsNerisProfilesRepository : RmsRepositoryBase<RmsNerisProfile>, IRmsNerisProfilesRepository
	{
		public RmsNerisProfilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsNerisProfile> GetByDepartmentIdAsync(int departmentId)
		{
			return QueryFirstOrDefaultAsync<RmsNerisProfile>(
				$"SELECT * FROM {Tbl("RmsNerisProfiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });
		}
	}

	public class RmsNerisValueSetsRepository : RmsRepositoryBase<RmsNerisValueSetEntry>, IRmsNerisValueSetsRepository
	{
		public RmsNerisValueSetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsNerisValueSetEntry>> GetSetAsync(string contractVersion, string setKey)
		{
			return QueryAsync<RmsNerisValueSetEntry>(
				$"SELECT * FROM {Tbl("RmsNerisValueSets")} WHERE {Col("ContractVersion")} = {P}Version AND {Col("SetKey")} = {P}SetKey ORDER BY {Col("SortOrder")}",
				new { Version = contractVersion, SetKey = setKey });
		}

		public Task<int> CountForVersionAsync(string contractVersion)
		{
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsNerisValueSets")} WHERE {Col("ContractVersion")} = {P}Version", new { Version = contractVersion });
		}

		public async Task<bool> ExistsAsync(string contractVersion, string setKey, string code)
		{
			return await ScalarAsync<int>(
				$"SELECT COUNT(1) FROM {Tbl("RmsNerisValueSets")} WHERE {Col("ContractVersion")} = {P}Version AND {Col("SetKey")} = {P}SetKey AND {Col("Code")} = {P}Code",
				new { Version = contractVersion, SetKey = setKey, Code = code }) > 0;
		}
	}

	public class RmsNerisCrosswalksRepository : RmsRepositoryBase<RmsNerisCrosswalk>, IRmsNerisCrosswalksRepository
	{
		public RmsNerisCrosswalksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsNerisCrosswalk>> GetForDepartmentAsync(int departmentId, string contractVersion)
		{
			return QueryAsync<RmsNerisCrosswalk>(
				$"SELECT * FROM {Tbl("RmsNerisCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContractVersion")} = {P}Version AND {Col("DeletedOn")} IS NULL ORDER BY {Col("SetKey")}, {Col("LocalSource")}, {Col("LocalCode")}",
				new { DepartmentId = departmentId, Version = contractVersion });
		}

		public Task<RmsNerisCrosswalk> GetAsync(int departmentId, string contractVersion, string setKey, string localSource, string localCode)
		{
			return QueryFirstOrDefaultAsync<RmsNerisCrosswalk>(
				$"SELECT * FROM {Tbl("RmsNerisCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContractVersion")} = {P}Version AND {Col("SetKey")} = {P}SetKey AND {Col("LocalSource")} = {P}LocalSource AND {Col("LocalCode")} = {P}LocalCode AND {Col("DeletedOn")} IS NULL",
				new { DepartmentId = departmentId, Version = contractVersion, SetKey = setKey, LocalSource = localSource, LocalCode = localCode });
		}
	}
}

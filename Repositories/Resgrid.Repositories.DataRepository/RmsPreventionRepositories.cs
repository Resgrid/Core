using System;
using System.Collections.Generic;
using System.Linq;
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
	/// RMS-5 prevention, investigation and RMS-4 quality-review repositories (registry M0185/M0186). Dapper over
	/// RmsRepositoryBase; every query carries DepartmentId, list parameters go through InList so PostgreSQL binds
	/// arrays, and soft-deleted rows are excluded everywhere a DeletedOn column exists.
	/// </summary>
	internal static class RmsPreventionSql
	{
		public static DynamicParameters Paged(int departmentId, int skip, int take, int cap = 500)
		{
			var p = new DynamicParameters();
			p.Add("DepartmentId", departmentId);
			p.Add("Skip", Math.Max(0, skip));
			p.Add("Take", take <= 0 ? 50 : Math.Min(take, cap));
			return p;
		}
	}

	public class RmsOccupanciesRepository : RmsRepositoryBase<RmsOccupancy>, IRmsOccupanciesRepository
	{
		public RmsOccupanciesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsOccupancy> GetByIdForDepartmentAsync(int departmentId, string occupancyId)
			=> QueryFirstOrDefaultAsync<RmsOccupancy>($"SELECT * FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<IEnumerable<RmsOccupancy>> GetByIdsAsync(int departmentId, IEnumerable<string> occupancyIds)
		{
			var ids = InListValue(occupancyIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsOccupancy>>(new List<RmsOccupancy>());
			return QueryAsync<RmsOccupancy>($"SELECT * FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RmsOccupancyId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}

		private string Where(RmsOccupancyQuery query, DynamicParameters p)
		{
			var clauses = new List<string> { $"{Col("DepartmentId")} = {P}DepartmentId", $"{Col("DeletedOn")} IS NULL" };
			if (query.Status.HasValue) { clauses.Add($"{Col("Status")} = {P}Status"); p.Add("Status", query.Status.Value); }
			else clauses.Add($"{Col("Status")} <> {(int)RmsOccupancyStatus.Merged}");
			if (query.OccupancyType.HasValue) { clauses.Add($"{Col("OccupancyType")} = {P}OccupancyType"); p.Add("OccupancyType", query.OccupancyType.Value); }
			if (query.HazmatOnSite.HasValue) { clauses.Add($"{Col("HazmatOnSite")} = {P}Hazmat"); p.Add("Hazmat", query.HazmatOnSite.Value); }
			if (query.ReviewOverdue == true) { clauses.Add($"{Col("NextReviewDue")} < {P}Now"); p.Add("Now", DateTime.UtcNow); }
			if (!string.IsNullOrWhiteSpace(query.Search))
			{
				clauses.Add($"({Col("Name")} LIKE {P}Search OR {Col("AddressText")} LIKE {P}Search OR {Col("OccupancyNumber")} LIKE {P}Search)");
				p.Add("Search", "%" + query.Search.Trim() + "%");
			}
			return string.Join(" AND ", clauses);
		}

		public Task<IEnumerable<RmsOccupancy>> QueryAsync(int departmentId, RmsOccupancyQuery query)
		{
			query ??= new RmsOccupancyQuery();
			var p = RmsPreventionSql.Paged(departmentId, query.Skip, query.Take);
			var where = Where(query, p);
			return QueryAsync<RmsOccupancy>($"SELECT * FROM {Tbl("RmsOccupancies")} WHERE {where} ORDER BY {Col("Name")}, {Col("RmsOccupancyId")} {Paging()}", p);
		}

		public Task<int> CountAsync(int departmentId, RmsOccupancyQuery query)
		{
			query ??= new RmsOccupancyQuery();
			var p = new DynamicParameters(); p.Add("DepartmentId", departmentId);
			var where = Where(query, p);
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsOccupancies")} WHERE {where}", p);
		}

		public Task<IEnumerable<RmsOccupancy>> GetByNormalizedAddressAsync(int departmentId, string normalizedAddress)
			=> QueryAsync<RmsOccupancy>($"SELECT * FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("NormalizedAddress")} = {P}Address AND {Col("DeletedOn")} IS NULL AND {Col("Status")} <> {(int)RmsOccupancyStatus.Merged}", new { DepartmentId = departmentId, Address = normalizedAddress });

		public Task<IEnumerable<RmsOccupancy>> GetAllLiveAsync(int departmentId)
			=> QueryAsync<RmsOccupancy>($"SELECT * FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("Status")} <> {(int)RmsOccupancyStatus.Merged} ORDER BY {Col("Name")}", new { DepartmentId = departmentId });

		public Task<int> CountLiveAsync(int departmentId)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("Status")} <> {(int)RmsOccupancyStatus.Merged}", new { DepartmentId = departmentId });

		public Task<int> CountReviewOverdueAsync(int departmentId, DateTime utcNow)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsOccupancies")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("Status")} = {(int)RmsOccupancyStatus.Active} AND {Col("NextReviewDue")} < {P}Now", new { DepartmentId = departmentId, Now = utcNow });

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string occupancyId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsOccupancies")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("RowVersion")} = {P}Expected",
				new { DepartmentId = departmentId, Id = occupancyId, Expected = expectedVersion, Now = DateTime.UtcNow }, cancellationToken) == 1;
	}

	public class RmsOccupancyContactLinksRepository : RmsRepositoryBase<RmsOccupancyContactLink>, IRmsOccupancyContactLinksRepository
	{
		public RmsOccupancyContactLinksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsOccupancyContactLink>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsOccupancyContactLink>($"SELECT * FROM {Tbl("RmsOccupancyContactLinks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Role")}, {Col("CreatedOn")}", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<IEnumerable<RmsOccupancyContactLink>> GetForContactAsync(int departmentId, string contactId)
			=> QueryAsync<RmsOccupancyContactLink>($"SELECT * FROM {Tbl("RmsOccupancyContactLinks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, ContactId = contactId });

		public Task<IEnumerable<RmsOccupancyContactLink>> GetForContactsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var ids = InListValue(contactIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsOccupancyContactLink>>(new List<RmsOccupancyContactLink>());
			return QueryAsync<RmsOccupancyContactLink>($"SELECT * FROM {Tbl("RmsOccupancyContactLinks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "Ids")} AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<RmsOccupancyContactLink> GetByIdForDepartmentAsync(int departmentId, string linkId)
			=> QueryFirstOrDefaultAsync<RmsOccupancyContactLink>($"SELECT * FROM {Tbl("RmsOccupancyContactLinks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyContactLinkId")} = {P}Id", new { DepartmentId = departmentId, Id = linkId });
	}

	public class RmsOccupancyHazardsRepository : RmsRepositoryBase<RmsOccupancyHazard>, IRmsOccupancyHazardsRepository
	{
		public RmsOccupancyHazardsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsOccupancyHazard>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsOccupancyHazard>($"SELECT * FROM {Tbl("RmsOccupancyHazards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Severity")} DESC, {Col("Title")}", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<IEnumerable<RmsOccupancyHazard>> GetForOccupanciesAsync(int departmentId, IEnumerable<string> occupancyIds)
		{
			var ids = InListValue(occupancyIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsOccupancyHazard>>(new List<RmsOccupancyHazard>());
			return QueryAsync<RmsOccupancyHazard>($"SELECT * FROM {Tbl("RmsOccupancyHazards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RmsOccupancyId", "Ids")} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Severity")} DESC", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<RmsOccupancyHazard> GetByIdForDepartmentAsync(int departmentId, string hazardId)
			=> QueryFirstOrDefaultAsync<RmsOccupancyHazard>($"SELECT * FROM {Tbl("RmsOccupancyHazards")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyHazardId")} = {P}Id", new { DepartmentId = departmentId, Id = hazardId });
	}

	public class RmsOccupancyCrosswalksRepository : RmsRepositoryBase<RmsOccupancyCrosswalk>, IRmsOccupancyCrosswalksRepository
	{
		public RmsOccupancyCrosswalksRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsOccupancyCrosswalk> GetByIdForDepartmentAsync(int departmentId, string crosswalkId)
			=> QueryFirstOrDefaultAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyCrosswalkId")} = {P}Id", new { DepartmentId = departmentId, Id = crosswalkId });

		public Task<RmsOccupancyCrosswalk> GetBySourceAsync(int departmentId, RmsOccupancyCrosswalkSourceKind sourceKind, string sourceId)
			=> QueryFirstOrDefaultAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}Kind AND {Col("SourceId")} = {P}SourceId", new { DepartmentId = departmentId, Kind = (int)sourceKind, SourceId = sourceId });

		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetByStateAsync(int departmentId, RmsOccupancyCrosswalkState state, int skip, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, skip, take, 1000); p.Add("State", (int)state);
			return QueryAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}State ORDER BY {Col("GroupKey")}, {Col("SourceDisplayName")}, {Col("RmsOccupancyCrosswalkId")} {Paging()}", p);
		}

		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetForContactAsync(int departmentId, string contactId)
			=> QueryAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ContactId")} = {P}ContactId", new { DepartmentId = departmentId, ContactId = contactId });

		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetForContactsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var ids = InListValue(contactIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsOccupancyCrosswalk>>(new List<RmsOccupancyCrosswalk>());
			return QueryAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ContactId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<IEnumerable<RmsOccupancyCrosswalk>> GetAllForDepartmentAsync(int departmentId)
			=> QueryAsync<RmsOccupancyCrosswalk>($"SELECT * FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });

		public Task<int> CountByStateAsync(int departmentId, RmsOccupancyCrosswalkState state)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsOccupancyCrosswalks")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {P}State", new { DepartmentId = departmentId, State = (int)state });
	}

	public class RmsOccupancyFieldProvenancesRepository : RmsRepositoryBase<RmsOccupancyFieldProvenance>, IRmsOccupancyFieldProvenancesRepository
	{
		public RmsOccupancyFieldProvenancesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsOccupancyFieldProvenance>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsOccupancyFieldProvenance>($"SELECT * FROM {Tbl("RmsOccupancyFieldProvenances")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id ORDER BY {Col("FieldKey")}", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<int> DeleteForOccupancyAsync(int departmentId, string occupancyId, CancellationToken cancellationToken = default)
			=> ExecuteAsync($"DELETE FROM {Tbl("RmsOccupancyFieldProvenances")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id", new { DepartmentId = departmentId, Id = occupancyId }, cancellationToken);
	}

	public class RmsOccupancyOwnershipsRepository : RmsRepositoryBase<RmsOccupancyOwnership>, IRmsOccupancyOwnershipsRepository
	{
		public RmsOccupancyOwnershipsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsOccupancyOwnership> GetForDepartmentAsync(int departmentId)
			=> QueryFirstOrDefaultAsync<RmsOccupancyOwnership>($"SELECT * FROM {Tbl("RmsOccupancyOwnerships")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });
	}

	public class RmsCodeSetsRepository : RmsRepositoryBase<RmsCodeSet>, IRmsCodeSetsRepository
	{
		public RmsCodeSetsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsCodeSet> GetByIdForDepartmentAsync(int departmentId, string codeSetId)
			=> QueryFirstOrDefaultAsync<RmsCodeSet>($"SELECT * FROM {Tbl("RmsCodeSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCodeSetId")} = {P}Id", new { DepartmentId = departmentId, Id = codeSetId });

		public Task<IEnumerable<RmsCodeSet>> GetForDepartmentAsync(int departmentId, bool includeInactive)
			=> QueryAsync<RmsCodeSet>($"SELECT * FROM {Tbl("RmsCodeSets")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{(includeInactive ? "" : $" AND {Col("IsActive")} = {P}Active")} ORDER BY {Col("Name")}", new { DepartmentId = departmentId, Active = true });
	}

	public class RmsCodeSectionsRepository : RmsRepositoryBase<RmsCodeSection>, IRmsCodeSectionsRepository
	{
		public RmsCodeSectionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsCodeSection> GetByIdForDepartmentAsync(int departmentId, string sectionId)
			=> QueryFirstOrDefaultAsync<RmsCodeSection>($"SELECT * FROM {Tbl("RmsCodeSections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCodeSectionId")} = {P}Id", new { DepartmentId = departmentId, Id = sectionId });

		public Task<IEnumerable<RmsCodeSection>> GetForCodeSetAsync(int departmentId, string codeSetId)
			=> QueryAsync<RmsCodeSection>($"SELECT * FROM {Tbl("RmsCodeSections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCodeSetId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("SectionNumber")}", new { DepartmentId = departmentId, Id = codeSetId });

		public Task<IEnumerable<RmsCodeSection>> GetByIdsAsync(int departmentId, IEnumerable<string> sectionIds)
		{
			var ids = InListValue(sectionIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsCodeSection>>(new List<RmsCodeSection>());
			return QueryAsync<RmsCodeSection>($"SELECT * FROM {Tbl("RmsCodeSections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RmsCodeSectionId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}
	}

	public class RmsInspectionProgramsRepository : RmsRepositoryBase<RmsInspectionProgram>, IRmsInspectionProgramsRepository
	{
		public RmsInspectionProgramsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsInspectionProgram> GetByIdForDepartmentAsync(int departmentId, string programId)
			=> QueryFirstOrDefaultAsync<RmsInspectionProgram>($"SELECT * FROM {Tbl("RmsInspectionPrograms")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInspectionProgramId")} = {P}Id", new { DepartmentId = departmentId, Id = programId });

		public Task<IEnumerable<RmsInspectionProgram>> GetForDepartmentAsync(int departmentId, bool includeInactive)
			=> QueryAsync<RmsInspectionProgram>($"SELECT * FROM {Tbl("RmsInspectionPrograms")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{(includeInactive ? "" : $" AND {Col("IsActive")} = {P}Active")} ORDER BY {Col("Name")}", new { DepartmentId = departmentId, Active = true });
	}

	public class RmsInspectionsRepository : RmsRepositoryBase<RmsInspection>, IRmsInspectionsRepository
	{
		public RmsInspectionsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsInspection> GetByIdForDepartmentAsync(int departmentId, string inspectionId)
			=> QueryFirstOrDefaultAsync<RmsInspection>($"SELECT * FROM {Tbl("RmsInspections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInspectionId")} = {P}Id", new { DepartmentId = departmentId, Id = inspectionId });

		private string Where(RmsInspectionQuery query, DynamicParameters p)
		{
			var clauses = new List<string> { $"{Col("DepartmentId")} = {P}DepartmentId", $"{Col("DeletedOn")} IS NULL" };
			if (!string.IsNullOrWhiteSpace(query.OccupancyId)) { clauses.Add($"{Col("RmsOccupancyId")} = {P}OccupancyId"); p.Add("OccupancyId", query.OccupancyId); }
			if (!string.IsNullOrWhiteSpace(query.ProgramId)) { clauses.Add($"{Col("RmsInspectionProgramId")} = {P}ProgramId"); p.Add("ProgramId", query.ProgramId); }
			if (!string.IsNullOrWhiteSpace(query.InspectorUserId)) { clauses.Add($"{Col("InspectorUserId")} = {P}Inspector"); p.Add("Inspector", query.InspectorUserId); }
			if (query.States != null && query.States.Count > 0) { clauses.Add(InList("State", "States")); p.Add("States", InListValue(query.States)); }
			if (query.ScheduledBefore.HasValue) { clauses.Add($"{Col("ScheduledOn")} <= {P}Before"); p.Add("Before", query.ScheduledBefore.Value); }
			return string.Join(" AND ", clauses);
		}

		public Task<IEnumerable<RmsInspection>> QueryAsync(int departmentId, RmsInspectionQuery query)
		{
			query ??= new RmsInspectionQuery();
			var p = RmsPreventionSql.Paged(departmentId, query.Skip, query.Take);
			return QueryAsync<RmsInspection>($"SELECT * FROM {Tbl("RmsInspections")} WHERE {Where(query, p)} ORDER BY {Col("ScheduledOn")} DESC, {Col("CreatedOn")} DESC, {Col("RmsInspectionId")} {Paging()}", p);
		}

		public Task<int> CountAsync(int departmentId, RmsInspectionQuery query)
		{
			query ??= new RmsInspectionQuery();
			var p = new DynamicParameters(); p.Add("DepartmentId", departmentId);
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsInspections")} WHERE {Where(query, p)}", p);
		}

		public Task<IEnumerable<RmsInspection>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsInspection>($"SELECT * FROM {Tbl("RmsInspections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")} DESC", new { DepartmentId = departmentId, Id = occupancyId });

		public async Task<IDictionary<string, DateTime>> GetLastCompletedByOccupancyAsync(int departmentId, string programId)
		{
			var rows = await QueryAsync<(string OccupancyId, DateTime? Completed)>(
				$"SELECT {Col("RmsOccupancyId")} AS OccupancyId, MAX({Col("CompletedOn")}) AS Completed FROM {Tbl("RmsInspections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInspectionProgramId")} = {P}ProgramId AND {Col("CompletedOn")} IS NOT NULL AND {Col("DeletedOn")} IS NULL GROUP BY {Col("RmsOccupancyId")}",
				new { DepartmentId = departmentId, ProgramId = programId });
			return rows.Where(r => r.Completed.HasValue).ToDictionary(r => r.OccupancyId, r => r.Completed.Value, StringComparer.Ordinal);
		}

		public Task<IEnumerable<RmsInspection>> GetOpenForProgramAsync(int departmentId, string programId)
			=> QueryAsync<RmsInspection>($"SELECT * FROM {Tbl("RmsInspections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInspectionProgramId")} = {P}ProgramId AND {Col("State")} IN ({(int)RmsInspectionState.Scheduled}, {(int)RmsInspectionState.InProgress}, {(int)RmsInspectionState.ReinspectionRequired}) AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, ProgramId = programId });

		public Task<IEnumerable<RmsInspection>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take)
		{
			var activity = $"COALESCE({Col("CompletedOn")}, {Col("ScheduledOn")}, {Col("CreatedOn")})";
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 50000); p.Add("Start", startUtc); p.Add("End", endUtc);
			return QueryAsync<RmsInspection>($"SELECT * FROM {Tbl("RmsInspections")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {activity} >= {P}Start AND {activity} < {P}End AND {Col("DeletedOn")} IS NULL ORDER BY {activity}, {Col("RmsInspectionId")} {Paging()}", p);
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string inspectionId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsInspections")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInspectionId")} = {P}Id AND {Col("RowVersion")} = {P}Expected",
				new { DepartmentId = departmentId, Id = inspectionId, Expected = expectedVersion, Now = DateTime.UtcNow }, cancellationToken) == 1;
	}

	public class RmsViolationsRepository : RmsRepositoryBase<RmsViolation>, IRmsViolationsRepository
	{
		public RmsViolationsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private string OpenStates => $"{Col("State")} IN ({(int)RmsViolationState.Open}, {(int)RmsViolationState.Escalated})";

		public Task<RmsViolation> GetByIdForDepartmentAsync(int departmentId, string violationId)
			=> QueryFirstOrDefaultAsync<RmsViolation>($"SELECT * FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsViolationId")} = {P}Id", new { DepartmentId = departmentId, Id = violationId });

		public Task<IEnumerable<RmsViolation>> GetForInspectionAsync(int departmentId, string inspectionId)
			=> QueryAsync<RmsViolation>($"SELECT * FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInspectionId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("Severity")} DESC, {Col("CreatedOn")}", new { DepartmentId = departmentId, Id = inspectionId });

		public Task<IEnumerable<RmsViolation>> GetForOccupancyAsync(int departmentId, string occupancyId, bool openOnly)
			=> QueryAsync<RmsViolation>($"SELECT * FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("DeletedOn")} IS NULL{(openOnly ? " AND " + OpenStates : "")} ORDER BY {Col("CreatedOn")} DESC", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<IEnumerable<RmsViolation>> GetOpenAsync(int departmentId, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 1000);
			return QueryAsync<RmsViolation>($"SELECT * FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {OpenStates} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("DueOn")}, {Col("Severity")} DESC, {Col("RmsViolationId")} {Paging()}", p);
		}

		public Task<IEnumerable<RmsViolation>> GetOverdueNotEmittedAsync(int departmentId, DateTime utcNow, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 1000); p.Add("Now", utcNow);
			return QueryAsync<RmsViolation>($"SELECT * FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {OpenStates} AND {Col("DueOn")} < {P}Now AND {Col("OverdueEmittedOn")} IS NULL AND {Col("DeletedOn")} IS NULL ORDER BY {Col("DueOn")}, {Col("RmsViolationId")} {Paging()}", p);
		}

		public Task<int> CountOpenAsync(int departmentId)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {OpenStates} AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId });

		public Task<int> CountOverdueAsync(int departmentId, DateTime utcNow)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {OpenStates} AND {Col("DueOn")} < {P}Now AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Now = utcNow });

		public async Task<IDictionary<string, int>> CountOpenByOccupancyAsync(int departmentId, IEnumerable<string> occupancyIds)
		{
			var ids = InListValue(occupancyIds);
			if (ids.Length == 0) return new Dictionary<string, int>(StringComparer.Ordinal);
			var rows = await QueryAsync<(string OccupancyId, int Open)>(
				$"SELECT {Col("RmsOccupancyId")} AS OccupancyId, COUNT(1) AS Open FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RmsOccupancyId", "Ids")} AND {OpenStates} AND {Col("DeletedOn")} IS NULL GROUP BY {Col("RmsOccupancyId")}",
				new { DepartmentId = departmentId, Ids = ids });
			return rows.ToDictionary(r => r.OccupancyId, r => r.Open, StringComparer.Ordinal);
		}

		public Task<IEnumerable<RmsViolation>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 50000); p.Add("Start", startUtc); p.Add("End", endUtc);
			return QueryAsync<RmsViolation>($"SELECT * FROM {Tbl("RmsViolations")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("CreatedOn")} >= {P}Start AND {Col("CreatedOn")} < {P}End AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CreatedOn")}, {Col("RmsViolationId")} {Paging()}", p);
		}
	}

	public class RmsHydrantsRepository : RmsRepositoryBase<RmsHydrant>, IRmsHydrantsRepository
	{
		public RmsHydrantsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsHydrant> GetByIdForDepartmentAsync(int departmentId, string hydrantId)
			=> QueryFirstOrDefaultAsync<RmsHydrant>($"SELECT * FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsHydrantId")} = {P}Id", new { DepartmentId = departmentId, Id = hydrantId });

		public Task<RmsHydrant> GetByNumberAsync(int departmentId, string hydrantNumber)
			=> QueryFirstOrDefaultAsync<RmsHydrant>($"SELECT * FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("HydrantNumber")} = {P}Number AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Number = hydrantNumber });

		public Task<IEnumerable<RmsHydrant>> GetAllLiveAsync(int departmentId)
			=> QueryAsync<RmsHydrant>($"SELECT * FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("HydrantNumber")}", new { DepartmentId = departmentId });

		public Task<IEnumerable<RmsHydrant>> GetByIdsAsync(int departmentId, IEnumerable<string> hydrantIds)
		{
			var ids = InListValue(hydrantIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsHydrant>>(new List<RmsHydrant>());
			return QueryAsync<RmsHydrant>($"SELECT * FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RmsHydrantId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<IEnumerable<RmsHydrant>> GetInBoundsAsync(int departmentId, decimal minLat, decimal maxLat, decimal minLon, decimal maxLon, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 5000);
			p.Add("MinLat", minLat); p.Add("MaxLat", maxLat); p.Add("MinLon", minLon); p.Add("MaxLon", maxLon);
			return QueryAsync<RmsHydrant>($"SELECT * FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("Latitude")} BETWEEN {P}MinLat AND {P}MaxLat AND {Col("Longitude")} BETWEEN {P}MinLon AND {P}MaxLon ORDER BY {Col("HydrantNumber")}, {Col("RmsHydrantId")} {Paging()}", p);
		}

		public Task<int> CountLiveAsync(int departmentId)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId });

		public Task<int> CountOutOfServiceAsync(int departmentId)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("InService")} = {P}Off", new { DepartmentId = departmentId, Off = false });

		public Task<int> CountTestDueAsync(int departmentId, DateTime cutoffUtc)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsHydrants")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("InService")} = {P}On AND ({Col("LastTestedOn")} IS NULL OR {Col("LastTestedOn")} < {P}Cutoff)", new { DepartmentId = departmentId, On = true, Cutoff = cutoffUtc });
	}

	public class RmsHydrantFlowTestsRepository : RmsRepositoryBase<RmsHydrantFlowTest>, IRmsHydrantFlowTestsRepository
	{
		public RmsHydrantFlowTestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsHydrantFlowTest>> GetForHydrantAsync(int departmentId, string hydrantId)
			=> QueryAsync<RmsHydrantFlowTest>($"SELECT * FROM {Tbl("RmsHydrantFlowTests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsHydrantId")} = {P}Id ORDER BY {Col("TestedOn")} DESC", new { DepartmentId = departmentId, Id = hydrantId });

		public Task<IEnumerable<RmsHydrantFlowTest>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 50000); p.Add("Start", startUtc); p.Add("End", endUtc);
			return QueryAsync<RmsHydrantFlowTest>($"SELECT * FROM {Tbl("RmsHydrantFlowTests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("TestedOn")} >= {P}Start AND {Col("TestedOn")} < {P}End ORDER BY {Col("TestedOn")}, {Col("RmsHydrantFlowTestId")} {Paging()}", p);
		}
	}

	public class RmsHydrantMaintenancesRepository : RmsRepositoryBase<RmsHydrantMaintenance>, IRmsHydrantMaintenancesRepository
	{
		public RmsHydrantMaintenancesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsHydrantMaintenance>> GetForHydrantAsync(int departmentId, string hydrantId)
			=> QueryAsync<RmsHydrantMaintenance>($"SELECT * FROM {Tbl("RmsHydrantMaintenances")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsHydrantId")} = {P}Id ORDER BY {Col("PerformedOn")} DESC", new { DepartmentId = departmentId, Id = hydrantId });
	}

	public class RmsPermitTypesRepository : RmsRepositoryBase<RmsPermitType>, IRmsPermitTypesRepository
	{
		public RmsPermitTypesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsPermitType> GetByIdForDepartmentAsync(int departmentId, string permitTypeId)
			=> QueryFirstOrDefaultAsync<RmsPermitType>($"SELECT * FROM {Tbl("RmsPermitTypes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsPermitTypeId")} = {P}Id", new { DepartmentId = departmentId, Id = permitTypeId });

		public Task<IEnumerable<RmsPermitType>> GetForDepartmentAsync(int departmentId, bool includeInactive)
			=> QueryAsync<RmsPermitType>($"SELECT * FROM {Tbl("RmsPermitTypes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{(includeInactive ? "" : $" AND {Col("IsActive")} = {P}Active")} ORDER BY {Col("Name")}", new { DepartmentId = departmentId, Active = true });
	}

	public class RmsPermitsRepository : RmsRepositoryBase<RmsPermit>, IRmsPermitsRepository
	{
		public RmsPermitsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsPermit> GetByIdForDepartmentAsync(int departmentId, string permitId)
			=> QueryFirstOrDefaultAsync<RmsPermit>($"SELECT * FROM {Tbl("RmsPermits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsPermitId")} = {P}Id", new { DepartmentId = departmentId, Id = permitId });

		private string Where(RmsPermitQuery query, DynamicParameters p)
		{
			var clauses = new List<string> { $"{Col("DepartmentId")} = {P}DepartmentId", $"{Col("DeletedOn")} IS NULL" };
			if (!string.IsNullOrWhiteSpace(query.OccupancyId)) { clauses.Add($"{Col("RmsOccupancyId")} = {P}OccupancyId"); p.Add("OccupancyId", query.OccupancyId); }
			if (!string.IsNullOrWhiteSpace(query.PermitTypeId)) { clauses.Add($"{Col("RmsPermitTypeId")} = {P}TypeId"); p.Add("TypeId", query.PermitTypeId); }
			if (query.States != null && query.States.Count > 0) { clauses.Add(InList("State", "States")); p.Add("States", InListValue(query.States)); }
			if (query.ExpiresBefore.HasValue) { clauses.Add($"{Col("ExpiresOn")} <= {P}Before"); p.Add("Before", query.ExpiresBefore.Value); }
			return string.Join(" AND ", clauses);
		}

		public Task<IEnumerable<RmsPermit>> QueryAsync(int departmentId, RmsPermitQuery query)
		{
			query ??= new RmsPermitQuery();
			var p = RmsPreventionSql.Paged(departmentId, query.Skip, query.Take);
			return QueryAsync<RmsPermit>($"SELECT * FROM {Tbl("RmsPermits")} WHERE {Where(query, p)} ORDER BY {Col("AppliedOn")} DESC, {Col("RmsPermitId")} {Paging()}", p);
		}

		public Task<int> CountAsync(int departmentId, RmsPermitQuery query)
		{
			query ??= new RmsPermitQuery();
			var p = new DynamicParameters(); p.Add("DepartmentId", departmentId);
			return ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsPermits")} WHERE {Where(query, p)}", p);
		}

		public Task<IEnumerable<RmsPermit>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsPermit>($"SELECT * FROM {Tbl("RmsPermits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("AppliedOn")} DESC", new { DepartmentId = departmentId, Id = occupancyId });

		public Task<IEnumerable<RmsPermit>> GetExpiringAsync(int departmentId, DateTime utcNow, DateTime horizonUtc, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 1000); p.Add("Now", utcNow); p.Add("Horizon", horizonUtc);
			return QueryAsync<RmsPermit>($"SELECT * FROM {Tbl("RmsPermits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {(int)RmsPermitState.Issued} AND {Col("ExpiresOn")} IS NOT NULL AND {Col("ExpiresOn")} <= {P}Horizon AND {Col("DeletedOn")} IS NULL ORDER BY {Col("ExpiresOn")}, {Col("RmsPermitId")} {Paging()}", p);
		}

		public Task<int> CountExpiringAsync(int departmentId, DateTime utcNow, DateTime horizonUtc)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsPermits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("State")} = {(int)RmsPermitState.Issued} AND {Col("ExpiresOn")} > {P}Now AND {Col("ExpiresOn")} <= {P}Horizon AND {Col("DeletedOn")} IS NULL", new { DepartmentId = departmentId, Now = utcNow, Horizon = horizonUtc });

		public Task<IEnumerable<RmsPermit>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 50000); p.Add("Start", startUtc); p.Add("End", endUtc);
			return QueryAsync<RmsPermit>($"SELECT * FROM {Tbl("RmsPermits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AppliedOn")} >= {P}Start AND {Col("AppliedOn")} < {P}End AND {Col("DeletedOn")} IS NULL ORDER BY {Col("AppliedOn")}, {Col("RmsPermitId")} {Paging()}", p);
		}

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string permitId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsPermits")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsPermitId")} = {P}Id AND {Col("RowVersion")} = {P}Expected",
				new { DepartmentId = departmentId, Id = permitId, Expected = expectedVersion, Now = DateTime.UtcNow }, cancellationToken) == 1;
	}

	public class RmsPlanReviewsRepository : RmsRepositoryBase<RmsPlanReview>, IRmsPlanReviewsRepository
	{
		public RmsPlanReviewsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsPlanReview>> GetForPermitAsync(int departmentId, string permitId)
			=> QueryAsync<RmsPlanReview>($"SELECT * FROM {Tbl("RmsPlanReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsPermitId")} = {P}Id ORDER BY {Col("CycleNumber")}", new { DepartmentId = departmentId, Id = permitId });

		public Task<RmsPlanReview> GetByIdForDepartmentAsync(int departmentId, string reviewId)
			=> QueryFirstOrDefaultAsync<RmsPlanReview>($"SELECT * FROM {Tbl("RmsPlanReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsPlanReviewId")} = {P}Id", new { DepartmentId = departmentId, Id = reviewId });
	}

	public class RmsCrrActivitiesRepository : RmsRepositoryBase<RmsCrrActivity>, IRmsCrrActivitiesRepository
	{
		public RmsCrrActivitiesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsCrrActivity> GetByIdForDepartmentAsync(int departmentId, string activityId)
			=> QueryFirstOrDefaultAsync<RmsCrrActivity>($"SELECT * FROM {Tbl("RmsCrrActivities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsCrrActivityId")} = {P}Id", new { DepartmentId = departmentId, Id = activityId });

		public Task<IEnumerable<RmsCrrActivity>> GetForRangeAsync(int departmentId, DateTime startUtc, DateTime endUtc, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 2000); p.Add("Start", startUtc); p.Add("End", endUtc);
			return QueryAsync<RmsCrrActivity>($"SELECT * FROM {Tbl("RmsCrrActivities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("OccurredOn")} >= {P}Start AND {Col("OccurredOn")} < {P}End AND {Col("DeletedOn")} IS NULL ORDER BY {Col("OccurredOn")} DESC, {Col("RmsCrrActivityId")} {Paging()}", p);
		}

		public Task<IEnumerable<RmsCrrActivity>> GetForOccupancyAsync(int departmentId, string occupancyId)
			=> QueryAsync<RmsCrrActivity>($"SELECT * FROM {Tbl("RmsCrrActivities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsOccupancyId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("OccurredOn")} DESC", new { DepartmentId = departmentId, Id = occupancyId });
	}

	public class RmsPreventionAttachmentsRepository : RmsRepositoryBase<RmsPreventionAttachment>, IRmsPreventionAttachmentsRepository
	{
		private static readonly string[] MetadataColumns =
		{
			"RmsPreventionAttachmentId", "DepartmentId", "ProtectionId", "ParentKind", "ParentId", "FileName", "ContentType", "ByteSize", "Checksum", "Description",
			"UploadedByUserId", "UploadedOn", "ScanState", "MetadataStripped", "Classification", "IsProtected", "ProtectedCatalogVersion", "CreatedOn", "ModifiedOn", "RowVersion", "DeletedOn"
		};

		public RmsPreventionAttachmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsPreventionAttachment> GetByIdForDepartmentAsync(int departmentId, string attachmentId)
			=> QueryFirstOrDefaultAsync<RmsPreventionAttachment>($"SELECT * FROM {Tbl("RmsPreventionAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsPreventionAttachmentId")} = {P}Id", new { DepartmentId = departmentId, Id = attachmentId });

		public Task<IEnumerable<RmsPreventionAttachment>> GetMetadataForParentAsync(int departmentId, RmsPreventionParentKind parentKind, string parentId)
			=> QueryAsync<RmsPreventionAttachment>($"SELECT {Cols(MetadataColumns)} FROM {Tbl("RmsPreventionAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ParentKind")} = {P}Kind AND {Col("ParentId")} = {P}ParentId AND {Col("DeletedOn")} IS NULL ORDER BY {Col("UploadedOn")}", new { DepartmentId = departmentId, Kind = (int)parentKind, ParentId = parentId });
	}

	public class RmsPreventionSequencesRepository : RmsRepositoryBase<RmsPreventionSequence>, IRmsPreventionSequencesRepository
	{
		public RmsPreventionSequencesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<int> NextAsync(int departmentId, string kind, int year, CancellationToken cancellationToken = default)
		{
			var p = new { Id = Guid.NewGuid().ToString(), DepartmentId = departmentId, Kind = kind, Year = year, Now = DateTime.UtcNow };
			var sql = IsPostgres
				? $@"INSERT INTO {Tbl("RmsPreventionSequences")} ({Cols("RmsPreventionSequenceId", "DepartmentId", "Kind", "Year", "LastValue", "ModifiedOn")}
					VALUES ({P}Id, {P}DepartmentId, {P}Kind, {P}Year, 1, {P}Now)
					ON CONFLICT ({Cols("DepartmentId", "Kind", "Year")}) DO UPDATE SET {Col("LastValue")} = {Tbl("RmsPreventionSequences")}.{Col("LastValue")} + 1, {Col("ModifiedOn")} = {P}Now
					RETURNING {Col("LastValue")}"
				: $@"MERGE {Tbl("RmsPreventionSequences")} WITH (HOLDLOCK) AS target
					USING (SELECT {P}DepartmentId AS DepartmentId, {P}Kind AS Kind, {P}Year AS Year) AS source
					ON target.[DepartmentId] = source.DepartmentId AND target.[Kind] = source.Kind AND target.[Year] = source.Year
					WHEN MATCHED THEN UPDATE SET [LastValue] = target.[LastValue] + 1, [ModifiedOn] = {P}Now
					WHEN NOT MATCHED THEN INSERT ([RmsPreventionSequenceId], [DepartmentId], [Kind], [Year], [LastValue], [ModifiedOn]) VALUES ({P}Id, {P}DepartmentId, {P}Kind, {P}Year, 1, {P}Now)
					OUTPUT inserted.[LastValue];";
			return ScalarAsync<int>(sql, p, cancellationToken);
		}
	}

	public class RmsInvestigationCasesRepository : RmsRepositoryBase<RmsInvestigationCase>, IRmsInvestigationCasesRepository
	{
		public RmsInvestigationCasesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsInvestigationCase> GetByIdForDepartmentAsync(int departmentId, string caseId)
			=> QueryFirstOrDefaultAsync<RmsInvestigationCase>($"SELECT * FROM {Tbl("RmsInvestigationCases")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id", new { DepartmentId = departmentId, Id = caseId });

		public Task<IEnumerable<RmsInvestigationCase>> GetByIdsAsync(int departmentId, IEnumerable<string> caseIds)
		{
			var ids = InListValue(caseIds);
			if (ids.Length == 0) return Task.FromResult<IEnumerable<RmsInvestigationCase>>(new List<RmsInvestigationCase>());
			return QueryAsync<RmsInvestigationCase>($"SELECT * FROM {Tbl("RmsInvestigationCases")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RmsInvestigationCaseId", "Ids")} AND {Col("DeletedOn")} IS NULL ORDER BY {Col("OpenedOn")} DESC", new { DepartmentId = departmentId, Ids = ids });
		}

		public Task<IEnumerable<RmsInvestigationCase>> GetForDepartmentAsync(int departmentId, bool includeClosed, int skip, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, skip, take);
			return QueryAsync<RmsInvestigationCase>($"SELECT * FROM {Tbl("RmsInvestigationCases")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{(includeClosed ? "" : $" AND {Col("State")} <> {(int)RmsInvestigationCaseState.Closed}")} ORDER BY {Col("OpenedOn")} DESC, {Col("RmsInvestigationCaseId")} {Paging()}", p);
		}

		public Task<int> CountOpenAsync(int departmentId)
			=> ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsInvestigationCases")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL AND {Col("State")} <> {(int)RmsInvestigationCaseState.Closed}", new { DepartmentId = departmentId });

		public async Task<bool> TryBumpRowVersionAsync(int departmentId, string caseId, long expectedVersion, CancellationToken cancellationToken = default)
			=> await ExecuteAsync($"UPDATE {Tbl("RmsInvestigationCases")} SET {Col("RowVersion")} = {Col("RowVersion")} + 1, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id AND {Col("RowVersion")} = {P}Expected",
				new { DepartmentId = departmentId, Id = caseId, Expected = expectedVersion, Now = DateTime.UtcNow }, cancellationToken) == 1;
	}

	public class RmsInvestigationCaseIncidentsRepository : RmsRepositoryBase<RmsInvestigationCaseIncident>, IRmsInvestigationCaseIncidentsRepository
	{
		public RmsInvestigationCaseIncidentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsInvestigationCaseIncident>> GetForCaseAsync(int departmentId, string caseId)
			=> QueryAsync<RmsInvestigationCaseIncident>($"SELECT * FROM {Tbl("RmsInvestigationCaseIncidents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id ORDER BY {Col("LinkedOn")}", new { DepartmentId = departmentId, Id = caseId });

		public Task<IEnumerable<RmsInvestigationCaseIncident>> GetForRecordAsync(int departmentId, string recordId)
			=> QueryAsync<RmsInvestigationCaseIncident>($"SELECT * FROM {Tbl("RmsInvestigationCaseIncidents")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId", new { DepartmentId = departmentId, RecordId = recordId });
	}

	public class RmsInvestigationCaseMembersRepository : RmsRepositoryBase<RmsInvestigationCaseMember>, IRmsInvestigationCaseMembersRepository
	{
		public RmsInvestigationCaseMembersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsInvestigationCaseMember>> GetForCaseAsync(int departmentId, string caseId)
			=> QueryAsync<RmsInvestigationCaseMember>($"SELECT * FROM {Tbl("RmsInvestigationCaseMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id ORDER BY {Col("AddedOn")}", new { DepartmentId = departmentId, Id = caseId });

		public Task<IEnumerable<RmsInvestigationCaseMember>> GetActiveForUserAsync(int departmentId, string userId)
			=> QueryAsync<RmsInvestigationCaseMember>($"SELECT * FROM {Tbl("RmsInvestigationCaseMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId AND {Col("RemovedOn")} IS NULL", new { DepartmentId = departmentId, UserId = userId });

		public Task<RmsInvestigationCaseMember> GetByIdForDepartmentAsync(int departmentId, string memberId)
			=> QueryFirstOrDefaultAsync<RmsInvestigationCaseMember>($"SELECT * FROM {Tbl("RmsInvestigationCaseMembers")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseMemberId")} = {P}Id", new { DepartmentId = departmentId, Id = memberId });
	}

	public class RmsInvestigationNotesRepository : RmsRepositoryBase<RmsInvestigationNote>, IRmsInvestigationNotesRepository
	{
		public RmsInvestigationNotesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsInvestigationNote> GetByIdForDepartmentAsync(int departmentId, string noteId)
			=> QueryFirstOrDefaultAsync<RmsInvestigationNote>($"SELECT * FROM {Tbl("RmsInvestigationNotes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationNoteId")} = {P}Id", new { DepartmentId = departmentId, Id = noteId });

		public Task<IEnumerable<RmsInvestigationNote>> GetForCaseAsync(int departmentId, string caseId)
			=> QueryAsync<RmsInvestigationNote>($"SELECT * FROM {Tbl("RmsInvestigationNotes")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("OccurredOn")}", new { DepartmentId = departmentId, Id = caseId });

		public Task<int> LockForCaseAsync(int departmentId, string caseId, DateTime utcNow, CancellationToken cancellationToken = default)
			=> ExecuteAsync($"UPDATE {Tbl("RmsInvestigationNotes")} SET {Col("IsLocked")} = {P}Locked, {Col("ModifiedOn")} = {P}Now WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id AND {Col("IsLocked")} = {P}Unlocked", new { DepartmentId = departmentId, Id = caseId, Now = utcNow, Locked = true, Unlocked = false }, cancellationToken);
	}

	public class RmsInvestigationEvidenceRepository : RmsRepositoryBase<RmsInvestigationEvidence>, IRmsInvestigationEvidenceRepository
	{
		public RmsInvestigationEvidenceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsInvestigationEvidence> GetByIdForDepartmentAsync(int departmentId, string evidenceId)
			=> QueryFirstOrDefaultAsync<RmsInvestigationEvidence>($"SELECT * FROM {Tbl("RmsInvestigationEvidence")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationEvidenceId")} = {P}Id", new { DepartmentId = departmentId, Id = evidenceId });

		public Task<IEnumerable<RmsInvestigationEvidence>> GetForCaseAsync(int departmentId, string caseId)
			=> QueryAsync<RmsInvestigationEvidence>($"SELECT * FROM {Tbl("RmsInvestigationEvidence")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id AND {Col("DeletedOn")} IS NULL ORDER BY {Col("CollectedOn")}", new { DepartmentId = departmentId, Id = caseId });
	}

	public class RmsInvestigationCustodyRepository : RmsRepositoryBase<RmsInvestigationCustody>, IRmsInvestigationCustodyRepository
	{
		public RmsInvestigationCustodyRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<RmsInvestigationCustody>> GetForEvidenceAsync(int departmentId, string evidenceId)
			=> QueryAsync<RmsInvestigationCustody>($"SELECT * FROM {Tbl("RmsInvestigationCustody")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationEvidenceId")} = {P}Id ORDER BY {Col("Sequence")}", new { DepartmentId = departmentId, Id = evidenceId });
	}

	public class RmsInvestigationReferralsRepository : RmsRepositoryBase<RmsInvestigationReferral>, IRmsInvestigationReferralsRepository
	{
		public RmsInvestigationReferralsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsInvestigationReferral> GetByIdForDepartmentAsync(int departmentId, string referralId)
			=> QueryFirstOrDefaultAsync<RmsInvestigationReferral>($"SELECT * FROM {Tbl("RmsInvestigationReferrals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationReferralId")} = {P}Id", new { DepartmentId = departmentId, Id = referralId });

		public Task<IEnumerable<RmsInvestigationReferral>> GetForCaseAsync(int departmentId, string caseId)
			=> QueryAsync<RmsInvestigationReferral>($"SELECT * FROM {Tbl("RmsInvestigationReferrals")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsInvestigationCaseId")} = {P}Id ORDER BY {Col("ReferredOn")}", new { DepartmentId = departmentId, Id = caseId });
	}

	public class RmsQualityRubricsRepository : RmsRepositoryBase<RmsQualityRubric>, IRmsQualityRubricsRepository
	{
		public RmsQualityRubricsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsQualityRubric> GetByIdForDepartmentAsync(int departmentId, string rubricId)
			=> QueryFirstOrDefaultAsync<RmsQualityRubric>($"SELECT * FROM {Tbl("RmsQualityRubrics")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsQualityRubricId")} = {P}Id", new { DepartmentId = departmentId, Id = rubricId });

		public Task<IEnumerable<RmsQualityRubric>> GetForDepartmentAsync(int departmentId, bool includeInactive)
			=> QueryAsync<RmsQualityRubric>($"SELECT * FROM {Tbl("RmsQualityRubrics")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DeletedOn")} IS NULL{(includeInactive ? "" : $" AND {Col("IsActive")} = {P}Active")} ORDER BY {Col("Name")}", new { DepartmentId = departmentId, Active = true });
	}

	public class RmsQualityReviewsRepository : RmsRepositoryBase<RmsQualityReview>, IRmsQualityReviewsRepository
	{
		public RmsQualityReviewsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<RmsQualityReview> GetByIdForDepartmentAsync(int departmentId, string reviewId)
			=> QueryFirstOrDefaultAsync<RmsQualityReview>($"SELECT * FROM {Tbl("RmsQualityReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsQualityReviewId")} = {P}Id", new { DepartmentId = departmentId, Id = reviewId });

		public Task<IEnumerable<RmsQualityReview>> GetForRecordAsync(int departmentId, string recordId)
			=> QueryAsync<RmsQualityReview>($"SELECT * FROM {Tbl("RmsQualityReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}RecordId ORDER BY {Col("SampledOn")} DESC", new { DepartmentId = departmentId, RecordId = recordId });

		public Task<IEnumerable<RmsQualityReview>> GetPendingAsync(int departmentId, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 1000);
			return QueryAsync<RmsQualityReview>($"SELECT * FROM {Tbl("RmsQualityReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ScoredOn")} IS NULL ORDER BY {Col("SampledOn")}, {Col("RmsQualityReviewId")} {Paging()}", p);
		}

		public Task<IEnumerable<RmsQualityReview>> GetScoredSinceAsync(int departmentId, DateTime sinceUtc, int take)
		{
			var p = RmsPreventionSql.Paged(departmentId, 0, take, 5000); p.Add("Since", sinceUtc);
			return QueryAsync<RmsQualityReview>($"SELECT * FROM {Tbl("RmsQualityReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ScoredOn")} >= {P}Since ORDER BY {Col("ScoredOn")} DESC, {Col("RmsQualityReviewId")} {Paging()}", p);
		}

		public async Task<IEnumerable<string>> GetReviewedRecordIdsAsync(int departmentId, IEnumerable<string> recordIds)
		{
			var ids = InListValue(recordIds);
			if (ids.Length == 0) return new List<string>();
			return await QueryAsync<string>($"SELECT DISTINCT {Col("RecordId")} FROM {Tbl("RmsQualityReviews")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("RecordId", "Ids")}", new { DepartmentId = departmentId, Ids = ids });
		}
	}
}

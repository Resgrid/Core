using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>POI capabilities and their assignments to POIs (M0315).</summary>
	public class PoiCapabilitiesRepository : RmsRepositoryBase<PoiCapability>, IPoiCapabilitiesRepository
	{
		public PoiCapabilitiesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<PoiCapabilityAssignment>> GetAssignmentsByDepartmentIdAsync(int departmentId)
		{
			return QueryAsync<PoiCapabilityAssignment>(
				$"SELECT * FROM {Tbl("PoiCapabilityAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });
		}

		public Task<IEnumerable<PoiCapabilityAssignment>> GetAssignmentsByPoiIdAsync(int departmentId, int poiId)
		{
			return QueryAsync<PoiCapabilityAssignment>(
				$"SELECT * FROM {Tbl("PoiCapabilityAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("PoiId")} = {P}PoiId",
				new { DepartmentId = departmentId, PoiId = poiId });
		}

		public async Task ReplacePoiAssignmentsAsync(int departmentId, int poiId, IEnumerable<string> capabilityIds, CancellationToken cancellationToken = default)
		{
			var ids = (capabilityIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await ExecuteAsync(
					$"DELETE FROM {Tbl("PoiCapabilityAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("PoiId")} = {P}PoiId",
					new { DepartmentId = departmentId, PoiId = poiId }, cancellationToken);

				foreach (var id in ids)
				{
					await ExecuteAsync(
						$"INSERT INTO {Tbl("PoiCapabilityAssignments")} ({Cols("PoiCapabilityAssignmentId", "DepartmentId", "PoiId", "PoiCapabilityId")}) " +
						$"VALUES ({P}Id, {P}DepartmentId, {P}PoiId, {P}CapabilityId)",
						new { Id = Guid.NewGuid().ToString(), DepartmentId = departmentId, PoiId = poiId, CapabilityId = id }, cancellationToken);
				}

				if (owns)
					UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns)
					UnitOfWork.DiscardChanges();
				throw;
			}
		}

		public Task<int> DeleteAssignmentsForPoisAsync(int departmentId, IEnumerable<int> poiIds, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((poiIds ?? Enumerable.Empty<int>()).Distinct());
			if (ids.Length == 0)
				return Task.FromResult(0);

			return ExecuteAsync(
				$"DELETE FROM {Tbl("PoiCapabilityAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("PoiId", "PoiIds")}",
				new { DepartmentId = departmentId, PoiIds = ids }, cancellationToken);
		}

		public Task<int> DeleteAssignmentsForCapabilityAsync(int departmentId, string poiCapabilityId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("PoiCapabilityAssignments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("PoiCapabilityId")} = {CaseInsensitive("CapabilityId")}",
				new { DepartmentId = departmentId, CapabilityId = poiCapabilityId }, cancellationToken);
		}
	}

	/// <summary>Destination availability reports, one per POI (M0315).</summary>
	public class PoiAvailabilitiesRepository : RmsRepositoryBase<PoiAvailability>, IPoiAvailabilitiesRepository
	{
		public PoiAvailabilitiesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<PoiAvailability> GetByPoiIdAsync(int departmentId, int poiId)
		{
			return QueryFirstOrDefaultAsync<PoiAvailability>(
				$"SELECT * FROM {Tbl("PoiAvailabilities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("PoiId")} = {P}PoiId",
				new { DepartmentId = departmentId, PoiId = poiId });
		}

		public Task<int> DeleteForPoisAsync(int departmentId, IEnumerable<int> poiIds, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((poiIds ?? Enumerable.Empty<int>()).Distinct());
			if (ids.Length == 0)
				return Task.FromResult(0);

			return ExecuteAsync(
				$"DELETE FROM {Tbl("PoiAvailabilities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("PoiId", "PoiIds")}",
				new { DepartmentId = departmentId, PoiIds = ids }, cancellationToken);
		}

		public async Task RemoveCapabilityAsync(int departmentId, string poiCapabilityId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(poiCapabilityId))
				return;

			var reports = await QueryAsync<PoiAvailability>(
				$"SELECT * FROM {Tbl("PoiAvailabilities")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UnavailableCapabilityIds")} IS NOT NULL",
				new { DepartmentId = departmentId }, cancellationToken);

			foreach (var report in reports)
			{
				var ids = report.GetUnavailableCapabilityIds();
				var kept = ids.Where(x => !string.Equals(x, poiCapabilityId, StringComparison.OrdinalIgnoreCase)).ToList();
				if (kept.Count == ids.Count)
					continue;

				await ExecuteAsync(
					$"UPDATE {Tbl("PoiAvailabilities")} SET {Col("UnavailableCapabilityIds")} = {P}Ids WHERE {Col("PoiAvailabilityId")} = {CaseInsensitive("Id")}",
					new { Id = report.PoiAvailabilityId, Ids = kept.Count == 0 ? null : string.Join(",", kept) }, cancellationToken);
			}
		}
	}
}

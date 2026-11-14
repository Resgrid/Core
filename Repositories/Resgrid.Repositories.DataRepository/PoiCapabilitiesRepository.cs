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
				// Two saves of one POI's capabilities at once could both delete before either inserts and leave the union of the
				// two sets. Locking the POI row first makes the second wait for the first to commit, then replace its set.
				await ScalarAsync<int>(IsPostgres
						? $"SELECT {Col("PoiId")} FROM {Tbl("Pois")} WHERE {Col("PoiId")} = {P}PoiId FOR UPDATE"
						: $"SELECT {Col("PoiId")} FROM {Tbl("Pois")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("PoiId")} = {P}PoiId",
					new { PoiId = poiId }, cancellationToken);

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

			foreach (var read in reports)
			{
				// The write only lands on the list it was computed from: a report someone changed since the read (a dispatcher
				// marking another capability down, or another removal) is read again rather than overwritten with the old list.
				// Still holding the capability after every attempt is an error: the caller must not go on as if it were removed.
				var report = read;
				for (var attempt = 0; ; attempt++)
				{
					var ids = report?.GetUnavailableCapabilityIds() ?? new List<string>();
					var kept = ids.Where(x => !string.Equals(x, poiCapabilityId, StringComparison.OrdinalIgnoreCase)).ToList();
					if (kept.Count == ids.Count)
						break;

					if (attempt >= MaxRemoveAttempts)
						throw new InvalidOperationException(
							$"Availability report {report.PoiAvailabilityId} kept changing; capability {poiCapabilityId} could not be removed from it. Try again.");

					var changed = await ExecuteAsync(
						$"UPDATE {Tbl("PoiAvailabilities")} SET {Col("UnavailableCapabilityIds")} = {P}Ids " +
						$"WHERE {Col("PoiAvailabilityId")} = {CaseInsensitive("Id")} AND {Col("UnavailableCapabilityIds")} = {P}Previous",
						new { Id = report.PoiAvailabilityId, Ids = kept.Count == 0 ? null : string.Join(",", kept), Previous = report.UnavailableCapabilityIds }, cancellationToken);
					if (changed > 0)
						break;

					report = await GetByPoiIdAsync(departmentId, report.PoiId);
				}
			}
		}

		/// <summary>Conditional writes of one report <see cref="RemoveCapabilityAsync"/> tries while others keep changing it.</summary>
		private const int MaxRemoveAttempts = 3;
	}
}

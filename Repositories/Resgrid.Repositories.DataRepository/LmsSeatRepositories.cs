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
	/// <summary>LMS seats (M0351). Member ids are citext on PostgreSQL, so their parameters are cast (pg-citext-text-param-equality).</summary>
	public class LmsMemberSeatsRepository : RmsRepositoryBase<LmsMemberSeat>, ILmsMemberSeatsRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "UserId", "EnabledOn", "EnabledByUserId" };

		public LmsMemberSeatsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsMemberSeat>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsMemberSeat>(
				$"SELECT {Cols("LmsMemberSeatId", "DepartmentId", "UserId", "EnabledOn", "EnabledByUserId")} FROM {Tbl("LmsMemberSeats")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("EnabledOn")}, {Col("LmsMemberSeatId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<bool> AddAsync(LmsMemberSeat seat, CancellationToken cancellationToken = default)
		{
			if (seat == null || seat.DepartmentId <= 0 || string.IsNullOrWhiteSpace(seat.UserId))
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", seat.DepartmentId);
			parameters.Add("UserId", seat.UserId);
			parameters.Add("EnabledOn", DatabaseTimestamp(seat.EnabledOn), System.Data.DbType.DateTime2);
			parameters.Add("EnabledByUserId", seat.EnabledByUserId);

			// A repeat (double click, two admins) is not an error: the unique (department, member) index keeps one row.
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsMemberSeats")} ({Cols(Columns)}) VALUES ({P}DepartmentId, {P}UserId, {P}EnabledOn, {P}EnabledByUserId) " +
				  $"ON CONFLICT ({Cols("DepartmentId", "UserId")}) DO NOTHING"
				: $"INSERT INTO {Tbl("LmsMemberSeats")} ({Cols(Columns)}) SELECT {P}DepartmentId, {P}UserId, {P}EnabledOn, {P}EnabledByUserId " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("LmsMemberSeats")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId)";
			try
			{
				return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public async Task<int> RemoveAsync(int departmentId, IEnumerable<string> userIds, CancellationToken cancellationToken = default)
		{
			var ids = InListValue((userIds ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase));
			if (departmentId <= 0 || ids.Length == 0)
				return 0;

			var removed = 0;
			foreach (var chunk in ids.Chunk(1000))
				removed += await ExecuteAsync(
					$"DELETE FROM {Tbl("LmsMemberSeats")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList("UserId", "UserIds")}",
					new { DepartmentId = departmentId, UserIds = chunk }, cancellationToken);
			return removed;
		}
	}

	/// <summary>What the LMS gate last saw of each department's add-on (M0352); one row per department.</summary>
	public class LmsAddonStatesRepository : RmsRepositoryBase<LmsAddonState>, ILmsAddonStatesRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "Packs", "PaidThroughUtc", "RenewalCancelled", "LastActiveOn", "LapsedSinceUtc", "UpdatedOn" };

		public LmsAddonStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<LmsAddonState> GetForDepartmentAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<LmsAddonState>($"SELECT * FROM {Tbl("LmsAddonStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId", new { DepartmentId = departmentId });

		public async Task<bool> TryChangePacksAsync(int departmentId, int expectedPacks, int packs, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || expectedPacks == packs)
				return false;
			return await ExecuteAsync(
				$"UPDATE {Tbl("LmsAddonStates")} SET {Col("Packs")} = {P}Packs WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Packs")} = {P}ExpectedPacks",
				new { DepartmentId = departmentId, Packs = packs, ExpectedPacks = expectedPacks }, cancellationToken) > 0;
		}

		public async Task SaveForDepartmentAsync(LmsAddonState state, CancellationToken cancellationToken = default)
		{
			if (state == null || state.DepartmentId <= 0)
				return;

			var parameters = Parameters(state);
			var update = $"UPDATE {Tbl("LmsAddonStates")} SET {string.Join(", ", Columns.Where(c => c != "DepartmentId").Select(c => $"{Col(c)} = {P}{c}"))} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId";
			if (await ExecuteAsync(update, parameters, cancellationToken) > 0)
				return;

			try
			{
				await ExecuteAsync($"INSERT INTO {Tbl("LmsAddonStates")} ({Cols(Columns)}) VALUES ({string.Join(", ", Columns.Select(c => P + c))})", parameters, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another request inserted the department's row first: write over it.
				await ExecuteAsync(update, parameters, cancellationToken);
			}
		}

		private static DynamicParameters Parameters(LmsAddonState state)
		{
			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", state.DepartmentId);
			parameters.Add("Packs", state.Packs);
			parameters.Add("PaidThroughUtc", state.PaidThroughUtc.HasValue ? DatabaseTimestamp(state.PaidThroughUtc.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("RenewalCancelled", state.RenewalCancelled);
			parameters.Add("LastActiveOn", state.LastActiveOn.HasValue ? DatabaseTimestamp(state.LastActiveOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("LapsedSinceUtc", state.LapsedSinceUtc.HasValue ? DatabaseTimestamp(state.LapsedSinceUtc.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("UpdatedOn", DatabaseTimestamp(state.UpdatedOn), System.Data.DbType.DateTime2);
			return parameters;
		}
	}

	/// <summary>LMS add-on lapse periods (M0352); at most one open per department (filtered unique index).</summary>
	public class LmsAddonLapsesRepository : RmsRepositoryBase<LmsAddonLapse>, ILmsAddonLapsesRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "StartedOn", "EndedOn", "PaidThroughUtc", "Reason", "Packs", "DetectedOn" };

		public LmsAddonLapsesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<LmsAddonLapse>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<LmsAddonLapse>(
				$"SELECT * FROM {Tbl("LmsAddonLapses")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("StartedOn")}, {Col("LmsAddonLapseId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public Task<LmsAddonLapse> GetOpenAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<LmsAddonLapse>($"SELECT * FROM {Tbl("LmsAddonLapses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EndedOn")} IS NULL",
				new { DepartmentId = departmentId });

		public async Task<bool> OpenAsync(LmsAddonLapse lapse, CancellationToken cancellationToken = default)
		{
			if (lapse == null || lapse.DepartmentId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("DepartmentId", lapse.DepartmentId);
			parameters.Add("StartedOn", DatabaseTimestamp(lapse.StartedOn), System.Data.DbType.DateTime2);
			parameters.Add("PaidThroughUtc", lapse.PaidThroughUtc.HasValue ? DatabaseTimestamp(lapse.PaidThroughUtc.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
			parameters.Add("Reason", lapse.Reason);
			parameters.Add("Packs", lapse.Packs);
			parameters.Add("DetectedOn", DatabaseTimestamp(lapse.DetectedOn), System.Data.DbType.DateTime2);

			// EndedOn is always null on open; written as a literal (an untyped null parameter in a comparison fails on PostgreSQL).
			var columns = Columns.Where(c => c != "EndedOn").ToArray();
			var values = string.Join(", ", columns.Select(c => P + c));
			var sql = IsPostgres
				? $"INSERT INTO {Tbl("LmsAddonLapses")} ({Cols(columns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("LmsAddonLapses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EndedOn")} IS NULL)"
				: $"INSERT INTO {Tbl("LmsAddonLapses")} ({Cols(columns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("LmsAddonLapses")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("EndedOn")} IS NULL)";
			try
			{
				return await ExecuteAsync(sql, parameters, cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public async Task<LmsAddonLapse> CloseAsync(int departmentId, DateTime endedOnUtc, CancellationToken cancellationToken = default)
		{
			var open = await GetOpenAsync(departmentId);
			if (open == null)
				return null;

			// A lapse never ends before it began (a reactivation seen inside the dunning grace window it was opened for).
			var ended = endedOnUtc < open.StartedOn ? open.StartedOn : endedOnUtc;
			var parameters = new DynamicParameters();
			parameters.Add("Id", open.LmsAddonLapseId);
			parameters.Add("DepartmentId", departmentId);
			parameters.Add("EndedOn", DatabaseTimestamp(ended), System.Data.DbType.DateTime2);
			var closed = await ExecuteAsync(
				$"UPDATE {Tbl("LmsAddonLapses")} SET {Col("EndedOn")} = {P}EndedOn WHERE {Col("LmsAddonLapseId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("EndedOn")} IS NULL",
				parameters, cancellationToken);
			if (closed != 1)
				return null;

			open.EndedOn = ended;
			return open;
		}
	}
}

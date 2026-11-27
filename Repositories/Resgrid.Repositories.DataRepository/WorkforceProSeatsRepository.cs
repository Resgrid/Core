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
	/// Workforce Pro seats (the Workforce Pro migration), the LMS seat repository's shape. Member ids are citext on PostgreSQL, so their
	/// parameters are cast (pg-citext-text-param-equality).
	/// </summary>
	public class WorkforceProSeatsRepository : RmsRepositoryBase<WorkforceProSeat>, IWorkforceProSeatsRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "UserId", "EnabledOn", "EnabledByUserId" };

		public WorkforceProSeatsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<WorkforceProSeat>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<WorkforceProSeat>(
				$"SELECT {Cols("WorkforceProSeatId", "DepartmentId", "UserId", "EnabledOn", "EnabledByUserId")} FROM {Tbl("WorkforceProSeats")} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("EnabledOn")}, {Col("WorkforceProSeatId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<bool> AddAsync(WorkforceProSeat seat, CancellationToken cancellationToken = default)
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
				? $"INSERT INTO {Tbl("WorkforceProSeats")} ({Cols(Columns)}) VALUES ({P}DepartmentId, {P}UserId, {P}EnabledOn, {P}EnabledByUserId) " +
				  $"ON CONFLICT ({Cols("DepartmentId", "UserId")}) DO NOTHING"
				: $"INSERT INTO {Tbl("WorkforceProSeats")} ({Cols(Columns)}) SELECT {P}DepartmentId, {P}UserId, {P}EnabledOn, {P}EnabledByUserId " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl("WorkforceProSeats")} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId)";
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
					$"DELETE FROM {Tbl("WorkforceProSeats")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {CaseInsensitiveInList("UserId", "UserIds")}",
					new { DepartmentId = departmentId, UserIds = chunk }, cancellationToken);
			return removed;
		}
	}
}

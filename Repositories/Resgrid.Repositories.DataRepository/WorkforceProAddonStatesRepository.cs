using System;
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
	/// What the Workforce Pro gate last saw of each department's add-on (M0423), one row per department (a unique index): the
	/// LmsAddonStatesRepository's upsert.
	/// </summary>
	public class WorkforceProAddonStatesRepository : RmsRepositoryBase<WorkforceProAddonState>, IWorkforceProAddonStatesRepository
	{
		private static readonly string[] Columns = { "DepartmentId", "Packs", "PaidThroughUtc", "RenewalCancelled", "LastActiveOn", "LapsedSinceUtc", "UpdatedOn" };

		public WorkforceProAddonStatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<WorkforceProAddonState> GetForDepartmentAsync(int departmentId) =>
			QueryFirstOrDefaultAsync<WorkforceProAddonState>(
				$"SELECT {Cols("WorkforceProAddonStateId", "DepartmentId", "Packs", "PaidThroughUtc", "RenewalCancelled", "LastActiveOn", "LapsedSinceUtc", "UpdatedOn")} " +
				$"FROM {Tbl("WorkforceProAddonStates")} WHERE {Col("DepartmentId")} = {P}DepartmentId",
				new { DepartmentId = departmentId });

		public async Task SaveForDepartmentAsync(WorkforceProAddonState state, CancellationToken cancellationToken = default)
		{
			if (state == null || state.DepartmentId <= 0)
				return;

			var parameters = Parameters(state);
			var update = $"UPDATE {Tbl("WorkforceProAddonStates")} SET {string.Join(", ", Columns.Where(c => c != "DepartmentId").Select(c => $"{Col(c)} = {P}{c}"))} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId";
			if (await ExecuteAsync(update, parameters, cancellationToken) > 0)
				return;

			try
			{
				await ExecuteAsync($"INSERT INTO {Tbl("WorkforceProAddonStates")} ({Cols(Columns)}) VALUES ({string.Join(", ", Columns.Select(c => P + c))})", parameters, cancellationToken);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another request inserted the department's row first: write over it.
				await ExecuteAsync(update, parameters, cancellationToken);
			}
		}

		private static DynamicParameters Parameters(WorkforceProAddonState state)
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
}

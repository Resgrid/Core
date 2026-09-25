using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Per-department AI dispatch settings (M0241). Writes are compare-and-swap on Revision so concurrent admin edits never silently overwrite.</summary>
	public sealed class AiDispatchConfigRepository : RmsRepositoryBase<DepartmentAiDispatchConfig>, IAiDispatchConfigRepository
	{
		private static readonly string[] Columns = typeof(DepartmentAiDispatchConfig).GetProperties()
			.Where(p => p.CanWrite && !new DepartmentAiDispatchConfig().IgnoredProperties.Contains(p.Name)).Select(p => p.Name).ToArray();

		public AiDispatchConfigRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }

		public async Task<DepartmentAiDispatchConfig> GetAsync(int departmentId, CancellationToken cancellationToken)
		{
			var row = await QueryFirstOrDefaultAsync<DepartmentAiDispatchConfig>(
				$"SELECT {Cols(Columns)} FROM {Tbl("DepartmentAiDispatchConfigs")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, cancellationToken);
			if (row?.UpdatedOnUtc != null)
				row.UpdatedOnUtc = DateTime.SpecifyKind(row.UpdatedOnUtc.Value, DateTimeKind.Utc);
			return row;
		}

		public async Task<bool> SaveAsync(DepartmentAiDispatchConfig config, long expectedRevision, CancellationToken cancellationToken)
		{
			if (config == null || config.DepartmentId <= 0 || expectedRevision < 0)
				throw new ArgumentException("Invalid AI dispatch settings.");
			var values = new
			{
				config.DepartmentId, config.MinimumConfidence, config.SenderAllowlist, config.MonthlyTokenCap, config.AuditRetentionDays, config.FillCallType, config.FillAddress,
				config.FillContact, config.FillIncidentNumber, config.RenamePlaceholder, config.AddSummaryNote, config.FlagRelatedCalls, Revision = expectedRevision + 1,
				config.UpdatedByUserId, UpdatedOnUtc = config.UpdatedOnUtc.HasValue ? DatabaseTimestamp(config.UpdatedOnUtc.Value) : (DateTime?)null, Expected = expectedRevision
			};
			if (expectedRevision == 0)
			{
				try
				{
					return await ExecuteAsync($"INSERT INTO {Tbl("DepartmentAiDispatchConfigs")} ({Cols(Columns)}) VALUES ({string.Join(",", Columns.Select(c => P + c))})", values, cancellationToken) == 1;
				}
				catch (Exception ex) when (IsUniqueViolation(ex))
				{
					return false;
				}
			}
			var updated = Columns.Where(c => c != "DepartmentId").ToArray();
			return await ExecuteAsync($"UPDATE {Tbl("DepartmentAiDispatchConfigs")} SET {string.Join(",", updated.Select(c => Col(c) + "=" + P + c))} " +
				$"WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Revision")}={P}Expected", values, cancellationToken) == 1;
		}
	}
}

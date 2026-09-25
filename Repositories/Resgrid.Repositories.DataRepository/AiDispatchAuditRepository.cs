using System;
using System.Collections.Generic;
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
	/// <summary>Metadata-only AI dispatch audit (M0240). The unique (DepartmentId, CallId) index is the at-least-once idempotency claim.</summary>
	public sealed class AiDispatchAuditRepository : RmsRepositoryBase<AiDispatchAuditRow>, IAiDispatchAuditRepository
	{
		private static readonly string[] Columns = typeof(AiDispatchAuditRow).GetProperties()
			.Where(p => p.CanWrite && !new AiDispatchAuditRow().IgnoredProperties.Contains(p.Name)).Select(p => p.Name).ToArray();

		public AiDispatchAuditRepository(IConnectionProvider connection, SqlConfiguration config, IUnitOfWork uow, IQueryFactory queries) : base(connection, config, uow, queries) { }

		public async Task<bool> TryClaimAsync(AiDispatchAuditRow row, CancellationToken cancellationToken)
		{
			if (row == null || row.DepartmentId <= 0 || row.CallId <= 0 || !Guid.TryParseExact(row.AiDispatchAuditId, "D", out _))
				throw new ArgumentException("Invalid AI dispatch claim.");
			try
			{
				return await ExecuteAsync($"INSERT INTO {Tbl("AiDispatchAudits")} ({Cols(Columns)}) VALUES ({string.Join(",", Columns.Select(c => P + c))})",
					Stamp(row), cancellationToken) == 1;
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				return false;
			}
		}

		public async Task CompleteAsync(AiDispatchAuditRow row, CancellationToken cancellationToken)
		{
			var updated = Columns.Where(c => c is not ("AiDispatchAuditId" or "DepartmentId" or "CallId" or "CreatedOnUtc")).ToArray();
			await ExecuteAsync($"UPDATE {Tbl("AiDispatchAudits")} SET {string.Join(",", updated.Select(c => Col(c) + "=" + P + c))} WHERE {Col("AiDispatchAuditId")}={P}AiDispatchAuditId AND {Col("DepartmentId")}={P}DepartmentId",
				Stamp(row), cancellationToken);
		}

		public async Task<List<AiDispatchAuditListItem>> GetRecentAsync(int departmentId, int take, CancellationToken cancellationToken)
		{
			take = Math.Clamp(take, 1, 200);
			// Only call numbers are joined: names and content stay behind the call page's own authorization and protection.
			var select = $"{string.Join(",", Columns.Select(c => "a." + Col(c)))},c.{Col("Number")} AS {Col("CallNumber")},r.{Col("Number")} AS {Col("RelatedCallNumber")}";
			var from = $"{Tbl("AiDispatchAudits")} a LEFT JOIN {Tbl("Calls")} c ON c.{Col("CallId")}=a.{Col("CallId")} AND c.{Col("DepartmentId")}=a.{Col("DepartmentId")} " +
				$"LEFT JOIN {Tbl("Calls")} r ON r.{Col("CallId")}=a.{Col("RelatedCallId")} AND r.{Col("DepartmentId")}=a.{Col("DepartmentId")} WHERE a.{Col("DepartmentId")}={P}DepartmentId";
			var sql = IsPostgres
				? $"SELECT {select} FROM {from} ORDER BY a.{Col("CreatedOnUtc")} DESC LIMIT {take}"
				: $"SELECT TOP ({take}) {select} FROM {from} ORDER BY a.{Col("CreatedOnUtc")} DESC";
			var rows = await QueryAsync<AuditWithNumbers>(sql, new { DepartmentId = departmentId }, cancellationToken);
			return rows.Select(r => new AiDispatchAuditListItem { Audit = r, CallNumber = r.CallNumber, RelatedCallNumber = r.RelatedCallNumber }).ToList();
		}

		public Task<int> PruneAsync(int departmentId, DateTime cutoffUtc, CancellationToken cancellationToken) =>
			ExecuteAsync($"DELETE FROM {Tbl("AiDispatchAudits")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("CreatedOnUtc")}<{P}Cutoff",
				new { DepartmentId = departmentId, Cutoff = DatabaseTimestamp(cutoffUtc) }, cancellationToken);

		private sealed class AuditWithNumbers
		{
			public string AiDispatchAuditId { get; set; }
			public int DepartmentId { get; set; }
			public int CallId { get; set; }
			public string Mode { get; set; }
			public string Outcome { get; set; }
			public decimal? Confidence { get; set; }
			public string AppliedFields { get; set; }
			public int RejectedCount { get; set; }
			public int? RelatedCallId { get; set; }
			public string PromptVersion { get; set; }
			public string ModelName { get; set; }
			public string ModelRevision { get; set; }
			public string RuntimeDigest { get; set; }
			public int InputTokens { get; set; }
			public int OutputTokens { get; set; }
			public int LatencyMs { get; set; }
			public DateTime CreatedOnUtc { get; set; }
			public DateTime? CompletedOnUtc { get; set; }
			public string CallNumber { get; set; }
			public string RelatedCallNumber { get; set; }

			public static implicit operator AiDispatchAuditRow(AuditWithNumbers r) => new AiDispatchAuditRow
			{
				AiDispatchAuditId = r.AiDispatchAuditId, DepartmentId = r.DepartmentId, CallId = r.CallId, Mode = r.Mode, Outcome = r.Outcome, Confidence = r.Confidence,
				AppliedFields = r.AppliedFields, RejectedCount = r.RejectedCount, RelatedCallId = r.RelatedCallId, PromptVersion = r.PromptVersion, ModelName = r.ModelName,
				ModelRevision = r.ModelRevision, RuntimeDigest = r.RuntimeDigest, InputTokens = r.InputTokens, OutputTokens = r.OutputTokens, LatencyMs = r.LatencyMs,
				CreatedOnUtc = DateTime.SpecifyKind(r.CreatedOnUtc, DateTimeKind.Utc),
				CompletedOnUtc = r.CompletedOnUtc.HasValue ? DateTime.SpecifyKind(r.CompletedOnUtc.Value, DateTimeKind.Utc) : null
			};
		}

		// Npgsql needs Unspecified-kind timestamps; SQL Server keeps the UTC value as is.
		private static object Stamp(AiDispatchAuditRow row) => new
		{
			row.AiDispatchAuditId, row.DepartmentId, row.CallId, row.Mode, row.Outcome, row.Confidence, row.AppliedFields, row.RejectedCount,
			row.RelatedCallId, row.PromptVersion, row.ModelName, row.ModelRevision, row.RuntimeDigest, row.InputTokens, row.OutputTokens, row.LatencyMs,
			CreatedOnUtc = DatabaseTimestamp(row.CreatedOnUtc),
			CompletedOnUtc = row.CompletedOnUtc.HasValue ? DatabaseTimestamp(row.CompletedOnUtc.Value) : (DateTime?)null
		};
	}
}

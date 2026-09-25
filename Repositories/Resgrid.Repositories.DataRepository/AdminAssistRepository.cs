using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Tenant-keyed metadata only. Workspace revision serializes competing setup and learning commands.</summary>
	public sealed partial class AdminAssistRepository : RmsRepositoryBase<AdminAssistWorkspaceRow>, IAdminAssistRepository, IAdminAssistMaintenanceStore, IAdminAssistTraceStore, IModuleImpactStore, IAdministrativeReferenceStore, IRetentionImpactStore, INotificationImpactStore, ISecurityImpactStore
	{
		public AdminAssistRepository(IConnectionProvider connections, SqlConfiguration configuration, IUnitOfWork unit,
			IQueryFactory queries) : base(connections, configuration, unit, queries) { }

		public async Task LockConfigurationAsync(int departmentId, CancellationToken ct)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Configuration audit requires a transaction.");
			var sql = IsPostgres
				? $"SELECT {Col("DepartmentId")} FROM {Tbl("Departments")} WHERE {Col("DepartmentId")}={P}DepartmentId FOR UPDATE"
				: $"SELECT {Col("DepartmentId")} FROM {Tbl("Departments")} WITH (UPDLOCK,HOLDLOCK) WHERE {Col("DepartmentId")}={P}DepartmentId";
			if (await ScalarAsync<int>(sql, new { DepartmentId = departmentId }, ct) != departmentId) throw new InvalidOperationException("Configuration department unavailable.");
		}

		public async Task<bool> TraceDepartmentExistsAsync(int departmentId, CancellationToken ct) =>
			await ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("Departments")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct) == 1;

		public async Task SaveTraceAsync(AdminAssistDispatchTraceRow row, CancellationToken ct) =>
			await MaintenanceTransactionAsync(row.DepartmentId, async () =>
			{
				var existingDepartment = await ScalarAsync<int?>($"SELECT {Col("DepartmentId")} FROM {Tbl("AdminAssistDispatchTraces")} WHERE {Col("AdminAssistDispatchTraceId")}={P}Id", new { Id = row.AdminAssistDispatchTraceId }, ct);
				if (existingDepartment.HasValue)
				{
					if (existingDepartment != row.DepartmentId) throw new UnauthorizedAccessException();
					return 0;
				}
				return await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistDispatchTraces")} ({Cols("AdminAssistDispatchTraceId", "DepartmentId", "CallId", "AttemptId", "Stage", "ResolverVersion", "OccurredOn", "Content", "IsProtected", "ProtectedCatalogVersion")}) " +
					$"VALUES ({P}AdminAssistDispatchTraceId,{P}DepartmentId,{P}CallId,{P}AttemptId,{P}Stage,{P}ResolverVersion,{P}OccurredOn,{P}Content,{P}IsProtected,{P}ProtectedCatalogVersion)",
					new { row.AdminAssistDispatchTraceId, row.DepartmentId, row.CallId, row.AttemptId, row.Stage, row.ResolverVersion,
						OccurredOn = DatabaseTimestamp(row.OccurredOn), row.Content, row.IsProtected, row.ProtectedCatalogVersion }, ct);
			}, ct);

		public async Task<long> AppendConfigurationChangeAsync(int departmentId, string actorId, string binding, string before, string after, string correlationId, CancellationToken ct)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Configuration audit requires a transaction.");
			var revision = await GetConfigurationRevisionAsync(departmentId, ct) + 1;
			var args = new { DepartmentId = departmentId, Revision = revision, ModifiedOn = DatabaseTimestamp(DateTime.UtcNow) };
			if (await ExecuteAsync($"UPDATE {Tbl("AdminAssistConfigurationRevisions")} SET {Col("Revision")}={P}Revision,{Col("ModifiedOn")}={P}ModifiedOn WHERE {Col("DepartmentId")}={P}DepartmentId", args, ct) == 0)
				await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistConfigurationRevisions")} ({Cols("DepartmentId", "Revision", "ModifiedOn")}) VALUES ({P}DepartmentId,{P}Revision,{P}ModifiedOn)", args, ct);
			await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistHistory")} ({Cols("AdminAssistHistoryId", "DepartmentId", "ActorId", "OccurredOnUtc", "Source", "Action", "SubjectId", "BeforeCode", "AfterCode", "Revision", "CorrelationId")}) " +
				$"VALUES ({P}Id,{P}DepartmentId,{P}ActorId,{P}OccurredOnUtc,{P}Source,{P}Action,{P}SubjectId,{P}BeforeCode,{P}AfterCode,{P}Revision,{P}CorrelationId)",
				new { Id = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, ActorId = actorId, OccurredOnUtc = args.ModifiedOn, Source = "Configuration", Action = "Changed", SubjectId = binding, BeforeCode = before, AfterCode = after, Revision = revision, CorrelationId = correlationId }, ct);
			return revision;
		}

		public Task<long> GetConfigurationRevisionAsync(int departmentId, CancellationToken ct) =>
			ScalarAsync<long>($"SELECT COALESCE(MAX({Col("Revision")}),0) FROM {Tbl("AdminAssistConfigurationRevisions")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct);

		public async Task<IReadOnlyList<AdminAssistFindingRow>> GetFindingsAsync(int departmentId, CancellationToken ct) =>
			(await QueryAsync<AdminAssistFindingRow>($"SELECT * FROM {Tbl("AdminAssistFindings")} WHERE {Col("DepartmentId")}={P}DepartmentId ORDER BY {Col("RuleId")}", new { DepartmentId = departmentId }, ct)).ToList();

		public async Task SaveFindingAsync(AdminAssistFindingRow row, long expectedRevision, CancellationToken ct)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Finding changes require a transaction.");
			var names = new[] { "AdminAssistFindingId", "DepartmentId", "RuleId", "SubjectId", "Episode", "Result", "Severity", "ReviewStatus", "OwnerId", "ReviewOn", "ExceptionUntil", "Content", "IsProtected", "ProtectedCatalogVersion", "SnapshotRevision", "Revision", "FirstObservedOn", "LastObservedOn" };
			var args = new { row.AdminAssistFindingId, row.DepartmentId, row.RuleId, row.SubjectId, row.Episode, row.Result, row.Severity, row.ReviewStatus, row.OwnerId,
				ReviewOn = row.ReviewOn.HasValue ? DatabaseTimestamp(row.ReviewOn.Value) : (DateTime?)null,
				ExceptionUntil = row.ExceptionUntil.HasValue ? DatabaseTimestamp(row.ExceptionUntil.Value) : (DateTime?)null,
				row.Content, row.IsProtected, row.ProtectedCatalogVersion, row.SnapshotRevision, row.Revision,
				FirstObservedOn = DatabaseTimestamp(row.FirstObservedOn), LastObservedOn = DatabaseTimestamp(row.LastObservedOn), ExpectedRevision = expectedRevision };
			if (expectedRevision == 0)
				await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistFindings")} ({Cols(names)}) VALUES ({string.Join(",", names.Select(n => P + n))})", args, ct);
			else if (await ExecuteAsync($"UPDATE {Tbl("AdminAssistFindings")} SET {string.Join(",", names.Skip(4).Select(n => Col(n) + "=" + P + n))} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("AdminAssistFindingId")}={P}AdminAssistFindingId AND {Col("Revision")}={P}ExpectedRevision", args, ct) != 1)
				throw new AdminAssistConcurrencyException();
		}

		public async Task SaveDailySummaryAsync(int departmentId, ConfigurationReport report, string catalogVersion, CancellationToken ct)
		{
			if (UnitOfWork.Transaction == null) throw new InvalidOperationException("Summary changes require a transaction.");
			var args = new { DepartmentId = departmentId, DayUtc = DatabaseTimestamp(report.Snapshot.AsOfUtc.Date), FailedCount = report.Failed, UnknownCount = report.Unknown, EvaluatedCount = report.Required - report.Unknown, CatalogVersion = catalogVersion };
			if (await ExecuteAsync($"UPDATE {Tbl("AdminAssistDailySummaries")} SET {Col("FailedCount")}={P}FailedCount,{Col("UnknownCount")}={P}UnknownCount,{Col("EvaluatedCount")}={P}EvaluatedCount,{Col("CatalogVersion")}={P}CatalogVersion WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("DayUtc")}={P}DayUtc", args, ct) == 0)
				await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistDailySummaries")} ({Cols("DepartmentId", "DayUtc", "FailedCount", "UnknownCount", "EvaluatedCount", "CatalogVersion")}) VALUES ({P}DepartmentId,{P}DayUtc,{P}FailedCount,{P}UnknownCount,{P}EvaluatedCount,{P}CatalogVersion)", args, ct);
		}

		public async Task<SetupWorkspace> GetWorkspaceAsync(int departmentId, string userId, string catalogVersion, CancellationToken ct)
		{
			var row = await ReadRowAsync(departmentId, ct);
			var state = SetupWorkspaceMetadata.Read(row?.AreasJson);
			var personal = await QueryAsync<LearningRow>($"SELECT {Cols("CapabilityId", "Learned", "Interested")} FROM {Tbl("AdminAssistLearning")} " +
				$"WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("CatalogVersion")}={P}CatalogVersion",
				new { DepartmentId = departmentId, UserId = userId, CatalogVersion = catalogVersion }, ct);
			return new SetupWorkspace(departmentId, row?.Revision ?? 0, (SetupMode)(row?.Mode ?? 0),
				state.Areas, personal.Where(p => p.Learned && p.CapabilityId != "setup-prompt").Select(p => p.CapabilityId).ToArray(),
				personal.Where(p => p.Interested && p.CapabilityId != "setup-prompt").Select(p => p.CapabilityId).ToArray(), catalogVersion,
				row?.ReviewedOn.HasValue == true ? DateTime.SpecifyKind(row.ReviewedOn.Value, DateTimeKind.Utc) : null,
				personal.Any(p => p.CapabilityId == "setup-prompt" && p.Learned), state.AreaReasons, state.ReviewEvidence, state.RevisitOnUtc, state.ScopeRevision);
		}

		public async Task<SetupWorkspace> UpdateWorkspaceAsync(AdminAssistActor actor, SetupProgressCommand command, CancellationToken ct)
		{
			var owns = UnitOfWork.Transaction == null;
			await UnitOfWork.CreateOrGetConnectionAsync(ct);
			try
			{
				await LockConfigurationAsync(actor.DepartmentId, ct);
				var now = DatabaseTimestamp(DateTime.UtcNow);
				var row = await ReadRowAsync(actor.DepartmentId, ct);
				if ((row?.Revision ?? 0) != command.ExpectedRevision) throw new AdminAssistConcurrencyException();
				var state = SetupWorkspaceMetadata.Read(row?.AreasJson);
				var areas = state.Areas;
				string before = null;
				string after = command.Choice;
				var mode = row?.Mode ?? 0;
				var reviewedOn = row?.ReviewedOn;
				switch (command.Operation)
				{
					case "area":
						before = areas.TryGetValue(command.TargetId, out var choice) ? choice.ToString() : null;
						if (state.AreaReasons.TryGetValue(command.TargetId, out var priorReason)) before += ":" + priorReason;
						areas[command.TargetId] = Enum.Parse<SetupAreaChoice>(command.Choice);
						if (areas[command.TargetId] == SetupAreaChoice.NotApplicable)
						{
							state.AreaReasons[command.TargetId] = Enum.Parse<SetupAreaReason>(command.ReasonCode);
							after += ":" + command.ReasonCode;
						}
						else state.AreaReasons.Remove(command.TargetId);
						if (before != after) state.ScopeRevision = checked(state.ScopeRevision + 1);
						break;
					case "mode": before = ((SetupMode)mode).ToString(); mode = (int)Enum.Parse<SetupMode>(command.Choice); break;
					case "review":
						if (command.ReviewEvidence == null || command.ReviewEvidence.ScopeRevision != state.ScopeRevision || (await GetConfigurationRevisionAsync(actor.DepartmentId, ct)).ToString(System.Globalization.CultureInfo.InvariantCulture) != command.ReviewEvidence.SnapshotRevision)
							throw new AdminAssistConcurrencyException();
						state.ReviewEvidence = command.ReviewEvidence;
						reviewedOn = now; after = "Reviewed:" + command.ReviewEvidence.SnapshotRevision; break;
					case "revisit":
						before = state.RevisitOnUtc?.ToString("O"); state.RevisitOnUtc = command.RevisitOnUtc; after = state.RevisitOnUtc?.ToString("O"); break;
					case "learn": case "interest": case "dismiss": break;
					default: throw new ArgumentException("Invalid setup operation.");
				}
				var revision = command.ExpectedRevision + 1;
				var args = new { actor.DepartmentId, Revision = revision, Mode = mode, AreasJson = state.Serialize(),
					command.CatalogVersion, ReviewedOn = reviewedOn, ModifiedOn = now, command.ExpectedRevision };
				if (row == null)
				{
					try
					{
						await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistWorkspaces")} ({Cols("DepartmentId", "Revision", "Mode", "AreasJson", "CatalogVersion", "ReviewedOn", "ModifiedOn")}) " +
							$"VALUES ({P}DepartmentId,{P}Revision,{P}Mode,{P}AreasJson,{P}CatalogVersion,{P}ReviewedOn,{P}ModifiedOn)", args, ct);
					}
					catch (Exception ex) when (IsUniqueViolation(ex)) { throw new AdminAssistConcurrencyException(); }
				}
				else if (await ExecuteAsync($"UPDATE {Tbl("AdminAssistWorkspaces")} SET {Col("Revision")}={P}Revision,{Col("Mode")}={P}Mode," +
					$"{Col("AreasJson")}={P}AreasJson,{Col("CatalogVersion")}={P}CatalogVersion,{Col("ReviewedOn")}={P}ReviewedOn,{Col("ModifiedOn")}={P}ModifiedOn " +
					$"WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("Revision")}={P}ExpectedRevision", args, ct) != 1)
					throw new AdminAssistConcurrencyException();

				if (command.Operation == "learn" || command.Operation == "interest" || command.Operation == "dismiss")
				{
					var personalArgs = new { actor.DepartmentId, actor.UserId, CapabilityId = command.Operation == "dismiss" ? "setup-prompt" : command.TargetId,
						command.CatalogVersion, ModifiedOn = now, Value = command.Choice == "true" };
					var where = $"{Col("DepartmentId")}={P}DepartmentId AND {Col("UserId")}={P}UserId AND {Col("CapabilityId")}={P}CapabilityId AND {Col("CatalogVersion")}={P}CatalogVersion";
					var personal = await QueryFirstOrDefaultAsync<LearningRow>($"SELECT {Cols("CapabilityId", "Learned", "Interested")} FROM {Tbl("AdminAssistLearning")} WHERE {where}", personalArgs, ct);
					before = (command.Operation != "interest" ? personal?.Learned : personal?.Interested)?.ToString().ToLowerInvariant();
					var column = command.Operation != "interest" ? "Learned" : "Interested";
					if (personal != null)
						await ExecuteAsync($"UPDATE {Tbl("AdminAssistLearning")} SET {Col(column)}={P}Value,{Col("ModifiedOn")}={P}ModifiedOn WHERE {where}", personalArgs, ct);
					else
						await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistLearning")} ({Cols("DepartmentId", "UserId", "CapabilityId", "CatalogVersion", "Learned", "Interested", "ModifiedOn")}) " +
							$"VALUES ({P}DepartmentId,{P}UserId,{P}CapabilityId,{P}CatalogVersion,{P}Learned,{P}Interested,{P}ModifiedOn)",
							new { actor.DepartmentId, actor.UserId, CapabilityId = command.Operation == "dismiss" ? "setup-prompt" : command.TargetId, command.CatalogVersion,
								Learned = command.Operation != "interest" && command.Choice == "true", Interested = command.Operation == "interest" && command.Choice == "true", ModifiedOn = now }, ct);
				}
				await ExecuteAsync($"INSERT INTO {Tbl("AdminAssistHistory")} ({Cols("AdminAssistHistoryId", "DepartmentId", "ActorId", "OccurredOnUtc", "Source", "Action", "SubjectId", "BeforeCode", "AfterCode", "Revision")}) " +
					$"VALUES ({P}Id,{P}DepartmentId,{P}ActorId,{P}OccurredOnUtc,{P}Source,{P}Action,{P}SubjectId,{P}BeforeCode,{P}AfterCode,{P}Revision)",
					new { Id = Guid.NewGuid().ToString(), actor.DepartmentId, ActorId = actor.UserId, OccurredOnUtc = now,
						Source = "Setup", Action = command.Operation, SubjectId = command.TargetId ?? "workspace", BeforeCode = before, AfterCode = after, Revision = revision }, ct);
				var result = await GetWorkspaceAsync(actor.DepartmentId, actor.UserId, command.CatalogVersion, ct);
				if (owns) UnitOfWork.CommitChanges();
				return result;
			}
			catch { if (owns) UnitOfWork.DiscardChanges(); throw; }
		}

		public async Task<IReadOnlyList<AdminAssistHistoryItem>> GetHistoryAsync(int departmentId, string actorId, int skip, int take, CancellationToken ct)
		{
			var rows = await QueryAsync<HistoryRow>($"SELECT * FROM {Tbl("AdminAssistHistory")} WHERE {Col("DepartmentId")}={P}DepartmentId " +
				$"AND ({Col("Action")} NOT IN ('learn','interest','dismiss') OR {Col("ActorId")}={P}ActorId) " +
				$"ORDER BY {Col("OccurredOnUtc")} DESC,{Col("AdminAssistHistoryId")} DESC {Paging()}",
				new { DepartmentId = departmentId, ActorId = actorId, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 1, 100) }, ct);
			return rows.Select(r => new AdminAssistHistoryItem(r.AdminAssistHistoryId, r.ActorId,
				DateTime.SpecifyKind(r.OccurredOnUtc, DateTimeKind.Utc), r.Source, r.Action, r.SubjectId, r.BeforeCode, r.AfterCode, r.Revision)).ToArray();
		}

		private Task<AdminAssistWorkspaceRow> ReadRowAsync(int departmentId, CancellationToken ct) =>
			QueryFirstOrDefaultAsync<AdminAssistWorkspaceRow>($"SELECT * FROM {Tbl("AdminAssistWorkspaces")} WHERE {Col("DepartmentId")}={P}DepartmentId", new { DepartmentId = departmentId }, ct);
		private sealed class LearningRow
		{
			public string CapabilityId { get; set; }
			public bool Learned { get; set; }
			public bool Interested { get; set; }
		}
		private sealed class HistoryRow
		{
			public string AdminAssistHistoryId { get; set; }
			public string ActorId { get; set; }
			public DateTime OccurredOnUtc { get; set; }
			public string Source { get; set; }
			public string Action { get; set; }
			public string SubjectId { get; set; }
			public string BeforeCode { get; set; }
			public string AfterCode { get; set; }
			public long Revision { get; set; }
		}
	}
}

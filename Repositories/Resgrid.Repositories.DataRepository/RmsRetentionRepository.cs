using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>One purge inventory for every revision, draft and copied artifact. Source systems retain their own records.</summary>
	public class RmsRetentionRepository : RmsRepositoryBase<RmsRevision>, IRmsRetentionRepository
	{
		public RmsRetentionRepository(IConnectionProvider connections, SqlConfiguration configuration, IUnitOfWork unitOfWork, IQueryFactory queries)
			: base(connections, configuration, unitOfWork, queries) { }

		public static readonly string[] ContentTables =
		{
			"RmsOperationalRecordDetails", "RmsRecordParticipants", "RmsRecordUnitResponses", "RmsRecordAttachments",
			"RmsExternalReferences", "RmsSourceFacts", "RmsUnitResponses", "RmsIncidentTypes", "RmsActionTactics", "RmsAids",
			"RmsLocations", "RmsNarratives", "RmsValidationIssues", "RmsCasualtyRescues", "RmsExposures", "RmsIncidentModules",
			"RmsIncidentResources", "RmsIncidentProperties", "RmsIncidentVehicles", "RmsEvidenceArtifacts"
		};

		public async Task<List<RmsSearchErasureTarget>> GetPendingSearchErasuresAsync(int take, RmsSearchErasureTarget after = null, CancellationToken cancellationToken = default)
		{
			string Pending(string table, string id, RmsRecordKind kind) =>
				$"SELECT {Col("DepartmentId")}, {(int)kind} AS {Col("RecordKind")}, {Col(id)} AS {Col("RecordId")}, {Col("PurgedOn")} FROM {Tbl(table)} WHERE {Col("PurgedOn")} IS NOT NULL AND {Col("SearchErasedOn")} IS NULL";
			var cursor = after == null ? "" : $"WHERE ({Col("DepartmentId")}>{P}AfterDepartment OR ({Col("DepartmentId")}={P}AfterDepartment AND ({Col("RecordKind")}>{P}AfterKind OR ({Col("RecordKind")}={P}AfterKind AND {Col("RecordId")}>{P}AfterId))))";
			var rows = (await QueryAsync<RmsSearchErasureTarget>(
				$"SELECT * FROM ({Pending("RmsOperationalRecords", "RmsOperationalRecordId", RmsRecordKind.Operational)} UNION ALL {Pending("RmsIncidentReports", "RmsIncidentReportId", RmsRecordKind.IncidentReport)}) pending {cursor} ORDER BY {Col("DepartmentId")}, {Col("RecordKind")}, {Col("RecordId")} {Paging()}",
				new { Skip = 0, Take = Math.Max(1, Math.Min(1000, take)), AfterDepartment = after?.DepartmentId, AfterKind = after?.RecordKind, AfterId = after?.RecordId }, cancellationToken)).ToList();
			foreach (var target in rows)
			{
				target.SourceIds.Add(target.RecordId);
				if (target.RecordKind == (int)RmsRecordKind.IncidentReport)
					target.SourceIds.AddRange(await QueryAsync<string>($"SELECT {Col("RmsIncidentAnalysisId")} FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IncidentReportId")}={P}RecordId", new { target.DepartmentId, target.RecordId }, cancellationToken));
			}
			return rows;
		}

		public async Task<bool> CompleteSearchErasureAsync(RmsSearchErasureTarget target, DateTime completedOn, CancellationToken cancellationToken = default)
		{
			if (target == null || target.RecordKind != (int)RmsRecordKind.Operational && target.RecordKind != (int)RmsRecordKind.IncidentReport) throw new ArgumentException("A purged parent report is required.");
			var operational = target.RecordKind == (int)RmsRecordKind.Operational;
			var table = operational ? "RmsOperationalRecords" : "RmsIncidentReports";
			var id = operational ? "RmsOperationalRecordId" : "RmsIncidentReportId";
			// PurgedOn is datetime2 on SQL Server. Binding it as datetime rounds the
			// database value on replay and can make an otherwise identical acknowledgement fail.
			var parameters = new DynamicParameters(new { target.DepartmentId, target.RecordId });
			parameters.Add("PurgedOn", target.PurgedOn, DbType.DateTime2);
			parameters.Add("CompletedOn", completedOn, DbType.DateTime2);
			return await ExecuteAsync($"UPDATE {Tbl(table)} SET {Col("SearchErasedOn")}={P}CompletedOn WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col(id)}={P}RecordId AND {Col("PurgedOn")}={P}PurgedOn AND {Col("SearchErasedOn")} IS NULL",
				parameters, cancellationToken) == 1;
		}

		public async Task<RmsPurgeResult> PurgeAsync(int departmentId, string recordId, RmsRecordKind kind, long expectedVersion, DateTime now, CancellationToken cancellationToken = default)
		{
			if (UnitOfWork.Transaction != null) throw new InvalidOperationException("Retention requires its own transaction.");
			UnitOfWork.CreateOrGetConnection();
			try
			{
				await LockRecordsDepartmentAsync(departmentId, cancellationToken);
				var result = await PurgeLockedAsync(departmentId, recordId, kind, expectedVersion, now, cancellationToken);
				UnitOfWork.CommitChanges();
				return result;
			}
			catch { UnitOfWork.DiscardChanges(); throw; }
		}

		private async Task<RmsPurgeResult> PurgeLockedAsync(int departmentId, string recordId, RmsRecordKind kind, long expectedVersion, DateTime now, CancellationToken cancellationToken)
		{
			var operational = kind == RmsRecordKind.Operational;
			if (!operational && kind != RmsRecordKind.IncidentReport) throw new ArgumentException("Retention is evaluated on the parent report.");
			var table = operational ? "RmsOperationalRecords" : "RmsIncidentReports";
			var idColumn = operational ? "RmsOperationalRecordId" : "RmsIncidentReportId";
			var key = new { DepartmentId = departmentId, Id = recordId };
			// The write lock also serializes draft/amendment/finalization writers that use the aggregate CAS.
			var locked = await ExecuteAsync($"UPDATE {Tbl(table)} SET {Col("RowVersion")} = {Col("RowVersion")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(idColumn)} = {P}Id AND {Col("RowVersion")} = {P}Version AND {Col("PurgedOn")} IS NULL",
				new { DepartmentId = departmentId, Id = recordId, Version = expectedVersion }, cancellationToken);
			if (locked != 1) return new RmsPurgeResult { Reason = "The record changed or was already purged." };
			var op = operational ? await QueryFirstOrDefaultAsync<RmsOperationalRecord>($"SELECT * FROM {Tbl(table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(idColumn)} = {P}Id", key, cancellationToken) : null;
			var incident = !operational ? await QueryFirstOrDefaultAsync<RmsIncidentReport>($"SELECT * FROM {Tbl(table)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(idColumn)} = {P}Id", key, cancellationToken) : null;
			var definition = op?.DefinitionKey ?? incident.DefinitionKey;
			var finalized = op?.FinalizedOn ?? incident?.FinalizedOn;
			var started = operational ? op.StartedOn ?? op.CreatedOn : incident.CallCreatedOn ?? incident.CreatedOn;
			var state = (RmsRecordState)(op?.State ?? incident.State);
			// Resumable upload bytes have a fixed 24-hour lifetime. Allow their expiration/cleanup before erasure.
			if ((op?.ModifiedOn ?? incident.ModifiedOn) > now.AddHours(-25)) return new RmsPurgeResult { Reason = "Recent record activity is still within the upload cleanup window." };
			if (!finalized.HasValue || (op?.AmendsRevisionId ?? incident?.AmendsRevisionId) != null ||
				!new[] { RmsRecordState.Finalized, RmsRecordState.Amended, RmsRecordState.Accepted, RmsRecordState.Voided, RmsRecordState.Cancelled }.Contains(state))
				return new RmsPurgeResult { Reason = "The report is still open." };
			var setting = await QueryFirstOrDefaultAsync<DepartmentSetting>($"SELECT * FROM {Tbl("DepartmentSettings")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SettingType")} = {(int)DepartmentSettingTypes.RecordsRetentionPolicy}", key, cancellationToken);
			var policy = string.IsNullOrEmpty(setting?.Setting) ? new RecordsRetentionPolicy() : ObjectSerialization.Deserialize<RecordsRetentionPolicy>(setting.Setting);
			if (policy == null) throw new InvalidOperationException("Retention policy is unreadable.");
			var years = policy.ResolveYears(definition, finalized.Value);
			if (years <= 0 || years > 9999 - finalized.Value.Year || finalized.Value.AddYears(years) > now) return new RmsPurgeResult { Reason = "Retention has not expired." };
			var analyses = operational ? new List<RmsIncidentAnalysis>() : (await QueryAsync<RmsIncidentAnalysis>($"SELECT * FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IncidentReportId")} = {P}Id", key, cancellationToken)).ToList();
			var ids = new[] { recordId }.Concat(analyses.Select(a => a.RmsIncidentAnalysisId)).ToArray();
			foreach (var analysis in analyses.OrderBy(a => a.RmsIncidentAnalysisId, StringComparer.Ordinal))
			{
				await ExecuteAsync($"UPDATE {Tbl("RmsIncidentAnalyses")} SET {Col("RowVersion")} = {Col("RowVersion")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentAnalysisId")} = {P}Id", new { DepartmentId = departmentId, Id = analysis.RmsIncidentAnalysisId }, cancellationToken);
				var current = await QueryFirstOrDefaultAsync<RmsIncidentAnalysis>($"SELECT * FROM {Tbl("RmsIncidentAnalyses")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RmsIncidentAnalysisId")} = {P}Id", new { DepartmentId = departmentId, Id = analysis.RmsIncidentAnalysisId }, cancellationToken);
				if (current.DeletedOn.HasValue) continue;
				var childYears = policy.ResolveYears(definition, current.FinalizedOn ?? current.CreatedOn);
				if (!current.FinalizedOn.HasValue || childYears <= 0 || childYears > 9999 - current.FinalizedOn.Value.Year || current.FinalizedOn.Value.AddYears(childYears) > now)
					return new RmsPurgeResult { Held = true, Reason = "The analysis is open or has an unexpired retention obligation." };
			}
			foreach (var permanentClass in new[] { "RmsCasualtyRescues", "RmsExposures" })
				if (await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl(permanentClass)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", key, cancellationToken) > 0)
					return new RmsPurgeResult { Held = true, Reason = "Casualty, rescue and exposure content retains permanently with its report." };
			var holds = (await QueryAsync<RmsRecordLegalHold>($"SELECT * FROM {Tbl("RmsRecordLegalHolds")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ReleasedOn")} IS NULL", key, cancellationToken)).ToList();
			if (holds.Any(h => h.Covers(recordId, definition, started) || analyses.Any(a => h.Covers(a.RmsIncidentAnalysisId, definition, started))))
				return new RmsPurgeResult { Held = true, Reason = "An active legal hold covers the report or its analysis." };
			var periodHolds = holds.Where(h => h.RecordId == null && (h.DefinitionKey == null || h.DefinitionKey == definition)).ToList();
			foreach (var id in ids)
				if (await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsRecordLegalHoldMembers")} m JOIN {Tbl("RmsRecordLegalHolds")} h ON h.{Col("DepartmentId")}=m.{Col("DepartmentId")} AND h.{Col("RmsRecordLegalHoldId")}=m.{Col("HoldId")} WHERE m.{Col("DepartmentId")}={P}DepartmentId AND m.{Col("RecordId")}={P}Id AND h.{Col("ReleasedOn")} IS NULL", new { DepartmentId = departmentId, Id = id }, cancellationToken) > 0)
					return new RmsPurgeResult { Held = true, Reason = "The record previously matched an active preservation hold; date changes cannot release it." };
			if (periodHolds.Count > 0)
				foreach (var id in ids)
				{
					var history = (await QueryAsync<RmsRevision>($"SELECT * FROM {Tbl("RmsRevisions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", new { DepartmentId = departmentId, Id = id }, cancellationToken)).ToList();
					if (history.Count == 0) return new RmsPurgeResult { Held = true, Reason = "Historical dates cannot be verified against an active preservation hold." };
					foreach (var revision in history)
					{
						try
						{
							var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(revision.SnapshotJson ?? ""))).ToLowerInvariant();
							if (checksum != revision.Checksum) return new RmsPurgeResult { Held = true, Reason = "Historical revision integrity cannot be verified against an active hold." };
							var snapshot = JObject.Parse(revision.SnapshotJson); var header = snapshot["Report"] as JObject ?? snapshot;
							var date = new[] { header["StartedOn"], header["CallCreatedOn"], header["CreatedOn"] }.FirstOrDefault(t => t != null && t.Type != JTokenType.Null);
							if (date == null || periodHolds.Any(h => h.Covers(id, definition, date.ToObject<DateTime>()))) return new RmsPurgeResult { Held = true, Reason = "An active legal hold covers a historical revision or its date is unknown." };
						}
						catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is InvalidCastException || ex is ArgumentException)
						{ return new RmsPurgeResult { Held = true, Reason = "Historical dates are unreadable; preservation cannot be released by retention." }; }
					}
				}
			// A produced copy is an independent retained public-records artifact. Never claim complete erasure while it exists.
			var productions = await QueryAsync<RmsDisclosureProduction>($"SELECT {Cols("ProducedSetJson")} FROM {Tbl("RmsDisclosureProductions")} WHERE {Col("DepartmentId")} = {P}DepartmentId", key, cancellationToken);
			foreach (var production in productions)
			{
				var manifest = JToken.Parse(production.ProducedSetJson ?? "[]");
				if (manifest.SelectTokens("$..RecordId").Concat(manifest.SelectTokens("$..recordId")).Concat(manifest.SelectTokens("$..record_id")).Any(t => ids.Contains(t.Value<string>())))
					return new RmsPurgeResult { Held = true, Reason = "An immutable disclosure production retains a copy of this report." };
			}
			var attachments = 0;
			foreach (var id in ids)
			{
				var parameters = new { DepartmentId = departmentId, Id = id, Now = now };
				if (await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("DomainEventOutbox")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AggregateId")} = {P}Id AND ({Col("DispatchedOn")} IS NULL OR {Col("LeaseExpiresOn")} > {P}Now)", parameters, cancellationToken) > 0
					|| await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("WorkflowRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AggregateId")} = {P}Id AND ({Col("CompletedOn")} IS NULL OR {Col("Status")} NOT IN ({(int)WorkflowRunStatus.Completed},{(int)WorkflowRunStatus.Failed},{(int)WorkflowRunStatus.Skipped}))", parameters, cancellationToken) > 0)
					return new RmsPurgeResult { Held = true, Reason = "An event delivery or workflow execution is still unresolved." };
				var evidence = await QueryAsync<RmsEvidenceArtifact>($"SELECT {Cols("RetentionYears", "CapturedOn", "StorageReference")} FROM {Tbl("RmsEvidenceArtifacts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				if (evidence.Any(e => e.RetentionYears.HasValue && (e.RetentionYears <= 0 || e.RetentionYears > 9999 - e.CapturedOn.Year || e.CapturedOn.AddYears(e.RetentionYears.Value) > now)))
					return new RmsPurgeResult { Held = true, Reason = "Supporting evidence has a longer retention obligation." };
				if (evidence.Any(e => !string.IsNullOrEmpty(e.StorageReference)) || await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsRecordAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id AND {Col("StorageReference")} IS NOT NULL", parameters, cancellationToken) > 0)
					throw new InvalidOperationException("External RMS storage requires a registered deletion provider before purge can complete.");
				if (await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsSubmissions")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id AND ({Col("LeaseExpiresOn")} > {P}Now OR {Col("RequiresReconciliation")} = {P}True OR {Col("CreatePendingReceipt")} = {P}True)", new { DepartmentId = departmentId, Id = id, Now = now, True = true }, cancellationToken) > 0)
					return new RmsPurgeResult { Held = true, Reason = "A destination delivery is still unresolved." };
				attachments += await ScalarAsync<int>($"SELECT COUNT(1) FROM {Tbl("RmsRecordAttachments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
			}
			// All guards have passed. No content mutation happens before the last child has been checked.
			foreach (var id in ids)
			{
				var parameters = new { DepartmentId = departmentId, Id = id, Now = now, Snapshot = "{}", True = true };
				foreach (var contentTable in ContentTables)
					await ExecuteAsync($"DELETE FROM {Tbl(contentTable)} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"DELETE FROM {Tbl("UdfFieldValues")} WHERE {Col("EntityType")}={(int)UdfEntityType.Record} AND {Col("EntityId")}={P}Id AND {Col("UdfDefinitionId")} IN (SELECT {Col("UdfDefinitionId")} FROM {Tbl("UdfDefinitions")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("EntityType")}={(int)UdfEntityType.Record})", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsSignatures")} SET {Col("SignerNameSnapshot")} = NULL, {Col("SignerRoleSnapshot")} = NULL, {Col("StatementText")} = NULL, {Col("IpAddress")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsRecordShares")} SET {Col("Reason")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("DomainEventOutbox")} SET {Col("PayloadJson")} = {P}Snapshot, {Col("LastError")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AggregateId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("WorkflowRunLogs")} SET {Col("RenderedOutput")} = NULL, {Col("ActionResult")} = NULL, {Col("ErrorMessage")} = NULL WHERE {Col("WorkflowRunId")} IN (SELECT {Col("WorkflowRunId")} FROM {Tbl("WorkflowRuns")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AggregateId")} = {P}Id)", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("WorkflowRuns")} SET {Col("InputPayload")} = {P}Snapshot, {Col("ErrorMessage")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("AggregateId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsRevisions")} SET {Col("SnapshotJson")} = {P}Snapshot, {Col("ReasonText")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsSubmissions")} SET {Col("PayloadJson")} = {P}Snapshot, {Col("ResponseJson")} = NULL, {Col("ErrorSummary")} = NULL, {Col("LeaseOwner")} = NULL, {Col("LeaseExpiresOn")} = NULL, {Col("CompletedOn")} = {P}Now, {Col("State")} = {(int)RmsSubmissionState.Superseded}, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsSubmissionExchanges")} SET {Col("OutcomeJson")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsAccessAudits")} SET {Col("DetailJson")} = NULL, {Col("Purpose")} = 'Pre-purge audit event', {Col("IpAddress")} = NULL WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("RecordId")} = {P}Id", parameters, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsRecordSearchProjections")} SET {Col("DisplaySummary")} = '[purged]', {Col("SearchText")} = NULL, {Col("DeletedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceId")} = {P}Id", parameters, cancellationToken);
			}
			if (operational)
				await ExecuteAsync($"UPDATE {Tbl(table)} SET {Col("DisplaySummary")} = '[purged]', {Col("ReturnReasonText")} = NULL, {Col("VoidReasonText")} = NULL, {Col("ExternalId")} = NULL, {Col("PurgedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(idColumn)} = {P}Id", new { DepartmentId = departmentId, Id = recordId, Now = now }, cancellationToken);
			else
			{
				var fields = new[] { "ReturnReasonText", "VoidReasonText", "RejectionSummary", "DispatchCenterId", "DeterminantCode", "DispatchIncidentCode", "Disposition", "PeoplePresent", "DisplacementCount", "AnimalsRescued", "SpecialModifiersCsv" };
				await ExecuteAsync($"UPDATE {Tbl(table)} SET {Col("DisplaySummary")} = '[purged]', {string.Join(", ", fields.Select(f => Col(f) + " = NULL"))}, {Col("PurgedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col(idColumn)} = {P}Id", new { DepartmentId = departmentId, Id = recordId, Now = now }, cancellationToken);
				await ExecuteAsync($"UPDATE {Tbl("RmsIncidentAnalyses")} SET {Col("GeneralCause")} = NULL, {Col("InvestigationTypesCsv")} = NULL, {Col("EstimatedLossTotal")} = NULL, {Col("EstimatedValueTotal")} = NULL, {Col("RejectionSummary")} = NULL, {Col("VoidReasonText")} = NULL, {Col("DeletedOn")} = {P}Now, {Col("ModifiedOn")} = {P}Now, {Col("RowVersion")} = {Col("RowVersion")} + 1 WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IncidentReportId")} = {P}Id", new { DepartmentId = departmentId, Id = recordId, Now = now }, cancellationToken);
			}
			await ExecuteAsync($"INSERT INTO {Tbl("RmsAccessAudits")} ({Cols("DepartmentId", "RecordId", "Action", "Purpose", "OriginClient", "Successful", "OccurredOn", "DetailJson")}) VALUES ({P}DepartmentId, {P}Id, {P}Action, {P}Purpose, {P}Origin, {P}Successful, {P}Now, {P}Detail)",
				new { DepartmentId = departmentId, Id = recordId, Action = (int)RmsAccessAuditAction.Change, Purpose = "Retention purge", Origin = (int)RmsOriginClient.System,
					Successful = true, Now = now, Detail = JsonConvert.SerializeObject(new { definition, years, finalizedOn = finalized, childRecordIds = ids, attachmentsPurged = attachments }) }, cancellationToken);
			return new RmsPurgeResult { Purged = true, SearchErasurePending = true, AttachmentsPurged = attachments, Reason = "RMS database content removed; committed search-storage erasure is pending." };
		}
	}
}

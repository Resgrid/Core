using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	/// <summary>Primary files of document-backed protocol versions (M0353). Metadata reads never select Data.</summary>
	public class ProtocolVersionFilesRepository : RmsRepositoryBase<ProtocolVersionFile>, IProtocolVersionFilesRepository
	{
		private static readonly string[] MetadataColumns =
		{
			"ProtocolVersionFileId", "DepartmentId", "DispatchProtocolId", "ProtocolVersionId", "FileName", "ContentType", "Size", "Sha256", "CreatedOn", "CreatedByUserId"
		};

		public ProtocolVersionFilesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ProtocolVersionFile> GetMetadataForVersionAsync(int departmentId, int protocolVersionId)
		{
			return QueryFirstOrDefaultAsync<ProtocolVersionFile>(
				$"SELECT {Cols(MetadataColumns)} FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolVersionId });
		}

		public async Task<List<ProtocolVersionFile>> GetMetadataForVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds)
		{
			var ids = InListValue((protocolVersionIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<ProtocolVersionFile>();

			return (await QueryAsync<ProtocolVersionFile>(
				$"SELECT {Cols(MetadataColumns)} FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("ProtocolVersionId", "Ids")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<ProtocolVersionFile> GetWithDataForVersionAsync(int departmentId, int protocolVersionId)
		{
			return QueryFirstOrDefaultAsync<ProtocolVersionFile>(
				$"SELECT * FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolVersionId });
		}

		public Task<ProtocolVersionFile> GetWithDataByIdAsync(int departmentId, int protocolVersionFileId)
		{
			return QueryFirstOrDefaultAsync<ProtocolVersionFile>(
				$"SELECT * FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionFileId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolVersionFileId });
		}

		public Task<int> DeleteForVersionAsync(int departmentId, int protocolVersionId, CancellationToken cancellationToken = default)
		{
			// Only a draft's file is ever replaced: the join refuses a version that left draft.
			return ExecuteAsync(
				$"DELETE FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}Id " +
				$"AND EXISTS (SELECT 1 FROM {Tbl("ProtocolVersions")} v WHERE v.{Col("ProtocolVersionId")} = {P}Id AND v.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND v.{Col("Status")} = {(int)ProtocolVersionStatuses.Draft})",
				new { DepartmentId = departmentId, Id = protocolVersionId }, cancellationToken);
		}

		public async Task<ProtocolVersionFile> CopyToVersionAsync(int departmentId, int fromVersionId, int toVersionId, int toProtocolId, string userId, DateTime utcNow,
			CancellationToken cancellationToken = default)
		{
			// Server-side copy: the bytes never travel through the application.
			var copied = await ExecuteAsync(
				$"INSERT INTO {Tbl("ProtocolVersionFiles")} ({Cols("DepartmentId", "DispatchProtocolId", "ProtocolVersionId", "FileName", "ContentType", "Size", "Sha256", "Data", "CreatedOn", "CreatedByUserId")}) " +
				$"SELECT {Col("DepartmentId")}, {P}ProtocolId, {P}ToId, {Col("FileName")}, {Col("ContentType")}, {Col("Size")}, {Col("Sha256")}, {Col("Data")}, {P}Now, {P}UserId " +
				$"FROM {Tbl("ProtocolVersionFiles")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}FromId",
				new { DepartmentId = departmentId, ProtocolId = toProtocolId, ToId = toVersionId, FromId = fromVersionId, Now = DatabaseTimestamp(utcNow), UserId = userId }, cancellationToken);

			return copied == 0 ? null : await GetMetadataForVersionAsync(departmentId, toVersionId);
		}
	}

	/// <summary>File text extractions and their pages (M0353).</summary>
	public class ProtocolFileTextsRepository : RmsRepositoryBase<ProtocolFileText>, IProtocolFileTextsRepository
	{
		/// <summary>Pages read per file for the search projection (its text is capped anyway).</summary>
		private const int SearchPagesPerFile = 60;

		public ProtocolFileTextsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ProtocolFileText> GetForSourceAsync(int departmentId, int sourceKind, int sourceId)
		{
			return QueryFirstOrDefaultAsync<ProtocolFileText>(
				$"SELECT * FROM {Tbl("ProtocolFileTexts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("SourceId")} = {P}SourceId",
				new { DepartmentId = departmentId, SourceKind = sourceKind, SourceId = sourceId });
		}

		public async Task<List<ProtocolFileText>> GetForProtocolAsync(int departmentId, int protocolId)
		{
			return (await QueryAsync<ProtocolFileText>(
				$"SELECT * FROM {Tbl("ProtocolFileTexts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId ORDER BY {Col("ProtocolFileTextId")}",
				new { DepartmentId = departmentId, ProtocolId = protocolId })).ToList();
		}

		public async Task<List<ProtocolFileTextPage>> GetPagesAsync(int departmentId, int protocolFileTextId)
		{
			return (await QueryAsync<ProtocolFileTextPage>(
				$"SELECT * FROM {Tbl("ProtocolFileTextPages")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFileTextId")} = {P}Id ORDER BY {Col("PageNumber")}",
				new { DepartmentId = departmentId, Id = protocolFileTextId })).ToList();
		}

		public async Task<string> GetSearchTextAsync(int departmentId, int protocolId, int? currentVersionId, int maxChars)
		{
			if (maxChars <= 0)
				return string.Empty;

			// The version in force's primary file, then the protocol's attachments: never a draft's file (R-P4).
			var texts = (await QueryAsync<ProtocolFileText>(
				$"SELECT * FROM {Tbl("ProtocolFileTexts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId " +
				$"AND {Col("Status")} = {(int)ProtocolFileTextStatuses.Extracted} " +
				$"AND (({Col("SourceKind")} = {(int)ProtocolFileSourceKinds.VersionFile} AND {Col("ProtocolVersionId")} = {P}VersionId) OR {Col("SourceKind")} = {(int)ProtocolFileSourceKinds.Attachment}) " +
				$"ORDER BY {Col("SourceKind")}, {Col("SourceId")}",
				new { DepartmentId = departmentId, ProtocolId = protocolId, VersionId = currentVersionId ?? 0 })).ToList();

			var builder = new StringBuilder();
			foreach (var text in texts)
			{
				if (builder.Length >= maxChars)
					break;

				var pages = await QueryAsync<ProtocolFileTextPage>(
					$"SELECT {Cols("PageNumber", "Text")} FROM {Tbl("ProtocolFileTextPages")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFileTextId")} = {P}Id " +
					$"AND {Col("PageNumber")} <= {SearchPagesPerFile} ORDER BY {Col("PageNumber")}",
					new { DepartmentId = departmentId, Id = text.ProtocolFileTextId });

				foreach (var page in pages)
				{
					if (builder.Length >= maxChars)
						break;
					if (string.IsNullOrWhiteSpace(page.Text))
						continue;
					if (builder.Length > 0)
						builder.Append(' ');
					builder.Append(page.Text.Length > maxChars - builder.Length ? page.Text.Substring(0, Math.Max(0, maxChars - builder.Length)) : page.Text);
				}
			}

			return builder.ToString();
		}

		public async Task SaveResultAsync(ProtocolFileText text, IReadOnlyList<string> pages, CancellationToken cancellationToken = default)
		{
			if (text == null || text.ProtocolFileTextId <= 0)
				return;

			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var key = new { DepartmentId = text.DepartmentId, Id = text.ProtocolFileTextId };
				await ExecuteAsync($"DELETE FROM {Tbl("ProtocolFileTextPages")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFileTextId")} = {P}Id", key, cancellationToken);

				var number = 0;
				foreach (var page in pages ?? Array.Empty<string>())
				{
					number++;
					var parameters = new DynamicParameters();
					parameters.Add("Id", text.ProtocolFileTextId);
					parameters.Add("DepartmentId", text.DepartmentId);
					parameters.Add("PageNumber", number);
					parameters.Add("Text", page ?? string.Empty, System.Data.DbType.String);
					await ExecuteAsync($"INSERT INTO {Tbl("ProtocolFileTextPages")} ({Cols("ProtocolFileTextId", "DepartmentId", "PageNumber", "Text")}) " +
						$"VALUES ({P}Id, {P}DepartmentId, {P}PageNumber, {P}Text)", parameters, cancellationToken);
				}

				var update = new DynamicParameters();
				update.Add("Id", text.ProtocolFileTextId);
				update.Add("DepartmentId", text.DepartmentId);
				update.Add("Status", text.Status);
				update.Add("Reason", text.Reason, System.Data.DbType.String);
				update.Add("PageCount", text.PageCount);
				update.Add("CharCount", text.CharCount);
				update.Add("Attempts", text.Attempts);
				update.Add("ExtractedOn", text.ExtractedOn.HasValue ? DatabaseTimestamp(text.ExtractedOn.Value) : (DateTime?)null, System.Data.DbType.DateTime2);
				await ExecuteAsync(
					$"UPDATE {Tbl("ProtocolFileTexts")} SET {Col("Status")} = {P}Status, {Col("Reason")} = {P}Reason, {Col("PageCount")} = {P}PageCount, " +
					$"{Col("CharCount")} = {P}CharCount, {Col("Attempts")} = {P}Attempts, {Col("ExtractedOn")} = {P}ExtractedOn " +
					$"WHERE {Col("ProtocolFileTextId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", update, cancellationToken);

				if (owns) UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}

		public async Task<List<ProtocolFileText>> GetPendingAsync(int take)
		{
			return (await QueryAsync<ProtocolFileText>(
				$"SELECT * FROM {Tbl("ProtocolFileTexts")} WHERE {Col("Status")} = {(int)ProtocolFileTextStatuses.Pending} " +
				$"ORDER BY {Col("QueuedOn")}, {Col("ProtocolFileTextId")} {Take()}",
				new { Take = Math.Max(1, take) })).ToList();
		}

		public async Task<List<ProtocolAttachmentTextCandidate>> GetAttachmentsWithoutTextAsync(int take)
		{
			var notDeleted = IsPostgres ? "false" : "0";
			var lowerName = IsPostgres ? $"LOWER(a.{Col("FileName")})" : $"LOWER(a.{Col("FileName")})";
			return (await QueryAsync<ProtocolAttachmentTextCandidate>(
				$"SELECT a.{Col("DispatchProtocolAttachmentId")} AS DispatchProtocolAttachmentId, a.{Col("DispatchProtocolId")} AS DispatchProtocolId, " +
				$"p.{Col("DepartmentId")} AS DepartmentId, a.{Col("FileName")} AS FileName, a.{Col("FileType")} AS FileType " +
				$"FROM {Tbl("DispatchProtocolAttachments")} a JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = a.{Col("DispatchProtocolId")} " +
				$"WHERE p.{Col("IsDeleted")} = {notDeleted} AND ({lowerName} LIKE '%.pdf' OR {lowerName} LIKE '%.docx') " +
				$"AND NOT EXISTS (SELECT 1 FROM {Tbl("ProtocolFileTexts")} t WHERE t.{Col("SourceKind")} = {(int)ProtocolFileSourceKinds.Attachment} " +
				$"AND t.{Col("SourceId")} = a.{Col("DispatchProtocolAttachmentId")}) " +
				$"ORDER BY a.{Col("DispatchProtocolAttachmentId")} {Take()}",
				new { Take = Math.Max(1, take) })).ToList();
		}

		public async Task<bool> TryClaimAsync(int protocolFileTextId, int expectedAttempts, CancellationToken cancellationToken = default)
		{
			return await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolFileTexts")} SET {Col("Attempts")} = {P}Next WHERE {Col("ProtocolFileTextId")} = {P}Id AND {Col("Attempts")} = {P}Expected " +
				$"AND {Col("Status")} = {(int)ProtocolFileTextStatuses.Pending}",
				new { Id = protocolFileTextId, Expected = expectedAttempts, Next = expectedAttempts + 1 }, cancellationToken) == 1;
		}

		public async Task CopyAsync(ProtocolFileText from, int toSourceId, int toVersionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (from == null)
				return;

			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var copy = await InsertAsync(new ProtocolFileText
				{
					DepartmentId = from.DepartmentId,
					DispatchProtocolId = from.DispatchProtocolId,
					SourceKind = from.SourceKind,
					SourceId = toSourceId,
					ProtocolVersionId = toVersionId,
					// A copy of a queued extraction stays queued: worker 86 extracts the copy from its own file.
					Status = from.Status,
					Reason = from.Reason,
					PageCount = from.PageCount,
					CharCount = from.CharCount,
					Attempts = 0,
					QueuedOn = utcNow,
					ExtractedOn = from.ExtractedOn
				}, cancellationToken, true);

				await ExecuteAsync(
					$"INSERT INTO {Tbl("ProtocolFileTextPages")} ({Cols("ProtocolFileTextId", "DepartmentId", "PageNumber", "Text")}) " +
					$"SELECT {P}ToId, {Col("DepartmentId")}, {Col("PageNumber")}, {Col("Text")} FROM {Tbl("ProtocolFileTextPages")} " +
					$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFileTextId")} = {P}FromId",
					new { ToId = copy.ProtocolFileTextId, DepartmentId = from.DepartmentId, FromId = from.ProtocolFileTextId }, cancellationToken);

				if (owns) UnitOfWork.CommitChanges();
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}

		public async Task<int> DeleteForSourceAsync(int departmentId, int sourceKind, int sourceId, CancellationToken cancellationToken = default)
		{
			var owns = UnitOfWork.Transaction == null;
			UnitOfWork.CreateOrGetConnection();
			try
			{
				var key = new { DepartmentId = departmentId, SourceKind = sourceKind, SourceId = sourceId };
				await ExecuteAsync(
					$"DELETE FROM {Tbl("ProtocolFileTextPages")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolFileTextId")} IN " +
					$"(SELECT t.{Col("ProtocolFileTextId")} FROM {Tbl("ProtocolFileTexts")} t WHERE t.{Col("DepartmentId")} = {P}DepartmentId " +
					$"AND t.{Col("SourceKind")} = {P}SourceKind AND t.{Col("SourceId")} = {P}SourceId)", key, cancellationToken);
				var removed = await ExecuteAsync(
					$"DELETE FROM {Tbl("ProtocolFileTexts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SourceKind")} = {P}SourceKind AND {Col("SourceId")} = {P}SourceId",
					key, cancellationToken);
				if (owns) UnitOfWork.CommitChanges();
				return removed;
			}
			catch
			{
				if (owns) UnitOfWork.DiscardChanges();
				throw;
			}
		}

		private static string Take() => IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";
	}

	/// <summary>Protocol acknowledgments (P2, M0354).</summary>
	public class ProtocolAcknowledgmentsRepository : RmsRepositoryBase<ProtocolAcknowledgment>, IProtocolAcknowledgmentsRepository
	{
		public ProtocolAcknowledgmentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ProtocolAcknowledgment> GetByIdForDepartmentAsync(int departmentId, int protocolAcknowledgmentId)
		{
			return QueryFirstOrDefaultAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolAcknowledgmentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = protocolAcknowledgmentId });
		}

		public Task<ProtocolAcknowledgment> GetForVersionAndUserAsync(int departmentId, int protocolVersionId, string userId)
		{
			return QueryFirstOrDefaultAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}VersionId " +
				$"AND {Col("UserId")} = {CaseInsensitive("UserId")}",
				new { DepartmentId = departmentId, VersionId = protocolVersionId, UserId = userId });
		}

		public async Task<List<ProtocolAcknowledgment>> GetForVersionAsync(int departmentId, int protocolVersionId)
		{
			return (await QueryAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolVersionId")} = {P}VersionId " +
				$"ORDER BY {Col("ProtocolAcknowledgmentId")}",
				new { DepartmentId = departmentId, VersionId = protocolVersionId })).ToList();
		}

		public async Task<List<ProtocolAcknowledgment>> GetForProtocolAsync(int departmentId, int protocolId)
		{
			return (await QueryAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId " +
				$"ORDER BY {Col("ProtocolAcknowledgmentId")}",
				new { DepartmentId = departmentId, ProtocolId = protocolId })).ToList();
		}

		public async Task<List<ProtocolAcknowledgment>> GetForUserAsync(int departmentId, string userId)
		{
			return (await QueryAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"ORDER BY {Col("ProtocolAcknowledgmentId")}",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		public async Task<List<ProtocolAcknowledgment>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ProtocolAcknowledgmentId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public async Task<bool> TryUpdateStatusAsync(ProtocolAcknowledgment acknowledgment, int expectedStatus, CancellationToken cancellationToken = default)
		{
			if (acknowledgment == null || acknowledgment.ProtocolAcknowledgmentId <= 0)
				return false;

			var parameters = new DynamicParameters();
			parameters.Add("Id", acknowledgment.ProtocolAcknowledgmentId);
			parameters.Add("DepartmentId", acknowledgment.DepartmentId);
			parameters.Add("Expected", expectedStatus);
			parameters.Add("Status", acknowledgment.Status);
			parameters.Add("Source", acknowledgment.Source);
			parameters.Add("DueOn", Stamp(acknowledgment.DueOn), System.Data.DbType.DateTime2);
			parameters.Add("EnteredAudienceOn", Stamp(acknowledgment.EnteredAudienceOn), System.Data.DbType.DateTime2);
			parameters.Add("OpenedOn", Stamp(acknowledgment.OpenedOn), System.Data.DbType.DateTime2);
			parameters.Add("SignedOn", Stamp(acknowledgment.SignedOn), System.Data.DbType.DateTime2);
			parameters.Add("SignatureRecordId", acknowledgment.SignatureRecordId, System.Data.DbType.Int32);
			parameters.Add("CompletedOn", Stamp(acknowledgment.CompletedOn), System.Data.DbType.DateTime2);
			parameters.Add("QuizTrainingId", acknowledgment.QuizTrainingId, System.Data.DbType.Int32);
			parameters.Add("WithdrawnOn", Stamp(acknowledgment.WithdrawnOn), System.Data.DbType.DateTime2);
			parameters.Add("ReminderStage", acknowledgment.ReminderStage);
			parameters.Add("BundleOrder", acknowledgment.BundleOrder);
			parameters.Add("ViaOffline", acknowledgment.ViaOffline);

			var set = string.Join(", ", new[]
			{
				"Status", "Source", "DueOn", "EnteredAudienceOn", "OpenedOn", "SignedOn", "SignatureRecordId", "CompletedOn", "QuizTrainingId", "WithdrawnOn",
				"ReminderStage", "BundleOrder", "ViaOffline"
			}.Select(c => $"{Col(c)} = {P}{c}"));

			return await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolAcknowledgments")} SET {set} WHERE {Col("ProtocolAcknowledgmentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId " +
				$"AND {Col("Status")} = {P}Expected",
				parameters, cancellationToken) == 1;
		}

		public Task<int> SupersedeOpenAsync(int departmentId, int protocolId, int exceptVersionId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("ProtocolAcknowledgments")} SET {Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Superseded}, {Col("WithdrawnOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId AND {Col("ProtocolVersionId")} <> {P}VersionId " +
				$"AND {Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Open}",
				new { DepartmentId = departmentId, ProtocolId = protocolId, VersionId = exceptVersionId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
		}

		public Task<int> WithdrawOpenForProtocolAsync(int departmentId, int protocolId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"UPDATE {Tbl("ProtocolAcknowledgments")} SET {Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Withdrawn}, {Col("WithdrawnOn")} = {P}Now " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("DispatchProtocolId")} = {P}ProtocolId AND {Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Open}",
				new { DepartmentId = departmentId, ProtocolId = protocolId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
		}

		public async Task<List<ProtocolAcknowledgmentDue>> GetRemindersDueAsync(DateTime utcNow, int take)
		{
			var notDeleted = IsPostgres ? "false" : "0";
			// The stage owed now, computed in SQL so only rows that owe a reminder come back (due work, never a sweep).
			var stage = $"CASE WHEN a.{Col("DueOn")} <= {P}Escalate THEN {(int)ProtocolAcknowledgmentReminderStages.Escalated} " +
				$"WHEN a.{Col("DueOn")} <= {P}Overdue THEN {(int)ProtocolAcknowledgmentReminderStages.Overdue} ELSE {(int)ProtocolAcknowledgmentReminderStages.Due} END";
			return (await QueryAsync<ProtocolAcknowledgmentDue>(
				$"SELECT a.{Col("ProtocolAcknowledgmentId")} AS ProtocolAcknowledgmentId, a.{Col("DepartmentId")} AS DepartmentId, a.{Col("DispatchProtocolId")} AS DispatchProtocolId, " +
				$"a.{Col("ProtocolVersionId")} AS ProtocolVersionId, a.{Col("UserId")} AS UserId, a.{Col("DueOn")} AS DueOn, a.{Col("ReminderStage")} AS ReminderStage " +
				$"FROM {Tbl("ProtocolAcknowledgments")} a JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = a.{Col("DispatchProtocolId")} " +
				$"WHERE a.{Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Open} AND a.{Col("DueOn")} IS NOT NULL AND a.{Col("DueOn")} <= {P}Now " +
				$"AND a.{Col("ReminderStage")} < ({stage}) AND p.{Col("IsDeleted")} = {notDeleted} AND p.{Col("Status")} = {(int)ProtocolStatuses.Published} " +
				$"ORDER BY a.{Col("DueOn")}, a.{Col("ProtocolAcknowledgmentId")} {Take()}",
				new
				{
					Now = DatabaseTimestamp(utcNow),
					Overdue = DatabaseTimestamp(utcNow.AddDays(-ProtocolAcknowledgmentRules.OverdueReminderDays)),
					Escalate = DatabaseTimestamp(utcNow.AddDays(-ProtocolAcknowledgmentRules.EscalationDays)),
					Take = Math.Max(1, take)
				})).ToList();
		}

		public async Task<bool> MarkReminderAsync(int departmentId, int protocolAcknowledgmentId, int stage, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			// The escalation time is set in C# (never an untyped null tested in SQL: pg-untyped-null-param-is-null).
			var escalated = stage >= (int)ProtocolAcknowledgmentReminderStages.Escalated ? $", {Col("EscalatedOn")} = {P}Now" : string.Empty;
			return await ExecuteAsync(
				$"UPDATE {Tbl("ProtocolAcknowledgments")} SET {Col("ReminderStage")} = {P}Stage, {Col("LastReminderOn")} = {P}Now{escalated} " +
				$"WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProtocolAcknowledgmentId")} = {P}Id AND {Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Open} " +
				$"AND {Col("ReminderStage")} < {P}Stage",
				new { DepartmentId = departmentId, Id = protocolAcknowledgmentId, Stage = stage, Now = DatabaseTimestamp(utcNow) }, cancellationToken) == 1;
		}

		public async Task<List<ProtocolAcknowledgment>> GetAwaitingQuizAsync(int take)
		{
			return (await QueryAsync<ProtocolAcknowledgment>(
				$"SELECT * FROM {Tbl("ProtocolAcknowledgments")} WHERE {Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.AwaitingQuiz} " +
				$"ORDER BY {Col("SignedOn")}, {Col("ProtocolAcknowledgmentId")} {Take()}",
				new { Take = Math.Max(1, take) })).ToList();
		}

		public async Task<List<ProtocolAcknowledgment>> GetOpenBulletinsForUserAsync(int departmentId, string userId)
		{
			var notDeleted = IsPostgres ? "false" : "0";
			return (await QueryAsync<ProtocolAcknowledgment>(
				$"SELECT a.* FROM {Tbl("ProtocolAcknowledgments")} a JOIN {Tbl("DispatchProtocols")} p ON p.{Col("DispatchProtocolId")} = a.{Col("DispatchProtocolId")} " +
				$"WHERE a.{Col("DepartmentId")} = {P}DepartmentId AND p.{Col("DepartmentId")} = {P}DepartmentId AND a.{Col("UserId")} = {CaseInsensitive("UserId")} " +
				$"AND a.{Col("Status")} = {(int)ProtocolAcknowledgmentStatuses.Open} AND p.{Col("Kind")} = {(int)ProtocolKinds.Bulletin} " +
				$"AND p.{Col("Status")} = {(int)ProtocolStatuses.Published} AND p.{Col("IsDeleted")} = {notDeleted} AND p.{Col("CurrentVersionId")} = a.{Col("ProtocolVersionId")} " +
				$"ORDER BY a.{Col("AssignedOn")} DESC",
				new { DepartmentId = departmentId, UserId = userId })).ToList();
		}

		private static DateTime? Stamp(DateTime? value) => value.HasValue ? DatabaseTimestamp(value.Value) : (DateTime?)null;

		private static string Take() => IsPostgres ? "LIMIT @Take" : "OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY";
	}
}

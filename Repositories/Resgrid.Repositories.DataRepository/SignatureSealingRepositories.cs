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
	/// <summary>Sealed documents (e-Signature S1, M0371). Department reads start at the department; worker reads are due-work queries.</summary>
	public class SealedDocumentsRepository : RmsRepositoryBase<SealedDocument>, ISealedDocumentsRepository
	{
		public SealedDocumentsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Max(1, take)}) ";
		private static string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;

		public Task<SealedDocument> GetByIdForDepartmentAsync(int departmentId, int sealedDocumentId)
		{
			return QueryFirstOrDefaultAsync<SealedDocument>(
				$"SELECT * FROM {Tbl("SealedDocuments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SealedDocumentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = sealedDocumentId });
		}

		public async Task<List<SealedDocument>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId, int role, string revisionSha256)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return new List<SealedDocument>();

			var revision = string.IsNullOrWhiteSpace(revisionSha256) ? string.Empty : $" AND {Col("RevisionSha256")} = {P}RevisionSha256";
			return (await QueryAsync<SealedDocument>(
				$"SELECT * FROM {Tbl("SealedDocuments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}SubjectKind " +
				$"AND {Col("SubjectId")} = {CaseInsensitive("SubjectId")} AND {Col("Role")} = {P}Role{revision} " +
				$"ORDER BY {Col("CreatedOn")} DESC, {Col("SealedDocumentId")} DESC",
				new { DepartmentId = departmentId, SubjectKind = subjectKind, SubjectId = subjectId, Role = role, RevisionSha256 = (revisionSha256 ?? string.Empty).ToLowerInvariant() })).ToList();
		}

		public async Task<List<SealedDocument>> GetBySha256Async(string sha256)
		{
			if (!SignaturePolicy.IsSha256(sha256))
				return new List<SealedDocument>();

			return (await QueryAsync<SealedDocument>(
				$"SELECT * FROM {Tbl("SealedDocuments")} WHERE {Col("Sha256")} = {P}Sha256 ORDER BY {Col("SealedDocumentId")} DESC",
				new { Sha256 = sha256.ToLowerInvariant() })).ToList();
		}

		public async Task<List<SealedDocument>> GetDuePendingAsync(DateTime utcNow, DateTime staleClaimBefore, int take)
		{
			return (await QueryAsync<SealedDocument>(
				$"SELECT {Top(take)}* FROM {Tbl("SealedDocuments")} WHERE {Col("Status")} = {(int)SealedDocumentStatuses.Pending} " +
				$"AND ({Col("NextAttemptOn")} IS NULL OR {Col("NextAttemptOn")} <= {P}Now) AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale) " +
				$"ORDER BY {Col("NextAttemptOn")}, {Col("SealedDocumentId")}{Limit(take)}",
				new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) })).ToList();
		}

		public async Task<List<SealedDocument>> GetDueRetimestampAsync(DateTime utcNow, DateTime staleClaimBefore, int take)
		{
			return (await QueryAsync<SealedDocument>(
				$"SELECT {Top(take)}* FROM {Tbl("SealedDocuments")} WHERE {Col("Status")} = {(int)SealedDocumentStatuses.Sealed} " +
				$"AND {Col("RetimestampDueOn")} IS NOT NULL AND {Col("RetimestampDueOn")} <= {P}Now AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale) " +
				$"ORDER BY {Col("RetimestampDueOn")}, {Col("SealedDocumentId")}{Limit(take)}",
				new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) })).ToList();
		}

		public async Task<bool> ClaimAsync(int sealedDocumentId, DateTime staleClaimBefore, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("SealedDocuments")} SET {Col("ClaimedOn")} = {P}Now WHERE {Col("SealedDocumentId")} = {P}Id " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale)",
				new { Id = sealedDocumentId, Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) }, cancellationToken);
			return updated == 1;
		}

		public Task ReleaseAsync(int sealedDocumentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"UPDATE {Tbl("SealedDocuments")} SET {Col("ClaimedOn")} = NULL WHERE {Col("SealedDocumentId")} = {P}Id",
				new { Id = sealedDocumentId }, cancellationToken);
		}
	}

	/// <summary>Pending seal inputs (S1, M0371): one row per pending document, read alone.</summary>
	public class SealedDocumentInputsRepository : RmsRepositoryBase<SealedDocumentInput>, ISealedDocumentInputsRepository
	{
		public SealedDocumentInputsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<SealedDocumentInput> GetForDocumentAsync(int departmentId, int sealedDocumentId)
		{
			return QueryFirstOrDefaultAsync<SealedDocumentInput>(
				$"SELECT * FROM {Tbl("SealedDocumentInputs")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SealedDocumentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = sealedDocumentId });
		}

		public Task<int> DeleteForDocumentAsync(int departmentId, int sealedDocumentId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("SealedDocumentInputs")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SealedDocumentId")} = {P}Id",
				new { DepartmentId = departmentId, Id = sealedDocumentId }, cancellationToken);
		}
	}

	/// <summary>Signature requests (S2, M0372).</summary>
	public class SignatureRequestsRepository : RmsRepositoryBase<SignatureRequestRecord>, ISignatureRequestsRepository
	{
		public SignatureRequestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Max(1, take)}) ";
		private static string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;
		private static string OpenStatuses => $"({(int)SignatureRequestStatuses.Draft}, {(int)SignatureRequestStatuses.Sent}, {(int)SignatureRequestStatuses.PartiallySigned})";

		public Task<SignatureRequestRecord> GetByIdForDepartmentAsync(int departmentId, int signatureRequestId)
		{
			return QueryFirstOrDefaultAsync<SignatureRequestRecord>(
				$"SELECT * FROM {Tbl("SignatureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRequestId")} = {P}Id",
				new { DepartmentId = departmentId, Id = signatureRequestId });
		}

		public async Task<List<SignatureRequestRecord>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return new List<SignatureRequestRecord>();

			return (await QueryAsync<SignatureRequestRecord>(
				$"SELECT * FROM {Tbl("SignatureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}SubjectKind " +
				$"AND {Col("SubjectId")} = {CaseInsensitive("SubjectId")} ORDER BY {Col("CreatedOn")} DESC, {Col("SignatureRequestId")} DESC",
				new { DepartmentId = departmentId, SubjectKind = subjectKind, SubjectId = subjectId })).ToList();
		}

		public Task<SignatureRequestRecord> GetByEnvelopeAsync(int providerKind, string envelopeId)
		{
			if (string.IsNullOrWhiteSpace(envelopeId))
				return Task.FromResult<SignatureRequestRecord>(null);

			// The envelope id is the provider's exact identifier (varchar on PostgreSQL), compared exactly.
			return QueryFirstOrDefaultAsync<SignatureRequestRecord>(
				$"SELECT * FROM {Tbl("SignatureRequests")} WHERE {Col("ProviderKind")} = {P}ProviderKind AND {Col("ProviderEnvelopeId")} = {P}EnvelopeId",
				new { ProviderKind = providerKind, EnvelopeId = envelopeId });
		}

		public async Task<List<SignatureRequestRecord>> GetDueReconcileAsync(DateTime utcNow, DateTime staleClaimBefore, int take)
		{
			return (await QueryAsync<SignatureRequestRecord>(
				$"SELECT {Top(take)}* FROM {Tbl("SignatureRequests")} WHERE {Col("Status")} IN {OpenStatuses} AND {Col("Channel")} = {(int)SignatureRequestChannels.Remote} " +
				$"AND {Col("ProviderEnvelopeId")} IS NOT NULL AND {Col("ReconcileAfter")} IS NOT NULL AND {Col("ReconcileAfter")} <= {P}Now " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale) ORDER BY {Col("ReconcileAfter")}, {Col("SignatureRequestId")}{Limit(take)}",
				new { Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) })).ToList();
		}

		public async Task<List<SignatureRequestRecord>> GetDueExpiryAsync(DateTime utcNow, int take)
		{
			return (await QueryAsync<SignatureRequestRecord>(
				$"SELECT {Top(take)}* FROM {Tbl("SignatureRequests")} WHERE {Col("Status")} IN {OpenStatuses} AND {Col("ExpiresOn")} <= {P}Now " +
				$"ORDER BY {Col("ExpiresOn")}, {Col("SignatureRequestId")}{Limit(take)}",
				new { Now = DatabaseTimestamp(utcNow) })).ToList();
		}

		public async Task<bool> ClaimAsync(int signatureRequestId, DateTime staleClaimBefore, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("SignatureRequests")} SET {Col("ClaimedOn")} = {P}Now WHERE {Col("SignatureRequestId")} = {P}Id " +
				$"AND ({Col("ClaimedOn")} IS NULL OR {Col("ClaimedOn")} < {P}Stale)",
				new { Id = signatureRequestId, Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) }, cancellationToken);
			return updated == 1;
		}

		public Task ReleaseAsync(int signatureRequestId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"UPDATE {Tbl("SignatureRequests")} SET {Col("ClaimedOn")} = NULL WHERE {Col("SignatureRequestId")} = {P}Id",
				new { Id = signatureRequestId }, cancellationToken);
		}

		public async Task<List<SignatureRequestRecord>> GetByIdsAsync(int departmentId, IEnumerable<int> signatureRequestIds)
		{
			var ids = InListValue((signatureRequestIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<SignatureRequestRecord>();

			return (await QueryAsync<SignatureRequestRecord>(
				$"SELECT * FROM {Tbl("SignatureRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("SignatureRequestId", "Ids")} " +
				$"ORDER BY {Col("SignatureRequestId")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}
	}

	/// <summary>Signers (S2, M0372). Token and session lookups compare exact SHA-256 hex.</summary>
	public class SignatureRequestSignersRepository : RmsRepositoryBase<SignatureRequestSigner>, ISignatureRequestSignersRepository
	{
		public SignatureRequestSignersRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<SignatureRequestSigner>> GetForRequestAsync(int departmentId, int signatureRequestId)
		{
			return (await QueryAsync<SignatureRequestSigner>(
				$"SELECT * FROM {Tbl("SignatureRequestSigners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRequestId")} = {P}Id " +
				$"ORDER BY {Col("SignerOrder")}, {Col("SignatureRequestSignerId")}",
				new { DepartmentId = departmentId, Id = signatureRequestId })).ToList();
		}

		public async Task<List<SignatureRequestSigner>> GetForRequestsAsync(int departmentId, IEnumerable<int> signatureRequestIds)
		{
			var ids = InListValue((signatureRequestIds ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct());
			if (ids.Length == 0)
				return new List<SignatureRequestSigner>();

			return (await QueryAsync<SignatureRequestSigner>(
				$"SELECT * FROM {Tbl("SignatureRequestSigners")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("SignatureRequestId", "Ids")} " +
				$"ORDER BY {Col("SignatureRequestId")}, {Col("SignerOrder")}, {Col("SignatureRequestSignerId")}",
				new { DepartmentId = departmentId, Ids = ids })).ToList();
		}

		public Task<SignatureRequestSigner> GetByLinkTokenHashAsync(string linkTokenHash)
		{
			if (!SignaturePolicy.IsSha256(linkTokenHash))
				return Task.FromResult<SignatureRequestSigner>(null);
			return QueryFirstOrDefaultAsync<SignatureRequestSigner>(
				$"SELECT * FROM {Tbl("SignatureRequestSigners")} WHERE {Col("LinkTokenHash")} = {P}Hash",
				new { Hash = linkTokenHash.ToLowerInvariant() });
		}

		public Task<SignatureRequestSigner> GetBySessionHashAsync(string sessionHash)
		{
			if (!SignaturePolicy.IsSha256(sessionHash))
				return Task.FromResult<SignatureRequestSigner>(null);
			return QueryFirstOrDefaultAsync<SignatureRequestSigner>(
				$"SELECT * FROM {Tbl("SignatureRequestSigners")} WHERE {Col("SessionHash")} = {P}Hash",
				new { Hash = sessionHash.ToLowerInvariant() });
		}

		public async Task<bool> ConsumeLinkTokenAsync(int signatureRequestSignerId, string linkTokenHash, string sessionHash, DateTime sessionExpiresOn, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			// One statement: two clicks on the same link race here and exactly one gets the session.
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("SignatureRequestSigners")} SET {Col("LinkTokenUsedOn")} = {P}Now, {Col("SessionHash")} = {P}Session, {Col("SessionExpiresOn")} = {P}SessionExpires, " +
				$"{Col("Status")} = CASE WHEN {Col("Status")} = {(int)SignatureSignerStatuses.Pending} THEN {(int)SignatureSignerStatuses.Opened} ELSE {Col("Status")} END " +
				$"WHERE {Col("SignatureRequestSignerId")} = {P}Id AND {Col("LinkTokenHash")} = {P}Hash AND {Col("LinkTokenUsedOn")} IS NULL " +
				$"AND {Col("LinkTokenExpiresOn")} IS NOT NULL AND {Col("LinkTokenExpiresOn")} > {P}Now",
				new
				{
					Id = signatureRequestSignerId,
					Hash = (linkTokenHash ?? string.Empty).ToLowerInvariant(),
					Session = (sessionHash ?? string.Empty).ToLowerInvariant(),
					SessionExpires = DatabaseTimestamp(sessionExpiresOn),
					Now = DatabaseTimestamp(utcNow)
				}, cancellationToken);
			return updated == 1;
		}

		private static string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Max(1, take)}) ";
		private static string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;
		private static string OpenRequestStatuses => $"({(int)SignatureRequestStatuses.Draft}, {(int)SignatureRequestStatuses.Sent}, {(int)SignatureRequestStatuses.PartiallySigned})";
		private static string UnsignedStatuses => $"({(int)SignatureSignerStatuses.Pending}, {(int)SignatureSignerStatuses.Opened}, {(int)SignatureSignerStatuses.Verified})";

		/// <summary>The signer's request is open and unexpired (an EXISTS on the request row; same department).</summary>
		private string OpenRequest(string alias) =>
			$"EXISTS (SELECT 1 FROM {Tbl("SignatureRequests")} r WHERE r.{Col("SignatureRequestId")} = {alias}.{Col("SignatureRequestId")} " +
			$"AND r.{Col("DepartmentId")} = {alias}.{Col("DepartmentId")} AND r.{Col("Status")} IN {OpenRequestStatuses} AND r.{Col("ExpiresOn")} > {P}Now)";

		public async Task<List<SignatureRequestSigner>> GetPendingMemberStepsAsync(int departmentId, string userId, DateTime utcNow, int take)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return new List<SignatureRequestSigner>();

			// User ids compare case-insensitively (citext on PostgreSQL, the default collation on SQL Server).
			return (await QueryAsync<SignatureRequestSigner>(
				$"SELECT {Top(take)}s.* FROM {Tbl("SignatureRequestSigners")} s WHERE s.{Col("DepartmentId")} = {P}DepartmentId " +
				$"AND s.{Col("UserId")} = {CaseInsensitive("UserId")} AND s.{Col("SignerKind")} = {(int)SignatureSignerKinds.Member} " +
				$"AND s.{Col("Status")} = {(int)SignatureSignerStatuses.Pending} AND {OpenRequest("s")} " +
				$"ORDER BY s.{Col("SignatureRequestId")} DESC, s.{Col("SignerOrder")}{Limit(take)}",
				new { DepartmentId = departmentId, UserId = userId.Trim(), Now = DatabaseTimestamp(utcNow) })).ToList();
		}

		public async Task<List<SignatureRequestSigner>> GetDueRemindersAsync(DateTime utcNow, int take)
		{
			return (await QueryAsync<SignatureRequestSigner>(
				$"SELECT {Top(take)}s.* FROM {Tbl("SignatureRequestSigners")} s WHERE s.{Col("NextReminderOn")} IS NOT NULL AND s.{Col("NextReminderOn")} <= {P}Now " +
				$"AND s.{Col("Status")} IN {UnsignedStatuses} AND {OpenRequest("s")} " +
				$"ORDER BY s.{Col("NextReminderOn")}, s.{Col("SignatureRequestSignerId")}{Limit(take)}",
				new { Now = DatabaseTimestamp(utcNow) })).ToList();
		}

		public async Task<bool> ClaimReminderAsync(int signatureRequestSignerId, int expectedReminderCount, DateTime? nextReminderOn, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("SignatureRequestSigners")} SET {Col("NextReminderOn")} = {P}Next, {Col("LastRemindedOn")} = {P}Now, {Col("ReminderCount")} = {Col("ReminderCount")} + 1 " +
				$"WHERE {Col("SignatureRequestSignerId")} = {P}Id AND {Col("ReminderCount")} = {P}Count AND {Col("NextReminderOn")} IS NOT NULL",
				new
				{
					Id = signatureRequestSignerId,
					Count = expectedReminderCount,
					Next = nextReminderOn.HasValue ? DatabaseTimestamp(nextReminderOn.Value) : (DateTime?)null,
					Now = DatabaseTimestamp(utcNow)
				}, cancellationToken);
			return updated == 1;
		}
	}

	/// <summary>In-person captures (S2, M0373).</summary>
	public class SignatureCapturesRepository : RmsRepositoryBase<SignatureCapture>, ISignatureCapturesRepository
	{
		public SignatureCapturesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<SignatureCapture>> GetForRequestAsync(int departmentId, int signatureRequestId)
		{
			return (await QueryAsync<SignatureCapture>(
				$"SELECT * FROM {Tbl("SignatureCaptures")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRequestId")} = {P}Id ORDER BY {Col("SignatureCaptureId")}",
				new { DepartmentId = departmentId, Id = signatureRequestId })).ToList();
		}
	}

	/// <summary>Department e-sign platform accounts (S2, M0374).</summary>
	public class ESignProviderAccountsRepository : RmsRepositoryBase<ESignProviderAccount>, IESignProviderAccountsRepository
	{
		public ESignProviderAccountsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<ESignProviderAccount> GetForDepartmentAsync(int departmentId, int providerKind)
		{
			return QueryFirstOrDefaultAsync<ESignProviderAccount>(
				$"SELECT * FROM {Tbl("ESignProviderAccounts")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ProviderKind")} = {P}ProviderKind",
				new { DepartmentId = departmentId, ProviderKind = providerKind });
		}
	}

	/// <summary>Webhook hints (S2, M0374): insert-if-absent by the provider event id, then a conditional claim per attempt.</summary>
	public class ESignWebhookEventsRepository : RmsRepositoryBase<ESignWebhookEvent>, IESignWebhookEventsRepository
	{
		private const string Table = "ESignWebhookEvents";

		private static readonly string[] InsertColumns =
		{
			"ESignWebhookEventId", "ProviderKind", "ProviderEventId", "EventType", "EnvelopeId", "State", "ReceivedOn", "Attempts"
		};

		public ESignWebhookEventsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Max(1, take)}) ";
		private static string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;

		public async Task<bool> TryInsertAsync(ESignWebhookEvent hint, CancellationToken cancellationToken = default)
		{
			var values = string.Join(", ", Array.ConvertAll(InsertColumns, c => P + c));

			// A unique violation would be logged as an error by RunAsync on every redelivery, so the collision is resolved in the
			// statement (inbound-email-receipts pattern): ON CONFLICT on PostgreSQL, a key-range lock on SQL Server.
			var sql = IsPostgres
				? $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) VALUES ({values}) ON CONFLICT ({Col("ESignWebhookEventId")}) DO NOTHING"
				: $"INSERT INTO {Tbl(Table)} ({Cols(InsertColumns)}) SELECT {values} " +
				  $"WHERE NOT EXISTS (SELECT 1 FROM {Tbl(Table)} WITH (UPDLOCK, HOLDLOCK) WHERE {Col("ESignWebhookEventId")} = {P}ESignWebhookEventId)";

			var inserted = await ExecuteAsync(sql, new
			{
				hint.ESignWebhookEventId,
				hint.ProviderKind,
				hint.ProviderEventId,
				hint.EventType,
				hint.EnvelopeId,
				hint.State,
				ReceivedOn = DatabaseTimestamp(hint.ReceivedOn),
				hint.Attempts
			}, cancellationToken);

			return inserted == 1;
		}

		public async Task<List<ESignWebhookEvent>> GetDueAsync(DateTime staleClaimBefore, int maxAttempts, int take)
		{
			return (await QueryAsync<ESignWebhookEvent>(
				$"SELECT {Top(take)}* FROM {Tbl(Table)} WHERE ({Col("State")} = {(int)ESignWebhookEventStates.Pending} " +
				$"OR ({Col("State")} = {(int)ESignWebhookEventStates.Processing} AND {Col("ClaimedOn")} < {P}Stale)) AND {Col("Attempts")} < {P}MaxAttempts " +
				$"ORDER BY {Col("ReceivedOn")}{Limit(take)}",
				new { Stale = DatabaseTimestamp(staleClaimBefore), MaxAttempts = maxAttempts })).ToList();
		}

		public async Task<bool> ClaimAsync(string id, DateTime staleClaimBefore, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {(int)ESignWebhookEventStates.Processing}, {Col("ClaimedOn")} = {P}Now, {Col("Attempts")} = {Col("Attempts")} + 1 " +
				$"WHERE {Col("ESignWebhookEventId")} = {P}Id AND ({Col("State")} = {(int)ESignWebhookEventStates.Pending} " +
				$"OR ({Col("State")} = {(int)ESignWebhookEventStates.Processing} AND {Col("ClaimedOn")} < {P}Stale))",
				new { Id = id, Now = DatabaseTimestamp(utcNow), Stale = DatabaseTimestamp(staleClaimBefore) }, cancellationToken);
			return updated == 1;
		}

		public Task MarkAsync(string id, ESignWebhookEventStates state, DateTime utcNow, string error, CancellationToken cancellationToken = default)
		{
			var processed = state == ESignWebhookEventStates.Pending ? "NULL" : P + "Now";
			return ExecuteAsync(
				$"UPDATE {Tbl(Table)} SET {Col("State")} = {P}State, {Col("ProcessedOn")} = {processed}, {Col("ClaimedOn")} = NULL, {Col("LastError")} = {P}Error " +
				$"WHERE {Col("ESignWebhookEventId")} = {P}Id",
				new { Id = id, State = (int)state, Now = DatabaseTimestamp(utcNow), Error = error == null ? null : (error.Length > 1000 ? error.Substring(0, 1000) : error) }, cancellationToken);
		}

		public Task<int> PurgeReceivedBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync($"DELETE FROM {Tbl(Table)} WHERE {Col("ReceivedOn")} < {P}Cutoff",
				new { Cutoff = DatabaseTimestamp(cutoff) }, cancellationToken);
		}
	}
}

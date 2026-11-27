using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Approvals;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Approval chains (e-Signature S4, M0385). Department reads start at the department.</summary>
	public class ApprovalChainsRepository : RmsRepositoryBase<ApprovalChain>, IApprovalChainsRepository
	{
		public ApprovalChainsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ApprovalChain>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ApprovalChain>(
				$"SELECT * FROM {Tbl("ApprovalChains")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("SubjectKind")}, {Col("ApprovalChainId")}",
				new { DepartmentId = departmentId })).ToList();
		}

		public Task<ApprovalChain> GetActiveForKindAsync(int departmentId, int subjectKind)
		{
			var top = IsPostgres ? string.Empty : "TOP (1) ";
			var limit = IsPostgres ? " LIMIT 1" : string.Empty;
			return QueryFirstOrDefaultAsync<ApprovalChain>(
				$"SELECT {top}* FROM {Tbl("ApprovalChains")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}SubjectKind " +
				$"AND {Col("IsActive")} = {P}Active ORDER BY {Col("ApprovalChainId")} DESC{limit}",
				new { DepartmentId = departmentId, SubjectKind = subjectKind, Active = true });
		}
	}

	public class ApprovalChainStepsRepository : RmsRepositoryBase<ApprovalChainStep>, IApprovalChainStepsRepository
	{
		public ApprovalChainStepsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ApprovalChainStep>> GetForChainAsync(int departmentId, int approvalChainId)
		{
			return (await QueryAsync<ApprovalChainStep>(
				$"SELECT * FROM {Tbl("ApprovalChainSteps")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ApprovalChainId")} = {P}ChainId " +
				$"ORDER BY {Col("StepOrder")}, {Col("ApprovalChainStepId")}",
				new { DepartmentId = departmentId, ChainId = approvalChainId })).ToList();
		}

		public Task<int> DeleteForChainAsync(int departmentId, int approvalChainId, CancellationToken cancellationToken = default)
		{
			return ExecuteAsync(
				$"DELETE FROM {Tbl("ApprovalChainSteps")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ApprovalChainId")} = {P}ChainId",
				new { DepartmentId = departmentId, ChainId = approvalChainId }, cancellationToken);
		}
	}

	public class ApprovalRequestsRepository : RmsRepositoryBase<ApprovalRequest>, IApprovalRequestsRepository
	{
		public ApprovalRequestsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Max(1, take)}) ";
		private static string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;

		public Task<ApprovalRequest> GetForDepartmentAsync(int departmentId, int approvalRequestId)
		{
			return QueryFirstOrDefaultAsync<ApprovalRequest>(
				$"SELECT * FROM {Tbl("ApprovalRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ApprovalRequestId")} = {P}Id",
				new { DepartmentId = departmentId, Id = approvalRequestId });
		}

		public async Task<List<ApprovalRequest>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(subjectId))
				return new List<ApprovalRequest>();
			return (await QueryAsync<ApprovalRequest>(
				$"SELECT * FROM {Tbl("ApprovalRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SubjectKind")} = {P}SubjectKind " +
				$"AND {Col("SubjectId")} = {CaseInsensitive("SubjectId")} ORDER BY {Col("ApprovalRequestId")} DESC",
				new { DepartmentId = departmentId, SubjectKind = subjectKind, SubjectId = subjectId })).ToList();
		}

		public Task<ApprovalRequest> GetBySignatureRequestAsync(int departmentId, int signatureRequestId)
		{
			return QueryFirstOrDefaultAsync<ApprovalRequest>(
				$"SELECT * FROM {Tbl("ApprovalRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("SignatureRequestId")} = {P}SignatureRequestId",
				new { DepartmentId = departmentId, SignatureRequestId = signatureRequestId });
		}

		public async Task<List<ApprovalRequest>> GetByStatusAsync(int departmentId, int status, int take)
		{
			return (await QueryAsync<ApprovalRequest>(
				$"SELECT {Top(take)}* FROM {Tbl("ApprovalRequests")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("Status")} = {P}Status " +
				$"ORDER BY {Col("ApprovalRequestId")} DESC{Limit(take)}",
				new { DepartmentId = departmentId, Status = status })).ToList();
		}

		public async Task<bool> ClaimStatusAsync(int approvalRequestId, int fromStatus, int toStatus, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("ApprovalRequests")} SET {Col("Status")} = {P}To, {Col("UpdatedOn")} = {P}Now " +
				$"WHERE {Col("ApprovalRequestId")} = {P}Id AND {Col("Status")} = {P}From",
				new { Id = approvalRequestId, From = fromStatus, To = toStatus, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
			return updated == 1;
		}

		public async Task<List<ApprovalRequest>> GetPendingWithClosedSignatureAsync(int take)
		{
			var open = $"({(int)SignatureRequestStatuses.Draft}, {(int)SignatureRequestStatuses.Sent}, {(int)SignatureRequestStatuses.PartiallySigned})";
			return (await QueryAsync<ApprovalRequest>(
				$"SELECT {Top(take)}a.* FROM {Tbl("ApprovalRequests")} a WHERE a.{Col("Status")} = {(int)ApprovalRequestStatuses.Pending} " +
				$"AND a.{Col("SignatureRequestId")} IS NOT NULL AND EXISTS (SELECT 1 FROM {Tbl("SignatureRequests")} r " +
				$"WHERE r.{Col("SignatureRequestId")} = a.{Col("SignatureRequestId")} AND r.{Col("DepartmentId")} = a.{Col("DepartmentId")} AND r.{Col("Status")} NOT IN {open}) " +
				$"ORDER BY a.{Col("ApprovalRequestId")}{Limit(take)}",
				new { })).ToList();
		}
	}

	public class ApprovalRequestStepsRepository : RmsRepositoryBase<ApprovalRequestStep>, IApprovalRequestStepsRepository
	{
		public ApprovalRequestStepsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string Top(int take) => IsPostgres ? string.Empty : $"TOP ({Math.Max(1, take)}) ";
		private static string Limit(int take) => IsPostgres ? $" LIMIT {Math.Max(1, take)}" : string.Empty;
		private static string Unsigned => $"({(int)SignatureSignerStatuses.Pending}, {(int)SignatureSignerStatuses.Opened}, {(int)SignatureSignerStatuses.Verified})";

		/// <summary>The step's request is pending (same department).</summary>
		private string PendingRequest(string alias) =>
			$"EXISTS (SELECT 1 FROM {Tbl("ApprovalRequests")} a WHERE a.{Col("ApprovalRequestId")} = {alias}.{Col("ApprovalRequestId")} " +
			$"AND a.{Col("DepartmentId")} = {alias}.{Col("DepartmentId")} AND a.{Col("Status")} = {(int)ApprovalRequestStatuses.Pending})";

		public async Task<List<ApprovalRequestStep>> GetForRequestAsync(int departmentId, int approvalRequestId)
		{
			return (await QueryAsync<ApprovalRequestStep>(
				$"SELECT * FROM {Tbl("ApprovalRequestSteps")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ApprovalRequestId")} = {P}Id " +
				$"ORDER BY {Col("StepOrder")}, {Col("ApprovalRequestStepId")}",
				new { DepartmentId = departmentId, Id = approvalRequestId })).ToList();
		}

		public async Task<List<ApprovalRequestStep>> GetUnannouncedTurnsAsync(int take)
		{
			return (await QueryAsync<ApprovalRequestStep>(
				$"SELECT {Top(take)}s.* FROM {Tbl("ApprovalRequestSteps")} s WHERE s.{Col("TurnNotifiedOn")} IS NULL AND {PendingRequest("s")} " +
				$"AND EXISTS (SELECT 1 FROM {Tbl("SignatureRequestSigners")} g WHERE g.{Col("SignatureRequestSignerId")} = s.{Col("SignatureRequestSignerId")} " +
				$"AND g.{Col("DepartmentId")} = s.{Col("DepartmentId")} AND g.{Col("TurnStartedOn")} IS NOT NULL AND g.{Col("Status")} IN {Unsigned}) " +
				$"ORDER BY s.{Col("ApprovalRequestStepId")}{Limit(take)}",
				new { })).ToList();
		}

		public async Task<List<ApprovalRequestStep>> GetDueEscalationsAsync(DateTime utcNow, int take)
		{
			return (await QueryAsync<ApprovalRequestStep>(
				$"SELECT {Top(take)}s.* FROM {Tbl("ApprovalRequestSteps")} s WHERE s.{Col("EscalateDueOn")} IS NOT NULL AND s.{Col("EscalateDueOn")} <= {P}Now " +
				$"AND s.{Col("EscalatedOn")} IS NULL AND {PendingRequest("s")} ORDER BY s.{Col("EscalateDueOn")}, s.{Col("ApprovalRequestStepId")}{Limit(take)}",
				new { Now = DatabaseTimestamp(utcNow) })).ToList();
		}

		public async Task<bool> ClaimAnnouncementAsync(int approvalRequestStepId, DateTime utcNow, DateTime? escalateDueOn, CancellationToken cancellationToken = default)
		{
			// The escalation time is part of the SQL shape (a null DateTime? parameter in IS NULL breaks PostgreSQL, 42P08).
			var due = escalateDueOn.HasValue ? $", {Col("EscalateDueOn")} = {P}Due" : string.Empty;
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("ApprovalRequestSteps")} SET {Col("TurnNotifiedOn")} = {P}Now{due} " +
				$"WHERE {Col("ApprovalRequestStepId")} = {P}Id AND {Col("TurnNotifiedOn")} IS NULL",
				escalateDueOn.HasValue
					? (object)new { Id = approvalRequestStepId, Now = DatabaseTimestamp(utcNow), Due = DatabaseTimestamp(escalateDueOn.Value) }
					: new { Id = approvalRequestStepId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
			return updated == 1;
		}

		public async Task<bool> ClaimEscalationAsync(int approvalRequestStepId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var updated = await ExecuteAsync(
				$"UPDATE {Tbl("ApprovalRequestSteps")} SET {Col("EscalatedOn")} = {P}Now WHERE {Col("ApprovalRequestStepId")} = {P}Id AND {Col("EscalatedOn")} IS NULL",
				new { Id = approvalRequestStepId, Now = DatabaseTimestamp(utcNow) }, cancellationToken);
			return updated == 1;
		}
	}

	public class ApprovalDelegatesRepository : RmsRepositoryBase<ApprovalDelegate>, IApprovalDelegatesRepository
	{
		public ApprovalDelegatesRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public async Task<List<ApprovalDelegate>> GetForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<ApprovalDelegate>(
				$"SELECT * FROM {Tbl("ApprovalDelegates")} WHERE {Col("DepartmentId")} = {P}DepartmentId ORDER BY {Col("ApprovalDelegateId")}",
				new { DepartmentId = departmentId })).ToList();
		}
	}

	public class MutualAidAgreementsRepository : RmsRepositoryBase<MutualAidAgreement>, IMutualAidAgreementsRepository
	{
		public MutualAidAgreementsRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<MutualAidAgreement> GetForDepartmentAsync(int departmentId, int mutualAidAgreementId)
		{
			return QueryFirstOrDefaultAsync<MutualAidAgreement>(
				$"SELECT * FROM {Tbl("MutualAidAgreements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("MutualAidAgreementId")} = {P}Id AND {Col("IsDeleted")} = {P}Deleted",
				new { DepartmentId = departmentId, Id = mutualAidAgreementId, Deleted = false });
		}

		public async Task<List<MutualAidAgreement>> GetAllForDepartmentAsync(int departmentId)
		{
			return (await QueryAsync<MutualAidAgreement>(
				$"SELECT * FROM {Tbl("MutualAidAgreements")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {P}Deleted ORDER BY {Col("MutualAidAgreementId")} DESC",
				new { DepartmentId = departmentId, Deleted = false })).ToList();
		}

		public async Task<List<MutualAidAgreement>> GetAwaitingCountersignatureAsync(int partnerDepartmentId)
		{
			return (await QueryAsync<MutualAidAgreement>(
				$"SELECT * FROM {Tbl("MutualAidAgreements")} WHERE {Col("PartnerDepartmentId")} = {P}PartnerId AND {Col("CountersignStatus")} = {P}Requested " +
				$"AND {Col("IsDeleted")} = {P}Deleted AND {Col("DepartmentId")} <> {P}PartnerId ORDER BY {Col("CountersignRequestedOn")} DESC, {Col("MutualAidAgreementId")} DESC",
				new { PartnerId = partnerDepartmentId, Requested = (int)MutualAidCountersignStatuses.Requested, Deleted = false })).ToList();
		}

		public async Task<List<MutualAidAgreement>> GetAllCountersignRequestedAsync(int take)
		{
			var top = Math.Max(1, Math.Min(take, 10000));
			var sql = IsPostgres
				? $"SELECT * FROM {Tbl("MutualAidAgreements")} WHERE {Col("CountersignStatus")} = {P}Requested AND {Col("IsDeleted")} = {P}Deleted ORDER BY {Col("MutualAidAgreementId")} LIMIT {top}"
				: $"SELECT TOP ({top}) * FROM {Tbl("MutualAidAgreements")} WHERE {Col("CountersignStatus")} = {P}Requested AND {Col("IsDeleted")} = {P}Deleted ORDER BY {Col("MutualAidAgreementId")}";
			return (await QueryAsync<MutualAidAgreement>(sql, new { Requested = (int)MutualAidCountersignStatuses.Requested, Deleted = false })).ToList();
		}

		public async Task<List<MutualAidAgreement>> GetCountersignRequestedForLinkAsync(int departmentLinkId)
		{
			return (await QueryAsync<MutualAidAgreement>(
				$"SELECT * FROM {Tbl("MutualAidAgreements")} WHERE {Col("DepartmentLinkId")} = {P}LinkId AND {Col("CountersignStatus")} = {P}Requested AND {Col("IsDeleted")} = {P}Deleted",
				new { LinkId = departmentLinkId, Requested = (int)MutualAidCountersignStatuses.Requested, Deleted = false })).ToList();
		}
	}
}

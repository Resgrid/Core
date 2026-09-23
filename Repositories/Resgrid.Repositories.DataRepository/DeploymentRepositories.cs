using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository
{
	// Workforce & Business Operations plan, Phase C (C3): deployment core repositories (registry M0218).

	public class DeploymentRepository : RmsRepositoryBase<Deployment>, IDeploymentRepository
	{
		public DeploymentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";
		private string OpenStatuses => $"{Col("Status")} IN (0, 1, 2, 3)";

		public Task<Deployment> GetByIdForDepartmentAsync(string deploymentId, int departmentId) =>
			QueryFirstOrDefaultAsync<Deployment>($"SELECT * FROM {Tbl("Deployments")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = deploymentId, DepartmentId = departmentId });

		public Task<Deployment> GetByCallIdAsync(int callId, int departmentId) =>
			QueryFirstOrDefaultAsync<Deployment>($"SELECT * FROM {Tbl("Deployments")} WHERE {Col("CallId")} = {P}CallId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}", new { CallId = callId, DepartmentId = departmentId });

		public Task<Deployment> GetByExternalOrderIdAsync(string rmsExternalOrderId, int departmentId) =>
			QueryFirstOrDefaultAsync<Deployment>($"SELECT * FROM {Tbl("Deployments")} WHERE {Col("RmsExternalOrderId")} = {P}OrderId AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC", new { OrderId = rmsExternalOrderId, DepartmentId = departmentId });

		public Task<IEnumerable<Deployment>> GetForDepartmentAsync(int departmentId, bool openOnly, int skip, int take) =>
			QueryAsync<Deployment>(
				$"SELECT * FROM {Tbl("Deployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" + (openOnly ? $" AND {OpenStatuses}" : string.Empty) +
				$" ORDER BY {Col("AddedOn")} DESC {Paging()}",
				new { DepartmentId = departmentId, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 1, 500) });

		public Task<IEnumerable<Deployment>> GetByIdsAsync(int departmentId, IEnumerable<string> deploymentIds) =>
			QueryAsync<Deployment>($"SELECT * FROM {Tbl("Deployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("DeploymentId", "Ids")} AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC", new { DepartmentId = departmentId, Ids = InListValue(deploymentIds) });

		public Task<int> CountForDepartmentAsync(int departmentId, bool openOnly) =>
			ScalarAsync<int>($"SELECT COUNT(*) FROM {Tbl("Deployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False}" + (openOnly ? $" AND {OpenStatuses}" : string.Empty), new { DepartmentId = departmentId });

		public Task<IEnumerable<Deployment>> GetByContractAsync(int departmentId, string serviceContractId) =>
			QueryAsync<Deployment>(
				$"SELECT * FROM {Tbl("Deployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("ServiceContractId")} = {P}ContractId AND {Col("IsDeleted")} = {False} ORDER BY {Col("AddedOn")} DESC {Paging()}",
				new { DepartmentId = departmentId, ContractId = serviceContractId, Skip = 0, Take = 500 });

		public Task<IEnumerable<Deployment>> GetCostRecoveryReleasedBeforeAsync(int departmentId, DateTime releasedOnOrBeforeUtc) =>
			QueryAsync<Deployment>(
				$"SELECT * FROM {Tbl("Deployments")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("FinanceMode")} = {P}CostRecovery " +
				$"AND {Col("Status")} IN ({P}Demobilizing, {P}Completed) AND COALESCE({Col("StatusChangedOn")}, {Col("EndOn")}, {Col("AddedOn")}) <= {P}ReleasedBy ORDER BY {Col("AddedOn")} DESC",
				new { DepartmentId = departmentId, CostRecovery = (int)DeploymentFinanceModes.CostRecovery, Demobilizing = (int)DeploymentStatuses.Demobilizing, Completed = (int)DeploymentStatuses.Completed, ReleasedBy = DatabaseTimestamp(releasedOnOrBeforeUtc) });
	}

	public class DeploymentUnitRepository : RmsRepositoryBase<DeploymentUnit>, IDeploymentUnitRepository
	{
		public DeploymentUnitRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DeploymentUnit>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentUnit>($"SELECT * FROM {Tbl("DeploymentUnits")} WHERE {Col("DeploymentId")} = {P}Id ORDER BY {Col("AddedOn")}", new { Id = deploymentId });

		public Task<IEnumerable<DeploymentUnit>> GetActiveAssignmentsForUnitsAsync(int departmentId, IEnumerable<int> unitIds, DateTime windowStart, DateTime windowEnd, string excludingDeploymentId) =>
			QueryAsync<DeploymentUnit>(
				$"SELECT u.* FROM {Tbl("DeploymentUnits")} u JOIN {Tbl("Deployments")} d ON d.{Col("DeploymentId")} = u.{Col("DeploymentId")} " +
				$"WHERE u.{Col("DepartmentId")} = {P}DepartmentId AND {InList("UnitId", "UnitIds", "u")} AND u.{Col("RemovedOn")} IS NULL AND d.{Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} AND d.{Col("Status")} IN (0, 1, 2, 3) " +
				$"AND (d.{Col("StartOn")} IS NULL OR d.{Col("StartOn")} <= {P}WindowEnd) AND (d.{Col("EndOn")} IS NULL OR d.{Col("EndOn")} >= {P}WindowStart) AND d.{Col("DeploymentId")} <> {P}Excluding",
				new { DepartmentId = departmentId, UnitIds = InListValue(unitIds), WindowStart = DatabaseTimestamp(windowStart), WindowEnd = DatabaseTimestamp(windowEnd), Excluding = excludingDeploymentId ?? string.Empty });

		public Task<IEnumerable<DeploymentUnit>> GetForUnitsAsync(int departmentId, IEnumerable<int> unitIds) =>
			QueryAsync<DeploymentUnit>($"SELECT * FROM {Tbl("DeploymentUnits")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {InList("UnitId", "UnitIds")} ORDER BY {Col("AddedOn")}",
				new { DepartmentId = departmentId, UnitIds = InListValue(unitIds) });
	}

	public class DeploymentPersonnelRepository : RmsRepositoryBase<DeploymentPersonnel>, IDeploymentPersonnelRepository
	{
		public DeploymentPersonnelRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DeploymentPersonnel>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentPersonnel>($"SELECT * FROM {Tbl("DeploymentPersonnel")} WHERE {Col("DeploymentId")} = {P}Id ORDER BY {Col("AddedOn")}", new { Id = deploymentId });

		public Task<IEnumerable<DeploymentPersonnel>> GetActiveAssignmentsForUsersAsync(int departmentId, IEnumerable<string> userIds, DateTime windowStart, DateTime windowEnd, string excludingDeploymentId) =>
			QueryAsync<DeploymentPersonnel>(
				$"SELECT p.* FROM {Tbl("DeploymentPersonnel")} p JOIN {Tbl("Deployments")} d ON d.{Col("DeploymentId")} = p.{Col("DeploymentId")} " +
				$"WHERE p.{Col("DepartmentId")} = {P}DepartmentId AND {InList("UserId", "UserIds", "p")} AND p.{Col("RemovedOn")} IS NULL AND d.{Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} AND d.{Col("Status")} IN (0, 1, 2, 3) " +
				$"AND (d.{Col("StartOn")} IS NULL OR d.{Col("StartOn")} <= {P}WindowEnd) AND (d.{Col("EndOn")} IS NULL OR d.{Col("EndOn")} >= {P}WindowStart) AND d.{Col("DeploymentId")} <> {P}Excluding",
				new { DepartmentId = departmentId, UserIds = InListValue(userIds), WindowStart = DatabaseTimestamp(windowStart), WindowEnd = DatabaseTimestamp(windowEnd), Excluding = excludingDeploymentId ?? string.Empty });

		public Task<IEnumerable<DeploymentPersonnel>> GetForUserAsync(int departmentId, string userId) =>
			QueryAsync<DeploymentPersonnel>($"SELECT * FROM {Tbl("DeploymentPersonnel")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("UserId")} = {P}UserId ORDER BY {Col("AddedOn")} DESC", new { DepartmentId = departmentId, UserId = userId });
	}

	public class DeploymentEquipmentRepository : RmsRepositoryBase<DeploymentEquipment>, IDeploymentEquipmentRepository
	{
		public DeploymentEquipmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DeploymentEquipment>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentEquipment>($"SELECT * FROM {Tbl("DeploymentEquipment")} WHERE {Col("DeploymentId")} = {P}Id ORDER BY {Col("AddedOn")}", new { Id = deploymentId });
	}

	public class DeploymentTimeReportRepository : RmsRepositoryBase<DeploymentTimeReport>, IDeploymentTimeReportRepository
	{
		public DeploymentTimeReportRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";

		public Task<DeploymentTimeReport> GetByIdForDepartmentAsync(string deploymentTimeReportId, int departmentId) =>
			QueryFirstOrDefaultAsync<DeploymentTimeReport>($"SELECT * FROM {Tbl("DeploymentTimeReports")} WHERE {Col("DeploymentTimeReportId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = deploymentTimeReportId, DepartmentId = departmentId });

		public Task<IEnumerable<DeploymentTimeReport>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentTimeReport>($"SELECT * FROM {Tbl("DeploymentTimeReports")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("IsDeleted")} = {False} ORDER BY {Col("ReportDate")}, {Col("ReportNumber")}", new { Id = deploymentId });

		public Task<DeploymentTimeReport> GetByDeploymentAndDateAsync(string deploymentId, DateTime reportDate) =>
			QueryFirstOrDefaultAsync<DeploymentTimeReport>(
				$"SELECT * FROM {Tbl("DeploymentTimeReports")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("ReportDate")} = {P}ReportDate AND {Col("IsDeleted")} = {False} AND {Col("Status")} <> {P}Void ORDER BY {Col("ReportNumber")}",
				new { Id = deploymentId, ReportDate = DatabaseTimestamp(reportDate.Date), Void = (int)DeploymentTimeReportStatuses.Void });

		public Task<IEnumerable<DeploymentTimeReport>> GetUnbilledApprovedAsync(int departmentId, string deploymentId = null) =>
			QueryAsync<DeploymentTimeReport>(
				$"SELECT * FROM {Tbl("DeploymentTimeReports")} WHERE {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {False} AND {Col("Status")} = {P}Approved AND {Col("InvoiceId")} IS NULL" +
				(deploymentId != null ? $" AND {Col("DeploymentId")} = {P}DeploymentId" : string.Empty) + $" ORDER BY {Col("DeploymentId")}, {Col("ReportDate")}",
				new { DepartmentId = departmentId, Approved = (int)DeploymentTimeReportStatuses.Approved, DeploymentId = deploymentId ?? string.Empty });

		public Task<IEnumerable<DeploymentTimeReport>> GetUnbilledApprovedBeforeAsync(DateTime approvedBeforeUtc) =>
			QueryAsync<DeploymentTimeReport>(
				$"SELECT * FROM {Tbl("DeploymentTimeReports")} WHERE {Col("IsDeleted")} = {False} AND {Col("Status")} = {P}Approved AND {Col("InvoiceId")} IS NULL AND {Col("ApprovedOn")} IS NOT NULL AND {Col("ApprovedOn")} <= {P}Before ORDER BY {Col("DepartmentId")}, {Col("DeploymentId")}, {Col("ReportDate")}",
				new { Approved = (int)DeploymentTimeReportStatuses.Approved, Before = DatabaseTimestamp(approvedBeforeUtc) });
	}

	public class DeploymentTimeEntryRepository : RmsRepositoryBase<DeploymentTimeEntry>, IDeploymentTimeEntryRepository
	{
		public DeploymentTimeEntryRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DeploymentTimeEntry>> GetByReportAsync(string deploymentTimeReportId) =>
			QueryAsync<DeploymentTimeEntry>($"SELECT * FROM {Tbl("DeploymentTimeEntries")} WHERE {Col("DeploymentTimeReportId")} = {P}Id ORDER BY {Col("SortOrder")}, {Col("StartTime")}", new { Id = deploymentTimeReportId });

		public Task<IEnumerable<DeploymentTimeEntry>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentTimeEntry>($"SELECT * FROM {Tbl("DeploymentTimeEntries")} WHERE {Col("DeploymentId")} = {P}Id ORDER BY {Col("StartTime")}", new { Id = deploymentId });

		public Task<int> DeleteByReportAsync(string deploymentTimeReportId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"DELETE FROM {Tbl("DeploymentTimeEntries")} WHERE {Col("DeploymentTimeReportId")} = {P}Id", new { Id = deploymentTimeReportId }, cancellationToken);
	}

	public class DeploymentExpenseRepository : RmsRepositoryBase<DeploymentExpense>, IDeploymentExpenseRepository
	{
		public DeploymentExpenseRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		private static string False => IsPostgres ? "FALSE" : "0";

		public Task<DeploymentExpense> GetByIdForDepartmentAsync(string deploymentExpenseId, int departmentId) =>
			QueryFirstOrDefaultAsync<DeploymentExpense>($"SELECT * FROM {Tbl("DeploymentExpenses")} WHERE {Col("DeploymentExpenseId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId", new { Id = deploymentExpenseId, DepartmentId = departmentId });

		public Task<IEnumerable<DeploymentExpense>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentExpense>($"SELECT * FROM {Tbl("DeploymentExpenses")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("IsDeleted")} = {False} ORDER BY {Col("ExpenseDate")}, {Col("AddedOn")}", new { Id = deploymentId });
	}

	public class DeploymentAttachmentRepository : RmsRepositoryBase<DeploymentAttachment>, IDeploymentAttachmentRepository
	{
		private static readonly string[] Meta = { "DeploymentAttachmentId", "DeploymentId", "DepartmentId", "AttachmentType", "Name", "FileName", "FileType", "FileSize", "IsDeleted", "AddedOn", "AddedByUserId" };

		public DeploymentAttachmentRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<IEnumerable<DeploymentAttachment>> GetByDeploymentAsync(string deploymentId) =>
			QueryAsync<DeploymentAttachment>($"SELECT {Cols(Meta)} FROM {Tbl("DeploymentAttachments")} WHERE {Col("DeploymentId")} = {P}Id AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")} ORDER BY {Col("AddedOn")} DESC", new { Id = deploymentId });

		public Task<DeploymentAttachment> GetMetadataByIdAsync(int deploymentAttachmentId) =>
			QueryFirstOrDefaultAsync<DeploymentAttachment>($"SELECT {Cols(Meta)} FROM {Tbl("DeploymentAttachments")} WHERE {Col("DeploymentAttachmentId")} = {P}Id", new { Id = deploymentAttachmentId });

		public Task<DeploymentAttachment> GetByIdWithDataAsync(int deploymentAttachmentId) =>
			QueryFirstOrDefaultAsync<DeploymentAttachment>($"SELECT * FROM {Tbl("DeploymentAttachments")} WHERE {Col("DeploymentAttachmentId")} = {P}Id", new { Id = deploymentAttachmentId });

		public Task<int> MarkDeletedAsync(int deploymentAttachmentId, int departmentId, CancellationToken cancellationToken = default) =>
			ExecuteAsync($"UPDATE {Tbl("DeploymentAttachments")} SET {Col("IsDeleted")} = {(IsPostgres ? "TRUE" : "1")} WHERE {Col("DeploymentAttachmentId")} = {P}Id AND {Col("DepartmentId")} = {P}DepartmentId AND {Col("IsDeleted")} = {(IsPostgres ? "FALSE" : "0")}",
				new { Id = deploymentAttachmentId, DepartmentId = departmentId }, cancellationToken);
	}

	public class TimeReportNumberSequenceRepository : RmsRepositoryBase<TimeReportNumberSequence>, ITimeReportNumberSequenceRepository
	{
		public TimeReportNumberSequenceRepository(IConnectionProvider connectionProvider, SqlConfiguration sqlConfiguration, IUnitOfWork unitOfWork, IQueryFactory queryFactory)
			: base(connectionProvider, sqlConfiguration, unitOfWork, queryFactory) { }

		public Task<int> GetNextNumberAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			// The invoice-sequence precedent: one atomic statement that inserts the row on first use (handing out 1) or advances it.
			string sql;
			if (IsPostgres)
				sql = $"INSERT INTO {Tbl("TimeReportNumberSequences")} ({Col("DepartmentId")}, {Col("NextReportNumber")}) VALUES ({P}DepartmentId, 2) " +
					  $"ON CONFLICT ({Col("DepartmentId")}) DO UPDATE SET {Col("NextReportNumber")} = {Tbl("TimeReportNumberSequences")}.{Col("NextReportNumber")} + 1 " +
					  $"RETURNING {Col("NextReportNumber")} - 1";
			else
				sql = $"MERGE {Tbl("TimeReportNumberSequences")} WITH (HOLDLOCK) AS t USING (SELECT {P}DepartmentId AS DepartmentId) AS s ON t.[DepartmentId] = s.DepartmentId " +
					  "WHEN MATCHED THEN UPDATE SET [NextReportNumber] = t.[NextReportNumber] + 1 " +
					  "WHEN NOT MATCHED THEN INSERT ([DepartmentId], [NextReportNumber]) VALUES (s.DepartmentId, 2) " +
					  "OUTPUT inserted.[NextReportNumber] - 1;";
			return ScalarAsync<int>(sql, new { DepartmentId = departmentId }, cancellationToken);
		}
	}
}

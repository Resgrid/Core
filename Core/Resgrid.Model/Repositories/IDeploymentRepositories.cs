using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Repositories
{
	// Workforce & Business Operations plan, Phase C (C3): the deployment core repositories (M0218). Every query begins
	// at DepartmentId. External orders and fills are read through IRecordDeploymentsService, never here.

	public interface IDeploymentRepository : IRepository<Deployment>
	{
		Task<Deployment> GetByIdForDepartmentAsync(string deploymentId, int departmentId);
		Task<Deployment> GetByCallIdAsync(int callId, int departmentId);
		Task<Deployment> GetByExternalOrderIdAsync(string rmsExternalOrderId, int departmentId);
		/// <summary>Non-deleted deployments, newest first; <paramref name="openOnly"/> keeps Planned/Standby/Active/Demobilizing.</summary>
		Task<IEnumerable<Deployment>> GetForDepartmentAsync(int departmentId, bool openOnly, int skip, int take);
		Task<int> CountForDepartmentAsync(int departmentId, bool openOnly);
		Task<IEnumerable<Deployment>> GetByIdsAsync(int departmentId, IEnumerable<string> deploymentIds);
	}

	public interface IDeploymentUnitRepository : IRepository<DeploymentUnit>
	{
		Task<IEnumerable<DeploymentUnit>> GetByDeploymentAsync(string deploymentId);
		/// <summary>Units seated on another open deployment overlapping the window (wizard conflict detection).</summary>
		Task<IEnumerable<DeploymentUnit>> GetActiveAssignmentsForUnitsAsync(int departmentId, IEnumerable<int> unitIds, DateTime windowStart, DateTime windowEnd, string excludingDeploymentId);
	}

	public interface IDeploymentPersonnelRepository : IRepository<DeploymentPersonnel>
	{
		Task<IEnumerable<DeploymentPersonnel>> GetByDeploymentAsync(string deploymentId);
		/// <summary>Members seated on another open deployment overlapping the window (wizard conflict detection, plan C3).</summary>
		Task<IEnumerable<DeploymentPersonnel>> GetActiveAssignmentsForUsersAsync(int departmentId, IEnumerable<string> userIds, DateTime windowStart, DateTime windowEnd, string excludingDeploymentId);
		/// <summary>Every roster row (active or removed) for one member; the field user's "my deployments" scope.</summary>
		Task<IEnumerable<DeploymentPersonnel>> GetForUserAsync(int departmentId, string userId);
	}

	public interface IDeploymentEquipmentRepository : IRepository<DeploymentEquipment>
	{
		Task<IEnumerable<DeploymentEquipment>> GetByDeploymentAsync(string deploymentId);
	}

	public interface IDeploymentTimeReportRepository : IRepository<DeploymentTimeReport>
	{
		Task<DeploymentTimeReport> GetByIdForDepartmentAsync(string deploymentTimeReportId, int departmentId);
		Task<IEnumerable<DeploymentTimeReport>> GetByDeploymentAsync(string deploymentId);
		Task<DeploymentTimeReport> GetByDeploymentAndDateAsync(string deploymentId, DateTime reportDate);
		/// <summary>Approved reports not yet on an invoice (the billing engine's input).</summary>
		Task<IEnumerable<DeploymentTimeReport>> GetUnbilledApprovedAsync(int departmentId, string deploymentId = null);
	}

	public interface IDeploymentTimeEntryRepository : IRepository<DeploymentTimeEntry>
	{
		Task<IEnumerable<DeploymentTimeEntry>> GetByReportAsync(string deploymentTimeReportId);
		Task<IEnumerable<DeploymentTimeEntry>> GetByDeploymentAsync(string deploymentId);
		Task<int> DeleteByReportAsync(string deploymentTimeReportId, CancellationToken cancellationToken = default);
	}

	public interface IDeploymentExpenseRepository : IRepository<DeploymentExpense>
	{
		Task<DeploymentExpense> GetByIdForDepartmentAsync(string deploymentExpenseId, int departmentId);
		Task<IEnumerable<DeploymentExpense>> GetByDeploymentAsync(string deploymentId);
	}

	public interface IDeploymentAttachmentRepository : IRepository<DeploymentAttachment>
	{
		/// <summary>Attachment rows without their bytes.</summary>
		Task<IEnumerable<DeploymentAttachment>> GetByDeploymentAsync(string deploymentId);
		Task<DeploymentAttachment> GetByIdWithDataAsync(int deploymentAttachmentId);
	}

	public interface ITimeReportNumberSequenceRepository : IRepository<TimeReportNumberSequence>
	{
		/// <summary>Atomically returns the next report number for the department and advances the sequence (dialect-specific SQL).</summary>
		Task<int> GetNextNumberAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}

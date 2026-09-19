using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase C (C4): the free deployment core. Deployments, their roster
	/// (units, personnel seats, equipment), attachments, the manifest and the link to an RMS external order.
	/// Entitlement and permission checks are the callers' job; the service trusts its caller's authorization
	/// except for <see cref="IsRosteredAsync"/>, which the surfaces use for the field member's own-deployment scope.
	/// Every mutation audits and the lifecycle mutations publish their Workflow trigger through the domain outbox.
	/// </summary>
	public interface IDeploymentService
	{
		Task<Deployment> GetDeploymentByIdAsync(string deploymentId, int departmentId);
		Task<Deployment> GetDeploymentByCallIdAsync(int callId, int departmentId);
		Task<Deployment> GetDeploymentByExternalOrderIdAsync(string rmsExternalOrderId, int departmentId);
		Task<List<Deployment>> GetDeploymentsForDepartmentAsync(int departmentId, bool openOnly, int skip = 0, int take = 100);
		Task<int> CountDeploymentsForDepartmentAsync(int departmentId, bool openOnly);
		/// <summary>Deployments the member is or was rostered on (the field user's scope).</summary>
		Task<List<Deployment>> GetDeploymentsForUserAsync(int departmentId, string userId, bool openOnly);
		Task<bool> IsRosteredAsync(string deploymentId, int departmentId, string userId);

		Task<Deployment> SaveDeploymentAsync(Deployment deployment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Planned→Standby→Active→Demobilizing→Completed, Cancelled from any open state; Completed/Cancelled are terminal.</summary>
		Task<Deployment> SetDeploymentStatusAsync(string deploymentId, int departmentId, DeploymentStatuses status, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteDeploymentAsync(string deploymentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Creates the finance wrapper for an RMS mutual-aid deployment Record (decision 39).</summary>
		Task<Deployment> CreateFromExternalOrderAsync(int departmentId, ExternalOrderDeploymentInput input, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>The order and fills behind a deployment, read through the Records service (never the RMS tables).</summary>
		Task<DeploymentExternalContext> GetExternalContextAsync(string deploymentId, int departmentId, string userId);

		Task<DeploymentRosterResult> AddUnitAsync(string deploymentId, int departmentId, int unitId, string callSign, string notes, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> RemoveUnitAsync(string deploymentUnitId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<DeploymentRosterResult> AddPersonnelAsync(string deploymentId, int departmentId, DeploymentPersonnelInput input, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> RemovePersonnelAsync(string deploymentPersonnelId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<DeploymentRosterResult> AddEquipmentAsync(string deploymentId, int departmentId, DeploymentEquipmentInput input, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> ReturnEquipmentAsync(string deploymentEquipmentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Seat/schedule warnings for candidate members without writing anything (the wizard's conflict badges).</summary>
		Task<List<DeploymentRosterWarning>> GetRosterWarningsAsync(string deploymentId, int departmentId, IEnumerable<string> userIds, IEnumerable<int> unitIds);
		Task<int> GetCrewSizeForUnitAsync(string deploymentUnitId, int departmentId);

		Task<List<DeploymentAttachment>> GetAttachmentsAsync(string deploymentId, int departmentId);
		Task<DeploymentAttachment> GetAttachmentAsync(int deploymentAttachmentId, int departmentId, bool includeData);
		Task<DeploymentAttachment> SaveAttachmentAsync(DeploymentAttachment attachment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteAttachmentAsync(int deploymentAttachmentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		Task<string> RenderManifestHtmlAsync(string deploymentId, int departmentId);
		/// <summary>Renders the manifest (roster, call signs, certifications) to PDF and files it as a Manifest attachment.</summary>
		Task<DeploymentAttachment> GenerateManifestAsync(string deploymentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
	}
}

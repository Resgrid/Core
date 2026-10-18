using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Carries out a staff-requested activation or invitation run (wizard plan section 17.8) once two Resgrid staff members
	/// have approved it, its notice period has passed (or an administrator approved sooner) and, when the grant asks for it,
	/// the department has approved it. Each step re-checks what the commit handler re-checks: the module, the on-behalf-of
	/// administrator, and the Operate grant allowing this action. Every step is idempotent, so a retried run repeats nothing.
	/// </summary>
	public class ImportActivationRunHandler : IImportRunHandler
	{
		private readonly IImportActivationService _activation;
		private readonly IDepartmentImportAccessService _access;
		private readonly IImportSupportGrantsRepository _grants;
		private readonly IImportWorkspacesRepository _workspaces;

		public ImportActivationRunHandler(IImportActivationService activation, IDepartmentImportAccessService access, IImportSupportGrantsRepository grants,
			IImportWorkspacesRepository workspaces)
		{
			_activation = activation;
			_access = access;
			_grants = grants;
			_workspaces = workspaces;
		}

		public ImportRunKind Kind => ImportRunKind.Activation;

		public async Task<ImportStepResult> ExecuteAsync(ImportRun run, ImportRunLease lease, DateTime deadline, CancellationToken cancellationToken)
		{
			var authority = string.IsNullOrWhiteSpace(run.Authority) ? null : JsonConvert.DeserializeObject<ImportAuthority>(run.Authority);
			// Administrators do these steps themselves on Verify & activate; only a twice-approved staff request becomes a run.
			if (authority?.IsStaff != true || string.IsNullOrWhiteSpace(authority.StaffOperator) || string.IsNullOrWhiteSpace(authority.StaffChecker))
				return ImportStepResult.Failed("IMP-APPROVER-NOT-AUTHORIZED");

			var request = ImportActivationService.ManagedRequest(run);
			var action = request.Kind == ImportManagedActivationKind.Invitations ? ImportOperateActions.SendInvitations : ImportOperateActions.Activate;

			if (!await _access.IsEnabledAsync(run.DepartmentId))
				return ImportStepResult.Paused("IMP-ENTITLEMENT-MISSING");
			if (!await _access.CanManageAsync(run.DepartmentId, authority.OnBehalfOfUserId))
				return ImportStepResult.Paused("IMP-APPROVER-NOT-AUTHORIZED");
			if (!await ImportCommitHandler.StaffGrantAllowsAsync(_grants, run, authority, action))
				return ImportStepResult.Paused("IMP-APPROVER-NOT-AUTHORIZED");

			var grant = DepartmentImportSupportService.ActiveGrant(await _grants.GetForWorkspaceAsync(run.DepartmentId, run.WorkspaceId, cancellationToken));
			if (grant.RequireDepartmentSignOff && !request.DepartmentApprovedOn.HasValue)
				return ImportStepResult.Paused("IMP-DEPARTMENT-SIGNOFF-REQUIRED");
			// Nobody is paged by surprise: never before the notice the department was given, unless an administrator approved sooner.
			if (request.NotBefore.HasValue && request.NotBefore.Value > DateTime.UtcNow && !request.DepartmentApprovedOn.HasValue)
				return ImportStepResult.Paused("IMP-NOTICE-PERIOD");

			try
			{
				if (request.Kind == ImportManagedActivationKind.Invitations)
				{
					var invited = await _activation.InviteOnBehalfAsync(run.DepartmentId, authority, run.WorkspaceId, request.Invitations ?? new ImportInvitationRequest(),
						cancellationToken);
					run.Counts = JsonConvert.SerializeObject(new { invited });
				}
				else
				{
					var applied = request.AdministratorUserIds?.Count > 0
						? await _activation.ApplyAdministratorsOnBehalfAsync(run.DepartmentId, authority, run.WorkspaceId, request.AdministratorUserIds, cancellationToken)
						: 0;

					var signedOff = false;
					if (request.SignOff)
					{
						var workspace = await _workspaces.GetByIdAsync(run.DepartmentId, run.WorkspaceId, cancellationToken);
						if (workspace != null && !workspace.ActivatedOn.HasValue)
						{
							await _activation.SignOffOnBehalfAsync(run.DepartmentId, authority, run.WorkspaceId, cancellationToken);
							signedOff = true;
						}
					}
					run.Counts = JsonConvert.SerializeObject(new { administrators = applied, signedOff });
				}
			}
			catch (DepartmentImportException ex)
			{
				// Deterministic refusals (not ready to sign off, nobody left to invite, permission gone) are not retried.
				return ImportStepResult.Failed(ex.Code);
			}

			return ImportStepResult.Done();
		}
	}
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Workflows;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Manages encrypted credentials used by workflow action executors.
	/// Secrets are write-only and are never returned in responses.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/WorkflowCredentials")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class WorkflowCredentialsController : V4AuthenticatedApiControllerbase
	{
		private readonly IWorkflowService _workflowService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IPermissionsService _permissionsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public WorkflowCredentialsController(IWorkflowService workflowService, IDepartmentsService departmentsService,
			IPermissionsService permissionsService, IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService)
		{
			_workflowService    = workflowService;
			_departmentsService = departmentsService;
			_permissionsService = permissionsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
		}

		/// <summary>Lists all credentials for the current department (secrets masked).</summary>
		[HttpGet("GetAll")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_View)]
		public async Task<ActionResult<GetCredentialsResult>> GetAll(CancellationToken ct)
		{
			var creds = await _workflowService.GetCredentialsByDepartmentIdAsync(DepartmentId, ct);
			return Ok(new GetCredentialsResult
			{
				Data = creds.Select(MapCredentialSummary).ToList()
			});
		}

		/// <summary>Gets a single credential by ID (secrets masked).</summary>
		[HttpGet("GetById/{credentialId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_View)]
		public async Task<ActionResult<CredentialSummaryData>> GetById(string credentialId, CancellationToken ct)
		{
			var cred = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (cred == null || cred.DepartmentId != DepartmentId)
				return NotFound();

			return Ok(MapCredentialSummary(cred));
		}

		/// <summary>Creates a new credential. Plaintext secrets are encrypted before storage.</summary>
		[HttpPost("Create")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Create)]
		public async Task<ActionResult<SaveCredentialResult>> Create([FromBody] WorkflowCredentialInput input, CancellationToken ct)
		{
			if (!ModelState.IsValid)
				return BadRequest(ModelState);

			if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var credential = new WorkflowCredential
			{
				DepartmentId    = DepartmentId,
				Name            = input.Name,
				CredentialType  = input.CredentialType,
				EncryptedData   = input.PlaintextData, // WorkflowService.SaveCredentialAsync will encrypt
				CreatedByUserId = UserId
			};

			var saved = await _workflowService.SaveCredentialAsync(credential, department?.Code ?? string.Empty, ct);
			return Ok(new SaveCredentialResult { WorkflowCredentialId = saved.WorkflowCredentialId.ToString() });
		}

		/// <summary>Updates an existing credential.</summary>
		[HttpPut("Update/{credentialId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Update)]
		public async Task<ActionResult<SaveCredentialResult>> Update(string credentialId, [FromBody] WorkflowCredentialInput input, CancellationToken ct)
		{
			var existing = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			existing.Name          = input.Name;
			existing.CredentialType = input.CredentialType;

			if (!string.IsNullOrWhiteSpace(input.PlaintextData))
				existing.EncryptedData = input.PlaintextData; // service will re-encrypt

			var saved = await _workflowService.SaveCredentialAsync(existing, department?.Code ?? string.Empty, ct);
			return Ok(new SaveCredentialResult { WorkflowCredentialId = saved.WorkflowCredentialId.ToString() });
		}

		/// <summary>Deletes a credential.</summary>
		[HttpDelete("Delete/{credentialId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.WorkflowCredential_Delete)]
		public async Task<ActionResult<DeleteWorkflowResult>> Delete(string credentialId, CancellationToken ct)
		{
			var existing = await _workflowService.GetCredentialByIdAsync(credentialId, ct);
			if (existing == null || existing.DepartmentId != DepartmentId)
				return NotFound();

			if (!await CanUserManageWorkflowCredentialsAsync()) return Forbid();

			var success = await _workflowService.DeleteCredentialAsync(credentialId, ct);
			return Ok(new DeleteWorkflowResult { Success = success });
		}

		// ── Permission Helpers ─────────────────────────────────────────────────────────

		private async Task<bool> CanUserManageWorkflowCredentialsAsync()
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.ManageWorkflowCredentials);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			bool isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
			return _permissionsService.IsUserAllowed(permission, department.IsUserAnAdmin(UserId), isGroupAdmin, roles);
		}

		// ── Mapping helper ─────────────────────────────────────────────────────────

		private static CredentialSummaryData MapCredentialSummary(WorkflowCredential c) =>
			new CredentialSummaryData
			{
				WorkflowCredentialId = c.WorkflowCredentialId.ToString(),
				Name                 = c.Name,
				CredentialType       = c.CredentialType,
				CreatedOn            = c.CreatedOn
			};
	}

	// ── Input DTOs (write-only secrets) ────────────────────────────────────────

	public class WorkflowCredentialInput
	{
		public string Name           { get; set; }
		public int    CredentialType { get; set; }
		/// <summary>Plaintext JSON blob of credential fields. Never returned in any GET response.</summary>
		public string PlaintextData  { get; set; }
	}
}


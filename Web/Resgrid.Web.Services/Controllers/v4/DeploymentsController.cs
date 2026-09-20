using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Deployments;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Deployment finance wrappers, roster and attachments (Workforce &amp; Business Operations plan, Phase C5). The
	/// deployment core is free behind the Operations.Deployments flag. Managers (Deployments_Update) see and edit every
	/// deployment; a rostered member sees the deployments they are seated on. RMS owns the RecordDeployments routes;
	/// this route is Deployments and its DTOs carry RmsExternalOrderId so a client can open the Records deployment.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class DeploymentsController : V4AuthenticatedApiControllerbase
	{
		private readonly IDeploymentService _deployments;
		private readonly IFeatureToggleService _flags;
		private readonly IBusinessOperationsAccessService _access;

		public DeploymentsController(IDeploymentService deployments, IFeatureToggleService flags, IBusinessOperationsAccessService access)
		{
			_deployments = deployments;
			_flags = flags;
			_access = access;
		}

		internal static bool CanManage() => ClaimsAuthorizationHelper.CanManageDeployments() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		internal static bool CanView() => ClaimsAuthorizationHelper.CanViewDeployments() || CanManage();

		private Task<bool> EnabledAsync() => _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId);

		private ActionResult<T> Failed<T>(string reason, int status = StatusCodes.Status400BadRequest) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return StatusCode(status, failed);
		}

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		/// <summary>Whether the deployment core is available to this department and what the caller may do. Always answers.</summary>
		[HttpGet("GetAccess")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentAccessResult>> GetAccess()
		{
			var result = new DeploymentAccessResult
			{
				Data = new DeploymentAccessData
				{
					Enabled = await EnabledAsync(),
					CanManage = CanManage(),
					CanApproveTimeReports = ClaimsAuthorizationHelper.CanApproveTimeReports() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
					ContractorBilling = await _access.CanUseContractorBillingAsync(DepartmentId)
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetDeployments")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentsResult>> GetDeployments(bool? active = null, int skip = 0, int take = 100)
		{
			if (!await EnabledAsync()) return Failed<DeploymentsResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			var openOnly = active ?? true;
			var deployments = CanView()
				? await _deployments.GetDeploymentsForDepartmentAsync(DepartmentId, openOnly, skip, take)
				: await _deployments.GetDeploymentsForUserAsync(DepartmentId, UserId, openOnly);
			var result = new DeploymentsResult { Data = deployments.Select(d => Map(d, false)).ToList(), PageSize = deployments.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetDeployment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentResult>> GetDeployment(string id)
		{
			if (!await EnabledAsync()) return Failed<DeploymentResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			var deployment = await _deployments.GetDeploymentByIdAsync(id, DepartmentId);
			if (deployment == null) return NotFound();
			if (!CanView() && !deployment.Personnel.Any(p => p.UserId == UserId)) return Unauthorized();
			return Ok(deployment);
		}

		/// <summary>The billing context behind a call (mobile: call → deployment).</summary>
		[HttpGet("GetDeploymentByCallId")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentResult>> GetDeploymentByCallId(int callId)
		{
			if (!await EnabledAsync()) return Failed<DeploymentResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			var deployment = await _deployments.GetDeploymentByCallIdAsync(callId, DepartmentId);
			if (deployment == null) return NotFound();
			if (!CanView() && !deployment.Personnel.Any(p => p.UserId == UserId)) return Unauthorized();
			return Ok(deployment);
		}

		[HttpPost("SaveDeployment")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentResult>> SaveDeployment([FromBody] SaveDeploymentInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<DeploymentResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var deployment = string.IsNullOrWhiteSpace(input.Id) ? new Deployment { DepartmentId = DepartmentId } : await _deployments.GetDeploymentByIdAsync(input.Id, DepartmentId);
				if (deployment == null) return NotFound();
				deployment.Name = input.Name;
				deployment.FinanceMode = input.FinanceMode;
				deployment.CallId = input.CallId;
				deployment.ContactId = input.ContactId;
				deployment.IncidentNumber = input.IncidentNumber;
				deployment.ServiceRequestNumber = input.ServiceRequestNumber;
				deployment.ResourceOrderNumber = input.ResourceOrderNumber;
				deployment.RequestNumber = input.RequestNumber;
				deployment.CostCode = input.CostCode;
				deployment.PointOfHire = input.PointOfHire;
				deployment.StartOn = input.StartOn;
				deployment.EndOn = input.EndOn;
				deployment.MaxDays = input.MaxDays;
				deployment.OutOfProvince = input.OutOfProvince;
				deployment.TravelViaAir = input.TravelViaAir;
				deployment.HomeCountry = input.HomeCountry;
				deployment.HostCountry = input.HostCountry;
				deployment.HomeSubdivision = input.HomeSubdivision;
				deployment.HostSubdivision = input.HostSubdivision;
				deployment.LocalTimeZoneId = input.LocalTimeZoneId;
				deployment.Currency = input.Currency;
				deployment.Notes = input.Notes;
				var saved = await _deployments.SaveDeploymentAsync(deployment, UserId, Ip, Agent, cancellationToken);
				return Ok(saved);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<DeploymentResult>(ex.Message); }
		}

		[HttpPost("CreateFromExternalOrder")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentResult>> CreateFromExternalOrder([FromBody] CreateFromExternalOrderInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<DeploymentResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var saved = await _deployments.CreateFromExternalOrderAsync(DepartmentId, new ExternalOrderDeploymentInput
				{
					RmsExternalOrderId = input.RmsExternalOrderId, FinanceMode = (DeploymentFinanceModes)input.FinanceMode, CreateCall = input.CreateCall, CallType = input.CallType, CallPriority = input.CallPriority,
					PrefillRoster = input.PrefillRoster, Name = input.Name, ContactId = input.ContactId, Notes = input.Notes
				}, UserId, Ip, Agent, cancellationToken);
				return Ok(saved);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<DeploymentResult>(ex.Message); }
			catch (UnauthorizedAccessException) { return Unauthorized(); }
		}

		[HttpPost("SetDeploymentStatus")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentResult>> SetDeploymentStatus([FromBody] SetDeploymentStatusInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<DeploymentResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null || !Enum.IsDefined(typeof(DeploymentStatuses), input.Status)) return BadRequest();
			try { return Ok(await _deployments.SetDeploymentStatusAsync(input.Id, DepartmentId, (DeploymentStatuses)input.Status, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<DeploymentResult>(ex.Message); }
		}

		#region Roster

		[HttpPost("AddUnit")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RosterChangeResult>> AddUnit([FromBody] AddDeploymentUnitInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RosterChangeResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try { return Ok(await _deployments.AddUnitAsync(input.DeploymentId, DepartmentId, input.UnitId, input.CallSign, input.Notes, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<RosterChangeResult>(ex.Message); }
		}

		[HttpDelete("RemoveUnit")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> RemoveUnit(string deploymentUnitId, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("deployments_disabled", StatusCodes.Status403Forbidden);
			try { return await _deployments.RemoveUnitAsync(deploymentUnitId, DepartmentId, UserId, Ip, Agent, cancellationToken) ? Done() : NotFound(); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<StandardApiResponseV4Base>(ex.Message); }
		}

		[HttpPost("AddPersonnel")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RosterChangeResult>> AddPersonnel([FromBody] AddDeploymentPersonnelInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RosterChangeResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				return Ok(await _deployments.AddPersonnelAsync(input.DeploymentId, DepartmentId, new DeploymentPersonnelInput
				{
					UserId = input.UserId, DeploymentUnitId = input.DeploymentUnitId, UnitRoleId = input.UnitRoleId, CertificationCode = input.CertificationCode, CallSign = input.CallSign, RmsExternalOrderFillId = input.RmsExternalOrderFillId, Force = input.Force
				}, UserId, Ip, Agent, cancellationToken));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<RosterChangeResult>(ex.Message); }
		}

		[HttpDelete("RemovePersonnel")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> RemovePersonnel(string deploymentPersonnelId, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("deployments_disabled", StatusCodes.Status403Forbidden);
			try { return await _deployments.RemovePersonnelAsync(deploymentPersonnelId, DepartmentId, UserId, Ip, Agent, cancellationToken) ? Done() : NotFound(); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<StandardApiResponseV4Base>(ex.Message); }
		}

		[HttpPost("AddEquipment")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RosterChangeResult>> AddEquipment([FromBody] AddDeploymentEquipmentInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<RosterChangeResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				return Ok(await _deployments.AddEquipmentAsync(input.DeploymentId, DepartmentId, new DeploymentEquipmentInput
				{
					DeploymentUnitId = input.DeploymentUnitId, InventoryAssetId = input.InventoryAssetId, InventoryItemId = input.InventoryItemId, FreeTextName = input.FreeTextName, Notes = input.Notes, IssueFromInventory = input.IssueFromInventory, FromLocationId = input.FromLocationId
				}, UserId, Ip, Agent, cancellationToken));
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<RosterChangeResult>(ex.Message); }
		}

		[HttpPost("ReturnEquipment")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> ReturnEquipment(string deploymentEquipmentId, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("deployments_disabled", StatusCodes.Status403Forbidden);
			return await _deployments.ReturnEquipmentAsync(deploymentEquipmentId, DepartmentId, UserId, Ip, Agent, cancellationToken) ? Done() : NotFound();
		}

		[HttpGet("GetRosterWarnings")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RosterWarningsResult>> GetRosterWarnings(string deploymentId, [FromQuery] List<string> userIds, [FromQuery] List<int> unitIds)
		{
			if (!await EnabledAsync()) return Failed<RosterWarningsResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			try
			{
				var warnings = await _deployments.GetRosterWarningsAsync(deploymentId, DepartmentId, userIds, unitIds);
				var result = new RosterWarningsResult { Data = warnings.Select(MapWarning).ToList(), PageSize = warnings.Count, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<RosterWarningsResult>(ex.Message); }
		}

		#endregion

		#region Attachments

		[HttpGet("GetAttachments")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentAttachmentsResult>> GetAttachments(string deploymentId)
		{
			if (!await EnabledAsync()) return Failed<DeploymentAttachmentsResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (!CanView() && !await _deployments.IsRosteredAsync(deploymentId, DepartmentId, UserId)) return Unauthorized();
			var rows = await _deployments.GetAttachmentsAsync(deploymentId, DepartmentId);
			var result = new DeploymentAttachmentsResult { Data = rows.Select(MapAttachment).ToList(), PageSize = rows.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("GenerateManifest")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DeploymentAttachmentsResult>> GenerateManifest(string deploymentId, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<DeploymentAttachmentsResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			try
			{
				var attachment = await _deployments.GenerateManifestAsync(deploymentId, DepartmentId, UserId, Ip, Agent, cancellationToken);
				var result = new DeploymentAttachmentsResult { Data = new List<DeploymentAttachmentData> { MapAttachment(attachment) }, PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("deployments_", StringComparison.Ordinal)) { return Failed<DeploymentAttachmentsResult>(ex.Message); }
		}

		[HttpDelete("DeleteAttachment")]
		[Authorize(Policy = ResgridResources.Deployments_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteAttachment(int deploymentAttachmentId, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("deployments_disabled", StatusCodes.Status403Forbidden);
			return await _deployments.DeleteAttachmentAsync(deploymentAttachmentId, DepartmentId, UserId, Ip, Agent, cancellationToken) ? Done() : NotFound();
		}

		#endregion

		#region Mapping

		private ActionResult<DeploymentResult> Ok(Deployment deployment)
		{
			var result = new DeploymentResult { Data = Map(deployment, true), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult<RosterChangeResult> Ok(DeploymentRosterResult change)
		{
			var result = new RosterChangeResult
			{
				Data = new RosterChangeData
				{
					Unit = change.Unit == null ? null : MapUnit(change.Unit), Personnel = change.Personnel == null ? null : MapPersonnel(change.Personnel), Equipment = change.Equipment == null ? null : MapEquipment(change.Equipment),
					Warnings = change.Warnings.Select(MapWarning).ToList(), Blocked = change.HasBlockingWarnings && change.Unit == null && change.Personnel == null && change.Equipment == null
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult<StandardApiResponseV4Base> Done()
		{
			var result = new StandardApiResponseV4Base { PageSize = 0, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		internal static DeploymentData Map(Deployment d, bool roster) => new DeploymentData
		{
			Id = d.DeploymentId, Name = d.Name, Status = d.Status, FinanceMode = d.FinanceMode, CallId = d.CallId, RmsExternalOrderId = d.RmsExternalOrderId, ContactId = d.ContactId, ServiceContractId = d.ServiceContractId,
			IncidentNumber = d.IncidentNumber, ServiceRequestNumber = d.ServiceRequestNumber, ResourceOrderNumber = d.ResourceOrderNumber, RequestNumber = d.RequestNumber, CostCode = d.CostCode, PointOfHire = d.PointOfHire,
			StartOn = d.StartOn, EndOn = d.EndOn, MaxDays = d.MaxDays, OutOfProvince = d.OutOfProvince, TravelViaAir = d.TravelViaAir, HomeCountry = d.HomeCountry, HostCountry = d.HostCountry, HomeSubdivision = d.HomeSubdivision, HostSubdivision = d.HostSubdivision,
			LocalTimeZoneId = d.LocalTimeZoneId, Currency = d.Currency, Notes = d.Notes, StatusChangedOn = d.StatusChangedOn, AddedOn = d.AddedOn, UpdatedOn = d.EditedOn ?? d.AddedOn,
			Units = roster ? d.Units.Select(MapUnit).ToList() : new List<DeploymentUnitData>(),
			Personnel = roster ? d.Personnel.Select(MapPersonnel).ToList() : new List<DeploymentPersonnelData>(),
			Equipment = roster ? d.Equipment.Select(MapEquipment).ToList() : new List<DeploymentEquipmentData>()
		};

		internal static DeploymentUnitData MapUnit(DeploymentUnit u) => new DeploymentUnitData { Id = u.DeploymentUnitId, UnitId = u.UnitId, UnitName = u.UnitName, CallSign = u.CallSign, Notes = u.Notes, IsActive = u.IsActive, AddedOn = u.AddedOn, RemovedOn = u.RemovedOn };
		internal static DeploymentPersonnelData MapPersonnel(DeploymentPersonnel p) => new DeploymentPersonnelData { Id = p.DeploymentPersonnelId, UserId = p.UserId, Name = p.DisplayName, DeploymentUnitId = p.DeploymentUnitId, UnitRoleId = p.UnitRoleId, CertificationCode = p.CertificationCode, CallSign = p.CallSign, RmsExternalOrderFillId = p.RmsExternalOrderFillId, IsActive = p.IsActive, AddedOn = p.AddedOn, RemovedOn = p.RemovedOn };
		internal static DeploymentEquipmentData MapEquipment(DeploymentEquipment e) => new DeploymentEquipmentData { Id = e.DeploymentEquipmentId, DeploymentUnitId = e.DeploymentUnitId, InventoryAssetId = e.InventoryAssetId, InventoryItemId = e.InventoryItemId, Name = e.FreeTextName ?? e.InventoryAssetId ?? e.InventoryItemId, Notes = e.Notes, IssuedOn = e.IssuedOn, ReturnedOn = e.ReturnedOn, IsActive = e.IsActive };
		internal static RosterWarningData MapWarning(DeploymentRosterWarning w) => new RosterWarningData { Code = w.Code, SubjectId = w.SubjectId, Detail = w.Detail, Blocking = w.Blocking };
		internal static DeploymentAttachmentData MapAttachment(DeploymentAttachment a) => new DeploymentAttachmentData { Id = a.DeploymentAttachmentId, DeploymentId = a.DeploymentId, AttachmentType = a.AttachmentType, Name = a.Name, FileName = a.FileName, FileType = a.FileType, FileSize = a.FileSize, AddedOn = a.AddedOn, AddedByUserId = a.AddedByUserId };

		#endregion
	}
}

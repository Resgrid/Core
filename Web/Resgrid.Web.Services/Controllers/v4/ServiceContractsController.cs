using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.ContractorBilling;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Service contracts, document requirements and department compliance documents (Workforce &amp; Business
	/// Operations plan, C5). Gated by the Invoicing.ContractorBilling entitlement; ServiceContracts_View reads,
	/// ServiceContracts_Update writes. Compliance document bytes are served only through GetComplianceDocumentFile.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class ServiceContractsController : V4AuthenticatedApiControllerbase
	{
		private readonly IServiceContractService _contracts;
		private readonly IBusinessOperationsAccessService _access;

		public ServiceContractsController(IServiceContractService contracts, IBusinessOperationsAccessService access)
		{
			_contracts = contracts;
			_access = access;
		}

		private Task<bool> EnabledAsync() => _access.CanUseContractorBillingAsync(DepartmentId);
		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		private ActionResult<T> Failed<T>(string reason, int status = StatusCodes.Status400BadRequest) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return StatusCode(status, failed);
		}

		[HttpGet("GetContracts")]
		[Authorize(Policy = ResgridResources.ServiceContracts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ServiceContractsResult>> GetContracts(int? status = null, string contactId = null)
		{
			if (!await EnabledAsync()) return Failed<ServiceContractsResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var contracts = string.IsNullOrWhiteSpace(contactId)
				? await _contracts.GetContractsForDepartmentAsync(DepartmentId, status.HasValue && Enum.IsDefined(typeof(ServiceContractStatuses), status.Value) ? (ServiceContractStatuses?)status.Value : null)
				: await _contracts.GetContractsByContactIdAsync(contactId, DepartmentId);
			var result = new ServiceContractsResult { Data = contracts.Select(c => Map(c, false)).ToList(), PageSize = contracts.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetContract")]
		[Authorize(Policy = ResgridResources.ServiceContracts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ServiceContractResult>> GetContract(string id)
		{
			if (!await EnabledAsync()) return Failed<ServiceContractResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var contract = await _contracts.GetContractByIdAsync(id, DepartmentId);
			return contract == null ? NotFound() : Ok(contract);
		}

		[HttpPost("SaveContract")]
		[Authorize(Policy = ResgridResources.ServiceContracts_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ServiceContractResult>> SaveContract([FromBody] SaveServiceContractInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<ServiceContractResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var saved = await _contracts.SaveContractAsync(new ServiceContract
				{
					ServiceContractId = input.Id, DepartmentId = DepartmentId, ContactId = input.ContactId, ContractNumber = input.ContractNumber, Name = input.Name, ContractType = input.ContractType,
					StartOn = input.StartOn, EndOn = input.EndOn, RateScheduleId = input.RateScheduleId, DiscountPercent = input.DiscountPercent, TermsNetDays = input.TermsNetDays,
					InvoiceSubmissionEmail = input.InvoiceSubmissionEmail, MaxDeploymentDays = input.MaxDeploymentDays, ResponseTimeMinutes = input.ResponseTimeMinutes, PointOfHire = input.PointOfHire,
					DocumentTemplateKey = input.DocumentTemplateKey, Notes = input.Notes
				}, UserId, Ip, Agent, cancellationToken);
				return Ok(saved);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("contracts_", StringComparison.Ordinal)) { return Failed<ServiceContractResult>(ex.Message); }
		}

		[HttpPost("SetContractStatus")]
		[Authorize(Policy = ResgridResources.ServiceContracts_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ServiceContractResult>> SetContractStatus([FromBody] SetContractStatusInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<ServiceContractResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null || !Enum.IsDefined(typeof(ServiceContractStatuses), input.Status)) return BadRequest();
			try { return Ok(await _contracts.SetContractStatusAsync(input.Id, DepartmentId, (ServiceContractStatuses)input.Status, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("contracts_", StringComparison.Ordinal)) { return Failed<ServiceContractResult>(ex.Message); }
		}

		[HttpDelete("DeleteContract")]
		[Authorize(Policy = ResgridResources.ServiceContracts_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteContract(string id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			try
			{
				if (!await _contracts.DeleteContractAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
				return Empty();
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("contracts_", StringComparison.Ordinal)) { return Failed<StandardApiResponseV4Base>(ex.Message); }
		}

		[HttpPost("SaveRequirements")]
		[Authorize(Policy = ResgridResources.ServiceContracts_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ContractRequirementsResult>> SaveRequirements([FromBody] SaveContractRequirementsInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<ContractRequirementsResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var saved = await _contracts.SaveRequirementsAsync(input.ServiceContractId, DepartmentId, (input.Requirements ?? new List<ContractRequirementData>()).Select(r => new ServiceContractDocumentRequirement
				{
					ServiceContractDocumentRequirementId = r.Id, Name = r.Name, Stage = r.Stage, ComplianceDocumentType = r.ComplianceDocumentType, IsMandatory = r.IsMandatory, SortOrder = r.SortOrder
				}).ToList(), UserId, Ip, Agent, cancellationToken);
				var result = new ContractRequirementsResult { Data = saved.Select(Map).ToList(), PageSize = saved.Count, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("contracts_", StringComparison.Ordinal)) { return Failed<ContractRequirementsResult>(ex.Message); }
		}

		[HttpGet("GetContractCompliance")]
		[Authorize(Policy = ResgridResources.ServiceContracts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ContractComplianceApiResult>> GetContractCompliance(string contractId = null, string deploymentId = null)
		{
			if (!await EnabledAsync()) return Failed<ContractComplianceApiResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var compliance = !string.IsNullOrWhiteSpace(deploymentId) ? await _contracts.GetContractComplianceAsync(deploymentId, DepartmentId) : await _contracts.GetContractComplianceForContractAsync(contractId, DepartmentId);
			if (compliance == null) return NotFound();
			var result = new ContractComplianceApiResult
			{
				Data = new ContractComplianceData
				{
					ServiceContractId = compliance.ServiceContractId, DeploymentId = compliance.DeploymentId, AllMandatorySatisfied = compliance.AllMandatorySatisfied,
					Items = compliance.Items.Select(i => new ContractComplianceItemData
					{
						RequirementId = i.ServiceContractDocumentRequirementId, Name = i.Name, Stage = i.Stage, ComplianceDocumentType = i.ComplianceDocumentType, IsMandatory = i.IsMandatory, Satisfied = i.Satisfied,
						SatisfiedBy = i.SatisfiedBy, ComplianceDocumentId = i.DepartmentComplianceDocumentId, DeploymentAttachmentId = i.DeploymentAttachmentId, ExpiresOn = i.ExpiresOn
					}).ToList()
				},
				PageSize = 1, Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#region Compliance documents

		[HttpGet("GetComplianceDocuments")]
		[Authorize(Policy = ResgridResources.ServiceContracts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ComplianceDocumentsResult>> GetComplianceDocuments()
		{
			if (!await EnabledAsync()) return Failed<ComplianceDocumentsResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			var documents = await _contracts.GetComplianceDocumentsAsync(DepartmentId);
			var result = new ComplianceDocumentsResult { Data = documents.Select(Map).ToList(), PageSize = documents.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveComplianceDocument")]
		[Authorize(Policy = ResgridResources.ServiceContracts_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ComplianceDocumentResult>> SaveComplianceDocument([FromBody] SaveComplianceDocumentInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<ComplianceDocumentResult>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			byte[] data = null;
			if (!string.IsNullOrWhiteSpace(input.FileBase64))
			{
				try { data = Convert.FromBase64String(input.FileBase64); }
				catch (FormatException) { return Failed<ComplianceDocumentResult>("compliance_file_invalid"); }
			}
			try
			{
				var saved = await _contracts.SaveComplianceDocumentAsync(new DepartmentComplianceDocument
				{
					DepartmentComplianceDocumentId = input.Id, DepartmentId = DepartmentId, DocumentType = input.DocumentType, Name = input.Name, DocumentNumber = input.DocumentNumber, Issuer = input.Issuer,
					EffectiveOn = input.EffectiveOn, ExpiresOn = input.ExpiresOn, AlertLeadDays = input.AlertLeadDays
				}, data, input.FileName, input.FileType, UserId, Ip, Agent, cancellationToken);
				var result = new ComplianceDocumentResult { Data = Map(saved), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("compliance_", StringComparison.Ordinal)) { return Failed<ComplianceDocumentResult>(ex.Message); }
		}

		[HttpGet("GetComplianceDocumentFile")]
		[Authorize(Policy = ResgridResources.ServiceContracts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetComplianceDocumentFile(int id)
		{
			if (!await EnabledAsync()) return Forbid();
			var document = await _contracts.GetComplianceDocumentAsync(id, DepartmentId, includeData: true);
			if (document == null) return NotFound();
			if (document.Data == null || document.Data.Length == 0) return NoContent();
			return File(document.Data, document.FileType ?? "application/octet-stream", string.IsNullOrWhiteSpace(document.FileName) ? $"compliance-{id}.bin" : document.FileName);
		}

		[HttpDelete("DeleteComplianceDocument")]
		[Authorize(Policy = ResgridResources.ServiceContracts_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteComplianceDocument(int id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("contractor_billing_disabled", StatusCodes.Status403Forbidden);
			if (!await _contracts.DeleteComplianceDocumentAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken)) return NotFound();
			return Empty();
		}

		#endregion

		#region Mapping

		private ActionResult<ServiceContractResult> Ok(ServiceContract contract)
		{
			var result = new ServiceContractResult { Data = Map(contract, true), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult<StandardApiResponseV4Base> Empty()
		{
			var result = new StandardApiResponseV4Base { PageSize = 0, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		internal static ServiceContractData Map(ServiceContract c, bool graph) => new ServiceContractData
		{
			Id = c.ServiceContractId, ContactId = c.ContactId, CustomerBillingProfileId = c.CustomerBillingProfileId, ContractNumber = c.ContractNumber, Name = c.Name, ContractType = c.ContractType, Status = c.Status,
			StartOn = c.StartOn, EndOn = c.EndOn, RateScheduleId = c.RateScheduleId, DiscountPercent = c.DiscountPercent, TermsNetDays = c.TermsNetDays, InvoiceSubmissionEmail = c.InvoiceSubmissionEmail,
			MaxDeploymentDays = c.MaxDeploymentDays, ResponseTimeMinutes = c.ResponseTimeMinutes, PointOfHire = c.PointOfHire, DocumentTemplateKey = c.DocumentTemplateKey, Notes = c.Notes,
			AddedOn = c.AddedOn, UpdatedOn = c.EditedOn ?? c.AddedOn,
			Requirements = graph ? (c.Requirements ?? new List<ServiceContractDocumentRequirement>()).Select(Map).ToList() : new List<ContractRequirementData>()
		};

		internal static ContractRequirementData Map(ServiceContractDocumentRequirement r) => new ContractRequirementData
		{
			Id = r.ServiceContractDocumentRequirementId, Name = r.Name, Stage = r.Stage, ComplianceDocumentType = r.ComplianceDocumentType, IsMandatory = r.IsMandatory, SortOrder = r.SortOrder
		};

		internal static ComplianceDocumentData Map(DepartmentComplianceDocument d) => new ComplianceDocumentData
		{
			Id = d.DepartmentComplianceDocumentId, DocumentType = d.DocumentType, Name = d.Name, DocumentNumber = d.DocumentNumber, Issuer = d.Issuer, EffectiveOn = d.EffectiveOn, ExpiresOn = d.ExpiresOn,
			AlertLeadDays = d.AlertLeadDays, FileName = d.FileName, FileType = d.FileType, FileSize = d.FileSize, IsCurrent = d.IsCurrent(DateTime.UtcNow),
			AddedOn = d.AddedOn, UpdatedOn = d.EditedOn ?? d.AddedOn
		};

		#endregion
	}
}

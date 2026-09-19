using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Certifications;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Certification catalog, personnel and unit records, role requirements and settings (Workforce &amp; Business
	/// Operations plan, Phase D9). Phase D is free: no add-on or flag gate. A member always sees and edits their own
	/// records; other members' records need Certifications_View / Certifications_Create / Certifications_Update;
	/// catalog, requirements and settings need Certifications_Setup. Protected values (ADP catalog 6 / 27) read
	/// REDACTED without a grant header, and file bytes never ride these endpoints.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class CertificationsController : V4AuthenticatedApiControllerbase
	{
		private readonly ICertificationService _certifications;
		private readonly IProtectedReadService _protectedRead;
		private readonly IDepartmentsService _departments;

		public CertificationsController(ICertificationService certifications, IProtectedReadService protectedRead, IDepartmentsService departments)
		{
			_certifications = certifications;
			_protectedRead = protectedRead;
			_departments = departments;
		}

		private string ProtectedGrantToken => Request.Headers[DataProtectionController.GrantHeader].ToString();
		private static bool CanViewOthers() => ClaimsAuthorizationHelper.CanViewCertifications() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanManage() => ClaimsAuthorizationHelper.CanManageCertifications() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanSetup() => ClaimsAuthorizationHelper.CanManageCertificationSetup() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		private ActionResult<T> Failed<T>(string reason) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return BadRequest(failed);
		}

		#region Types

		[HttpGet("GetCertificationTypes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationTypesResult>> GetCertificationTypes(int? appliesTo = null, bool includeInactive = false)
		{
			var types = includeInactive && CanSetup()
				? (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).Where(t => !t.IsDeleted && (!appliesTo.HasValue || t.AppliesTo == appliesTo.Value)).ToList()
				: await _certifications.GetActiveCertificationTypesAsync(DepartmentId, appliesTo.HasValue ? (CertificationAppliesTo?)appliesTo.Value : null);
			var result = new CertificationTypesResult { Data = types.Select(Map).ToList(), PageSize = types.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetCertificationTemplates")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult<CertificationTemplatesResult> GetCertificationTemplates(string query = null, int? category = null, int? appliesTo = null)
		{
			var templates = CertificationTypeTemplateCatalog.Search(query)
				.Where(t => (!category.HasValue || (int)t.Category == category.Value) && (!appliesTo.HasValue || (int)t.AppliesTo == appliesTo.Value))
				.Select(t => new CertificationTemplateData { Id = t.Id, Code = t.Code, Name = t.Name, Category = (int)t.Category, AppliesTo = (int)t.AppliesTo, IssuingAuthority = t.IssuingAuthority, DefaultValidityMonths = t.DefaultValidityMonths, NeverExpires = t.NeverExpires, RequiresVerification = t.RequiresVerification, RenewalCreditHoursRequired = t.RenewalCreditHoursRequired, Description = t.Description })
				.ToList();
			var result = new CertificationTemplatesResult { Data = templates, PageSize = templates.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveCertificationType")]
		[Authorize(Policy = ResgridResources.Certifications_Setup)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationTypeResult>> SaveCertificationType([FromBody] SaveCertificationTypeInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			try
			{
				var existing = input.Id > 0 ? await _certifications.GetCertificationTypeByIdAsync(input.Id) : null;
				if (input.Id > 0 && (existing == null || existing.DepartmentId != DepartmentId)) return NotFound();
				var type = existing ?? new DepartmentCertificationType { DepartmentId = DepartmentId };
				type.Type = input.Name; type.Code = input.Code; type.Category = input.Category; type.AppliesTo = input.AppliesTo; type.Description = input.Description; type.IssuingAuthority = input.IssuingAuthority;
				type.DefaultValidityMonths = input.DefaultValidityMonths; type.NeverExpires = input.NeverExpires; type.RenewalCreditHoursRequired = input.RenewalCreditHoursRequired; type.RequiresVerification = input.RequiresVerification; type.IsActive = input.IsActive;
				var saved = await _certifications.SaveCertificationTypeAsync(type, UserId, cancellationToken);
				var result = new CertificationTypeResult { Data = Map(saved), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationTypeResult>(ex.Message); }
		}

		[HttpPost("CreateCertificationTypeFromTemplate")]
		[Authorize(Policy = ResgridResources.Certifications_Setup)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationTypeResult>> CreateCertificationTypeFromTemplate(string templateId, CancellationToken cancellationToken)
		{
			try
			{
				var saved = await _certifications.CreateCertificationTypeFromTemplateAsync(DepartmentId, templateId, UserId, cancellationToken);
				var result = new CertificationTypeResult { Data = Map(saved), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationTypeResult>(ex.Message); }
		}

		[HttpDelete("DeleteCertificationType")]
		[Authorize(Policy = ResgridResources.Certifications_Setup)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationTypeResult>> DeleteCertificationType(int id, CancellationToken cancellationToken)
		{
			var type = await _certifications.GetCertificationTypeByIdAsync(id);
			if (type == null || type.DepartmentId != DepartmentId) return NotFound();
			try
			{
				await _certifications.DeleteCertificationTypeByIdAsync(id, cancellationToken);
				var result = new CertificationTypeResult { Data = Map(type), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationTypeResult>(ex.Message); }
		}

		#endregion

		#region Personnel records

		/// <summary>The caller's records, or another member's with Certifications_View.</summary>
		[HttpGet("GetUserCertifications")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationsResult>> GetUserCertifications(string userId = null)
		{
			var subject = string.IsNullOrWhiteSpace(userId) ? UserId : userId;
			if (subject != UserId && !CanViewOthers()) return Unauthorized();
			var records = await _certifications.GetCertificationsForDepartmentAsync(DepartmentId, new[] { subject });
			var data = await MapRecordsAsync(records);
			var result = new CertificationsResult { Data = data, PageSize = data.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetCertification")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationResult>> GetCertification(int id)
		{
			var record = await _certifications.GetCertificationByIdAsync(id);
			if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId) return NotFound();
			if (record.UserId != UserId && !CanViewOthers()) return Unauthorized();
			record.Data = null;
			var data = (await MapRecordsAsync(new List<PersonnelCertification> { record })).First();
			var result = new CertificationResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveCertification")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationResult>> SaveCertification([FromBody] SaveCertificationInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var subject = string.IsNullOrWhiteSpace(input.UserId) ? UserId : input.UserId;
			if (subject != UserId && !CanManage()) return Unauthorized();
			try
			{
				PersonnelCertification record;
				if (input.Id > 0)
				{
					record = await _certifications.GetCertificationByIdAsync(input.Id);
					if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId) return NotFound();
					if (record.UserId != UserId && !CanManage()) return Unauthorized();
				}
				else
					record = new PersonnelCertification { DepartmentId = DepartmentId, UserId = subject, Status = (int)PersonnelCertificationStatuses.Active };

				record.DepartmentCertificationTypeId = input.TypeId > 0 ? input.TypeId : null;
				record.Name = input.Name; record.Number = input.Number; record.Type = input.Type; record.Area = input.Area; record.IssuedBy = input.IssuedBy;
				record.ExpiresOn = input.ExpiresOn; record.RecievedOn = input.ReceivedOn;
				if (!string.IsNullOrWhiteSpace(input.FileData))
				{
					record.Data = Convert.FromBase64String(input.FileData);
					record.Filename = input.FileName;
					record.Filetype = input.FileType;
				}
				if (string.IsNullOrWhiteSpace(record.Name))
				{
					var type = record.DepartmentCertificationTypeId.HasValue ? await _certifications.GetCertificationTypeByIdAsync(record.DepartmentCertificationTypeId.Value) : null;
					record.Name = type?.Type ?? record.Type ?? "Certification";
				}
				var saved = await _certifications.SaveCertificationAsync(record, cancellationToken);
				return await GetCertification(saved.PersonnelCertificationId);
			}
			catch (FormatException) { return Failed<CertificationResult>("certifications_file_invalid"); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationResult>(ex.Message); }
		}

		[HttpDelete("DeleteCertification")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationResult>> DeleteCertification(int id, CancellationToken cancellationToken)
		{
			var record = await _certifications.GetCertificationByIdAsync(id);
			if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId) return NotFound();
			if (record.UserId != UserId && !CanManage()) return Unauthorized();
			try
			{
				await _certifications.SoftDeleteCertificationAsync(id, DepartmentId, UserId, cancellationToken);
				var result = new CertificationResult { PageSize = 0, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationResult>(ex.Message); }
		}

		[HttpPost("SetCertificationStatus")]
		[Authorize(Policy = ResgridResources.Certifications_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationResult>> SetCertificationStatus([FromBody] SetCertificationStatusInput input, CancellationToken cancellationToken)
		{
			if (input == null || !Enum.IsDefined(typeof(PersonnelCertificationStatuses), input.Status)) return BadRequest();
			try
			{
				var saved = await _certifications.SetCertificationStatusAsync(input.Id, DepartmentId, (PersonnelCertificationStatuses)input.Status, input.Reason, UserId, cancellationToken);
				return await GetCertification(saved.PersonnelCertificationId);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationResult>(ex.Message); }
		}

		[HttpPost("VerifyCertification")]
		[Authorize(Policy = ResgridResources.Certifications_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationResult>> VerifyCertification(int id, CancellationToken cancellationToken)
		{
			try
			{
				var saved = await _certifications.VerifyCertificationAsync(id, DepartmentId, UserId, cancellationToken);
				return await GetCertification(saved.PersonnelCertificationId);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationResult>(ex.Message); }
		}

		[HttpPost("RenewCertification")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationResult>> RenewCertification([FromBody] RenewCertificationInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var record = await _certifications.GetCertificationByIdAsync(input.Id);
			if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId) return NotFound();
			if (record.UserId != UserId && !CanManage()) return Unauthorized();
			try
			{
				var saved = await _certifications.RenewCertificationAsync(input.Id, DepartmentId, input.ExpiresOn, input.Number, UserId, cancellationToken);
				return await GetCertification(saved.PersonnelCertificationId);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationResult>(ex.Message); }
		}

		#endregion

		#region Credits

		[HttpGet("GetCertificationCredits")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationCreditsResult>> GetCertificationCredits(int certificationId)
		{
			var record = await _certifications.GetCertificationByIdAsync(certificationId);
			if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId) return NotFound();
			if (record.UserId != UserId && !CanViewOthers()) return Unauthorized();
			var credits = await _certifications.GetCertificationCreditsAsync(certificationId);
			var data = credits.Select(c => new CertificationCreditData { Id = c.PersonnelCertificationCreditId, CertificationId = c.PersonnelCertificationId, CreditDate = c.CreditDate, Hours = c.Hours, Category = c.Category, Description = c.Description, HasFile = !string.IsNullOrWhiteSpace(c.FileName), UpdatedOn = c.AddedOn }).ToList();
			var result = new CertificationCreditsResult { Data = data, PageSize = data.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("AddCertificationCredit")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationCreditsResult>> AddCertificationCredit([FromBody] AddCertificationCreditInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var record = await _certifications.GetCertificationByIdAsync(input.CertificationId);
			if (record == null || record.IsDeleted || record.DepartmentId != DepartmentId) return NotFound();
			if (record.UserId != UserId && !CanManage()) return Unauthorized();
			try
			{
				var credit = new PersonnelCertificationCredit { PersonnelCertificationId = input.CertificationId, DepartmentId = DepartmentId, CreditDate = input.CreditDate ?? DateTime.UtcNow.Date, Hours = input.Hours, Category = input.Category, Description = input.Description, FileName = input.FileName, FileType = input.FileType };
				if (!string.IsNullOrWhiteSpace(input.FileData)) credit.Data = Convert.FromBase64String(input.FileData);
				await _certifications.AddCertificationCreditAsync(credit, UserId, cancellationToken);
				return await GetCertificationCredits(input.CertificationId);
			}
			catch (FormatException) { return Failed<CertificationCreditsResult>("certifications_file_invalid"); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationCreditsResult>(ex.Message); }
		}

		[HttpDelete("DeleteCertificationCredit")]
		[Authorize(Policy = ResgridResources.Certifications_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationCreditsResult>> DeleteCertificationCredit(int id, CancellationToken cancellationToken)
		{
			try
			{
				var credit = await _certifications.GetCertificationCreditByIdAsync(id);
				if (credit == null || credit.DepartmentId != DepartmentId) return NotFound();
				await _certifications.DeleteCertificationCreditAsync(id, DepartmentId, UserId, cancellationToken);
				return await GetCertificationCredits(credit.PersonnelCertificationId);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationCreditsResult>(ex.Message); }
		}

		#endregion

		#region Expiring and units

		/// <summary>Person and unit records expiring within <paramref name="days"/> (or already expired), typed discriminator on each row.</summary>
		[HttpGet("GetExpiringCertifications")]
		[Authorize(Policy = ResgridResources.Certifications_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ExpiringCertificationsResult>> GetExpiringCertifications(int days = 60)
		{
			var horizon = Math.Clamp(days, 0, 3650);
			var dashboard = await _certifications.GetExpiryDashboardAsync(DepartmentId);
			var rows = dashboard.PersonCells.Select(c => Map("Person", c)).Concat(dashboard.UnitCells.Select(c => Map("Unit", c)))
				.Where(r => r.DaysUntilExpiry.HasValue && r.DaysUntilExpiry.Value <= horizon)
				.OrderBy(r => r.DaysUntilExpiry).ThenBy(r => r.SubjectName).ToList();
			var result = new ExpiringCertificationsResult { Data = rows, PageSize = rows.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetUnitCertifications")]
		[Authorize(Policy = ResgridResources.Certifications_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<UnitCertificationsResult>> GetUnitCertifications(int unitId)
		{
			var rows = await _certifications.GetUnitCertificationsAsync(unitId);
			if (rows.Any(r => r.DepartmentId != DepartmentId)) return Unauthorized();
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ToDictionary(t => t.DepartmentCertificationTypeId);
			var data = rows.Select(r => Map(r, types)).ToList();
			var result = new UnitCertificationsResult { Data = data, PageSize = data.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveUnitCertification")]
		[Authorize(Policy = ResgridResources.Certifications_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<UnitCertificationResult>> SaveUnitCertification([FromBody] SaveUnitCertificationInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			try
			{
				var row = new UnitCertification { UnitCertificationId = input.Id, UnitId = input.UnitId, DepartmentId = DepartmentId, DepartmentCertificationTypeId = input.TypeId, Number = input.Number, IssuedBy = input.IssuedBy, IssuedOn = input.IssuedOn, ExpiresOn = input.ExpiresOn, Notes = input.Notes, FileName = input.FileName, FileType = input.FileType };
				if (!string.IsNullOrWhiteSpace(input.FileData)) row.Data = Convert.FromBase64String(input.FileData);
				var saved = await _certifications.SaveUnitCertificationAsync(row, UserId, cancellationToken);
				var reloaded = await _certifications.GetUnitCertificationByIdAsync(saved.UnitCertificationId) ?? saved;
				var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ToDictionary(t => t.DepartmentCertificationTypeId);
				var result = new UnitCertificationResult { Data = Map(reloaded, types), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (FormatException) { return Failed<UnitCertificationResult>("certifications_file_invalid"); }
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<UnitCertificationResult>(ex.Message); }
		}

		[HttpPost("SetUnitCertificationStatus")]
		[Authorize(Policy = ResgridResources.Certifications_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<UnitCertificationResult>> SetUnitCertificationStatus([FromBody] SetUnitCertificationStatusInput input, CancellationToken cancellationToken)
		{
			if (input == null || !Enum.IsDefined(typeof(UnitCertificationStatuses), input.Status)) return BadRequest();
			try
			{
				var saved = await _certifications.SetUnitCertificationStatusAsync(input.Id, DepartmentId, (UnitCertificationStatuses)input.Status, input.Reason, UserId, cancellationToken);
				var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ToDictionary(t => t.DepartmentCertificationTypeId);
				var result = new UnitCertificationResult { Data = Map(saved, types), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<UnitCertificationResult>(ex.Message); }
		}

		[HttpDelete("DeleteUnitCertification")]
		[Authorize(Policy = ResgridResources.Certifications_Delete)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<UnitCertificationResult>> DeleteUnitCertification(int id, CancellationToken cancellationToken)
		{
			try
			{
				await _certifications.DeleteUnitCertificationAsync(id, DepartmentId, UserId, cancellationToken);
				var result = new UnitCertificationResult { PageSize = 0, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<UnitCertificationResult>(ex.Message); }
		}

		#endregion

		#region Requirements, settings, eligibility

		[HttpGet("GetRoleRequirements")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RoleRequirementsResult>> GetRoleRequirements(int roleId)
		{
			var rows = await _certifications.GetRoleRequirementsAsync(roleId);
			if (rows.Any(r => r.DepartmentId != DepartmentId)) return Unauthorized();
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ToDictionary(t => t.DepartmentCertificationTypeId);
			var data = rows.Select(r => Map(r, types)).ToList();
			var result = new RoleRequirementsResult { Data = data, PageSize = data.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveRoleRequirements")]
		[Authorize(Policy = ResgridResources.Certifications_Setup)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<RoleRequirementsResult>> SaveRoleRequirements([FromBody] SaveRoleRequirementsInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.RoleId <= 0) return BadRequest();
			try
			{
				var rows = (input.Requirements ?? new List<RoleRequirementInput>()).Select(r => new PersonnelRoleCertificationRequirement { PersonnelRoleId = input.RoleId, DepartmentId = DepartmentId, DepartmentCertificationTypeId = r.TypeId, IsMandatory = r.IsMandatory, AnyOfGroup = r.AnyOfGroup, AllowTrainee = r.AllowTrainee, GraceDaysOverride = r.GraceDaysOverride }).ToList();
				await _certifications.SaveRoleRequirementsAsync(DepartmentId, input.RoleId, rows, UserId, cancellationToken);
				return await GetRoleRequirements(input.RoleId);
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<RoleRequirementsResult>(ex.Message); }
		}

		[HttpGet("GetCertificationSettings")]
		[Authorize(Policy = ResgridResources.Certifications_Setup)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CertificationSettingsResult>> GetCertificationSettings()
		{
			var settings = await _certifications.GetCertificationSettingsAsync(DepartmentId);
			var result = new CertificationSettingsResult { Data = Map(settings), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveCertificationSettings")]
		[Authorize(Policy = ResgridResources.Certifications_Setup)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<CertificationSettingsResult>> SaveCertificationSettings([FromBody] CertificationSettingsData input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			try
			{
				var saved = await _certifications.SaveCertificationSettingsAsync(new DepartmentCertificationSettings
				{
					DepartmentId = DepartmentId, EnforcementMode = input.EnforcementMode, RoleRemovalGraceDays = input.RoleRemovalGraceDays, NotifyLeadDaysCsv = input.NotifyLeadDaysCsv,
					NotifyCertificationHolder = input.NotifyCertificationHolder, TreatPendingVerificationAsValid = input.TreatPendingVerificationAsValid, SendAdminDigest = input.SendAdminDigest
				}, UserId, cancellationToken);
				var result = new CertificationSettingsResult { Data = Map(saved), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (ex.Message.StartsWith("certifications_", StringComparison.Ordinal)) { return Failed<CertificationSettingsResult>(ex.Message); }
		}

		/// <summary>Every current member of the role evaluated against its requirements (D1.7).</summary>
		[HttpGet("GetRoleEligibility")]
		[Authorize(Policy = ResgridResources.Certifications_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<RoleEligibilityResult>> GetRoleEligibility(int roleId)
		{
			var evaluations = await _certifications.EvaluateRoleRequirementsAsync(DepartmentId, roleId);
			var data = evaluations.Select(e => new RoleEligibilityData
			{
				UserId = e.UserId, Qualified = e.Qualified, WarningsOnly = e.WarningsOnly, RemovalDueOn = e.RemovalDueOn,
				Violations = e.Violations.Select(v => new RoleRequirementOutcomeData { TypeId = v.DepartmentCertificationTypeId, TypeCode = v.TypeCode, TypeName = v.TypeName, IsMandatory = v.IsMandatory, AnyOfGroup = v.AnyOfGroup, ExpiresOn = v.ExpiresOn, ViolationStartedOn = v.ViolationStartedOn, GraceDays = v.GraceDays }).ToList()
			}).ToList();
			var result = new RoleEligibilityResult { Data = data, PageSize = data.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Mapping

		private static CertificationTypeData Map(DepartmentCertificationType t) => new CertificationTypeData
		{
			Id = t.DepartmentCertificationTypeId, Code = t.Code, Name = t.Type, Category = t.Category, AppliesTo = t.AppliesTo, Description = t.Description, IssuingAuthority = t.IssuingAuthority,
			DefaultValidityMonths = t.DefaultValidityMonths, NeverExpires = t.NeverExpires, RenewalCreditHoursRequired = t.RenewalCreditHoursRequired, RequiresVerification = t.RequiresVerification, IsActive = t.IsActive, UpdatedOn = t.EditedOn ?? t.AddedOn
		};

		private async Task<List<CertificationData>> MapRecordsAsync(List<PersonnelCertification> records)
		{
			if (records.Count == 0) return new List<CertificationData>();
			try { await _protectedRead.ResolveCertificationsForReadAsync(DepartmentId, records, ProtectedGrantToken, UserId); }
			catch (Exception ex) { Framework.Logging.LogException(ex, "Protected certification rows could not be resolved for the API."); }
			var types = (await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId)).ToDictionary(t => t.DepartmentCertificationTypeId);
			var totals = await _certifications.GetCertificationCreditTotalsAsync(records.Select(r => r.PersonnelCertificationId));
			var today = DateTime.UtcNow.Date;
			return records.Select(r =>
			{
				DepartmentCertificationType type = null;
				if (r.DepartmentCertificationTypeId.HasValue) types.TryGetValue(r.DepartmentCertificationTypeId.Value, out type);
				return new CertificationData
				{
					Id = r.PersonnelCertificationId, UserId = r.UserId, TypeId = r.DepartmentCertificationTypeId, TypeCode = type?.Code, TypeName = type?.Type,
					Name = r.Name, Number = r.Number, Type = r.Type, Area = r.Area, IssuedBy = r.IssuedBy, ExpiresOn = r.ExpiresOn, ReceivedOn = r.RecievedOn,
					Status = r.Status, StatusReason = r.StatusReason, VerifiedOn = r.VerifiedOn, VerifiedByUserId = r.VerifiedByUserId,
					DaysUntilExpiry = CertificationRequirementEvaluator.DaysUntilExpiry(r.ExpiresOn, type?.NeverExpires == true, today),
					HasFile = !string.IsNullOrWhiteSpace(r.Filename), CreditHours = totals.TryGetValue(r.PersonnelCertificationId, out var hours) ? hours : 0m, CreditHoursRequired = type?.RenewalCreditHoursRequired,
					IsProtected = r.IsProtected, UpdatedOn = r.StatusChangedOn ?? r.VerifiedOn ?? r.RecievedOn
				};
			}).ToList();
		}

		private static ExpiringCertificationData Map(string scope, CertificationDashboardCell c) => new ExpiringCertificationData
		{
			Scope = scope, RecordId = c.RecordId, SubjectId = c.SubjectId, SubjectName = c.SubjectName, TypeId = c.DepartmentCertificationTypeId, TypeCode = c.TypeCode, TypeName = c.TypeName,
			Category = c.Category, Status = c.Status, ExpiresOn = c.ExpiresOn, DaysUntilExpiry = c.DaysUntilExpiry
		};

		private static UnitCertificationData Map(UnitCertification u, IReadOnlyDictionary<int, DepartmentCertificationType> types)
		{
			types.TryGetValue(u.DepartmentCertificationTypeId, out var type);
			return new UnitCertificationData
			{
				Id = u.UnitCertificationId, UnitId = u.UnitId, TypeId = u.DepartmentCertificationTypeId, TypeCode = type?.Code, TypeName = type?.Type, Number = u.Number, IssuedBy = u.IssuedBy, IssuedOn = u.IssuedOn, ExpiresOn = u.ExpiresOn,
				Status = u.Status, StatusReason = u.StatusReason, Notes = u.Notes, HasFile = !string.IsNullOrWhiteSpace(u.FileName) || u.FileSize > 0, FileName = u.FileName,
				DaysUntilExpiry = CertificationRequirementEvaluator.DaysUntilExpiry(u.ExpiresOn, type?.NeverExpires == true, DateTime.UtcNow.Date), IsProtected = u.IsProtected, UpdatedOn = u.EditedOn ?? u.AddedOn
			};
		}

		private static RoleRequirementData Map(PersonnelRoleCertificationRequirement r, IReadOnlyDictionary<int, DepartmentCertificationType> types)
		{
			types.TryGetValue(r.DepartmentCertificationTypeId, out var type);
			return new RoleRequirementData { Id = r.PersonnelRoleCertificationRequirementId, RoleId = r.PersonnelRoleId, TypeId = r.DepartmentCertificationTypeId, TypeCode = type?.Code, TypeName = type?.Type, IsMandatory = r.IsMandatory, AnyOfGroup = r.AnyOfGroup, AllowTrainee = r.AllowTrainee, GraceDaysOverride = r.GraceDaysOverride };
		}

		private static CertificationSettingsData Map(DepartmentCertificationSettings s) => new CertificationSettingsData
		{
			EnforcementMode = s.EnforcementMode, RoleRemovalGraceDays = s.RoleRemovalGraceDays, NotifyLeadDaysCsv = s.NotifyLeadDaysCsv, NotifyCertificationHolder = s.NotifyCertificationHolder,
			TreatPendingVerificationAsValid = s.TreatPendingVerificationAsValid, SendAdminDigest = s.SendAdminDigest, UpdatedOn = s.UpdatedOn == default ? null : s.UpdatedOn
		};

		#endregion
	}
}

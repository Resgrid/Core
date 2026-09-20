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
	/// Daily time reports, entries and expenses (Workforce &amp; Business Operations plan, Phase C5). Mobile crews file
	/// DTRs from the field: a rostered member may create, edit, sign and submit reports and expenses on their own
	/// deployments without any new claim; approval and void need TimeReports_Approve. Receipts upload as base64 and
	/// are stored as Receipt attachments; DTOs carry UpdatedOn for delta-sync.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class TimeReportsController : V4AuthenticatedApiControllerbase
	{
		private readonly ITimeTrackingService _timeTracking;
		private readonly IDeploymentService _deployments;
		private readonly IFeatureToggleService _flags;

		public TimeReportsController(ITimeTrackingService timeTracking, IDeploymentService deployments, IFeatureToggleService flags)
		{
			_timeTracking = timeTracking;
			_deployments = deployments;
			_flags = flags;
		}

		private Task<bool> EnabledAsync() => _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId);
		private static bool CanApprove() => ClaimsAuthorizationHelper.CanApproveTimeReports() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private async Task<bool> CanTouchAsync(string deploymentId) => DeploymentsController.CanManage() || await _deployments.IsRosteredAsync(deploymentId, DepartmentId, UserId);
		private async Task<bool> CanSeeAsync(string deploymentId) => DeploymentsController.CanView() || await _deployments.IsRosteredAsync(deploymentId, DepartmentId, UserId);

		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";

		private ActionResult<T> Failed<T>(string reason, int status = StatusCodes.Status400BadRequest) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return StatusCode(status, failed);
		}

		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("timereports_", StringComparison.Ordinal) || ex.Message.StartsWith("deployments_", StringComparison.Ordinal) || ex.Message.StartsWith("expenses_", StringComparison.Ordinal);

		#region Reports

		[HttpGet("GetTimeReports")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportsResult>> GetTimeReports(string deploymentId)
		{
			if (!await EnabledAsync()) return Failed<TimeReportsResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (!await CanSeeAsync(deploymentId)) return Unauthorized();
			var reports = await _timeTracking.GetTimeReportsAsync(deploymentId, DepartmentId);
			var result = new TimeReportsResult { Data = reports.Select(r => Map(r)).ToList(), PageSize = reports.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> GetTimeReport(string id)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			if (!await CanSeeAsync(report.DeploymentId)) return Unauthorized();
			return Ok(report);
		}

		[HttpPost("NewTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> NewTimeReport([FromBody] NewTimeReportInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return BadRequest();
			if (!await CanTouchAsync(input.DeploymentId)) return Unauthorized();
			try { return Ok(await _timeTracking.CreateTimeReportAsync(input.DeploymentId, DepartmentId, input.ReportDate, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("UpdateTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> UpdateTimeReport([FromBody] UpdateTimeReportInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			var report = await _timeTracking.GetTimeReportByIdAsync(input.Id, DepartmentId);
			if (report == null) return NotFound();
			if (!await CanTouchAsync(report.DeploymentId)) return Unauthorized();
			report.IncidentNumber = input.IncidentNumber; report.ResourceOrderNumber = input.ResourceOrderNumber; report.RequestNumber = input.RequestNumber; report.CostCode = input.CostCode; report.PointOfHire = input.PointOfHire;
			report.NoClear8 = input.NoClear8; report.UnsafeConditionsStandDown = input.UnsafeConditionsStandDown; report.Notes = input.Notes; report.RmsExternalOrderFillId = input.RmsExternalOrderFillId;
			try { return Ok(await _timeTracking.UpdateTimeReportAsync(report, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		/// <summary>Replaces the report's entries as a batch. Validation errors come back with the unchanged report and status Failure.</summary>
		[HttpPost("SaveTimeEntries")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> SaveTimeEntries([FromBody] SaveTimeEntriesInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			var report = await _timeTracking.GetTimeReportByIdAsync(input.TimeReportId, DepartmentId);
			if (report == null) return NotFound();
			if (!await CanTouchAsync(report.DeploymentId)) return Unauthorized();
			try
			{
				var entries = (input.Entries ?? new List<TimeEntryData>()).Select(e => new DeploymentTimeEntry
				{
					DeploymentTimeEntryId = string.IsNullOrWhiteSpace(e.Id) ? null : e.Id,
					DeploymentPersonnelId = e.DeploymentPersonnelId, DeploymentUnitId = e.DeploymentUnitId, DeploymentEquipmentId = e.DeploymentEquipmentId, EntryType = e.EntryType, StartTime = e.StartTime, EndTime = e.EndTime,
					PaidBreakMinutes = e.PaidBreakMinutes, UnpaidBreakMinutes = e.UnpaidBreakMinutes, CrewSizeSnapshot = e.CrewSizeSnapshot, CertificationCode = e.CertificationCode, MileageKm = e.MileageKm, FuelDeductionLitres = e.FuelDeductionLitres,
					AgencySuppliedMeals = e.AgencySuppliedMeals, AgencySuppliedAccommodation = e.AgencySuppliedAccommodation, Notes = e.Notes, SortOrder = e.SortOrder
				}).ToList();
				return Ok(await _timeTracking.SaveTimeEntriesAsync(input.TimeReportId, DepartmentId, entries, UserId, Ip, Agent, cancellationToken));
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("SubmitTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> SubmitTimeReport([FromBody] TimeReportActionInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			var report = await _timeTracking.GetTimeReportByIdAsync(input.Id, DepartmentId);
			if (report == null) return NotFound();
			if (!await CanTouchAsync(report.DeploymentId)) return Unauthorized();
			try { return Ok(await _timeTracking.SubmitTimeReportAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("ApproveTimeReport")]
		[Authorize(Policy = ResgridResources.TimeReports_Approve)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> ApproveTimeReport([FromBody] TimeReportActionInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try { return Ok(await _timeTracking.ApproveTimeReportAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("VoidTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> VoidTimeReport([FromBody] TimeReportActionInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			if (!CanApprove()) return Unauthorized();
			try { return Ok(await _timeTracking.VoidTimeReportAsync(input.Id, DepartmentId, input.Reason, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("SignTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> SignTimeReport([FromBody] SignTimeReportInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			var report = await _timeTracking.GetTimeReportByIdAsync(input.Id, DepartmentId);
			if (report == null) return NotFound();
			if (!await CanTouchAsync(report.DeploymentId)) return Unauthorized();
			try { return Ok(await _timeTracking.SignTimeReportAsync(input.Id, DepartmentId, input.ContractorSigned, input.CustomerSignerName, UserId, Ip, Agent, cancellationToken)); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpGet("GetTimeReportPdf")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetTimeReportPdf(string id)
		{
			if (!await EnabledAsync()) return StatusCode(StatusCodes.Status403Forbidden);
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			if (!await CanSeeAsync(report.DeploymentId)) return Unauthorized();
			try
			{
				var pdf = await _timeTracking.GetTimeReportPdfAsync(id, DepartmentId);
				return File(pdf, "application/pdf", $"dtr-{report.ReportNumber}-{report.ReportDate:yyyyMMdd}.pdf");
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return BadRequest(ex.Message); }
		}

		#endregion

		#region Expenses

		[HttpGet("GetExpenses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ExpensesResult>> GetExpenses(string deploymentId)
		{
			if (!await EnabledAsync()) return Failed<ExpensesResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (!await CanSeeAsync(deploymentId)) return Unauthorized();
			var rows = await _timeTracking.GetExpensesAsync(deploymentId, DepartmentId);
			var result = new ExpensesResult { Data = rows.Select(MapExpense).ToList(), PageSize = rows.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveExpense")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ExpenseResult>> SaveExpense([FromBody] SaveExpenseInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<ExpenseResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return BadRequest();
			if (!await CanTouchAsync(input.DeploymentId)) return Unauthorized();
			byte[] receipt = null;
			if (!string.IsNullOrWhiteSpace(input.ReceiptData))
			{
				try { receipt = Convert.FromBase64String(input.ReceiptData); }
				catch (FormatException) { return Failed<ExpenseResult>("expenses_receipt_invalid"); }
				if (receipt.Length > Resgrid.Services.Invoicing.DeploymentService.MaxAttachmentBytes) return Failed<ExpenseResult>("deployments_attachment_too_large");
			}
			try
			{
				var expense = new DeploymentExpense
				{
					DeploymentExpenseId = input.Id, DeploymentId = input.DeploymentId, DeploymentTimeReportId = input.TimeReportId, DepartmentId = DepartmentId, ExpenseDate = input.ExpenseDate ?? DateTime.UtcNow.Date,
					ExpenseType = input.ExpenseType, MealCode = input.MealCode, City = input.City, Description = input.Description, Amount = input.Amount, Currency = input.Currency, PreApproved = input.PreApproved, Billable = input.Billable
				};
				var saved = await _timeTracking.SaveExpenseAsync(expense, receipt, input.ReceiptFileName, input.ReceiptContentType, UserId, Ip, Agent, cancellationToken);
				var result = new ExpenseResult { Data = MapExpense(saved), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<ExpenseResult>(ex.Message); }
		}

		[HttpDelete("DeleteExpense")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StandardApiResponseV4Base>> DeleteExpense(string id, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<StandardApiResponseV4Base>("deployments_disabled", StatusCodes.Status403Forbidden);
			var expense = await _timeTracking.GetExpenseByIdAsync(id, DepartmentId);
			if (expense == null) return NotFound();
			if (!await CanTouchAsync(expense.DeploymentId)) return Unauthorized();
			try
			{
				await _timeTracking.DeleteExpenseAsync(id, DepartmentId, UserId, Ip, Agent, cancellationToken);
				var result = new StandardApiResponseV4Base { PageSize = 0, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<StandardApiResponseV4Base>(ex.Message); }
		}

		#endregion

		#region Mapping

		private ActionResult<TimeReportResult> Ok(DeploymentTimeReport report)
		{
			var result = new TimeReportResult { Data = Map(report), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult<TimeReportResult> Ok(TimeReportSaveResult save)
		{
			var result = new TimeReportResult
			{
				Data = save.Report == null ? null : Map(save.Report),
				Errors = save.Validation.Errors.Select(MapIssue).ToList(),
				Warnings = save.Validation.Warnings.Select(MapIssue).ToList(),
				PageSize = 1,
				Status = save.Validation.IsValid ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			if (!save.Validation.IsValid) Response.Headers["X-Resgrid-Reason"] = "timereports_validation";
			return result;
		}

		internal static TimeReportData Map(DeploymentTimeReport r) => new TimeReportData
		{
			Id = r.DeploymentTimeReportId, DeploymentId = r.DeploymentId, ReportNumber = r.ReportNumber, ReportDate = r.ReportDate, Status = r.Status, IncidentNumber = r.IncidentNumber, ResourceOrderNumber = r.ResourceOrderNumber,
			RequestNumber = r.RequestNumber, CostCode = r.CostCode, PointOfHire = r.PointOfHire, NoClear8 = r.NoClear8, UnsafeConditionsStandDown = r.UnsafeConditionsStandDown, ContractorSignedByUserId = r.ContractorSignedByUserId,
			ContractorSignedOn = r.ContractorSignedOn, CustomerSignerName = r.CustomerSignerName, CustomerSignedOn = r.CustomerSignedOn, SubmittedByUserId = r.SubmittedByUserId, SubmittedOn = r.SubmittedOn,
			ApprovedByUserId = r.ApprovedByUserId, ApprovedOn = r.ApprovedOn, InvoiceId = r.InvoiceId, RmsExternalOrderFillId = r.RmsExternalOrderFillId, Notes = r.Notes, AddedOn = r.AddedOn, UpdatedOn = r.EditedOn ?? r.AddedOn,
			Entries = r.Entries.Select(e => new TimeEntryData
			{
				Id = e.DeploymentTimeEntryId, SubjectType = e.SubjectType, DeploymentPersonnelId = e.DeploymentPersonnelId, DeploymentUnitId = e.DeploymentUnitId, DeploymentEquipmentId = e.DeploymentEquipmentId, EntryType = e.EntryType,
				StartTime = e.StartTime, EndTime = e.EndTime, PaidBreakMinutes = e.PaidBreakMinutes, UnpaidBreakMinutes = e.UnpaidBreakMinutes, CrewSizeSnapshot = e.CrewSizeSnapshot, CertificationCode = e.CertificationCode, MileageKm = e.MileageKm,
				FuelDeductionLitres = e.FuelDeductionLitres, AgencySuppliedMeals = e.AgencySuppliedMeals, AgencySuppliedAccommodation = e.AgencySuppliedAccommodation, Notes = e.Notes, SortOrder = e.SortOrder, Hours = e.Hours
			}).ToList()
		};

		internal static TimeReportIssueData MapIssue(TimeReportIssue i) => new TimeReportIssueData { Code = i.Code, SubjectId = i.SubjectId, EntryId = i.EntryId, Detail = i.Detail };

		internal static ExpenseData MapExpense(DeploymentExpense e) => new ExpenseData
		{
			Id = e.DeploymentExpenseId, DeploymentId = e.DeploymentId, TimeReportId = e.DeploymentTimeReportId, ExpenseDate = e.ExpenseDate, ExpenseType = e.ExpenseType, MealCode = e.MealCode, City = e.City,
			Description = e.Description, Amount = e.Amount, Currency = e.Currency, PreApproved = e.PreApproved, Billable = e.Billable, ReceiptAttachmentId = e.ReceiptAttachmentId,
			AddedOn = e.AddedOn, UpdatedOn = e.EditedOn ?? e.AddedOn
		};

		#endregion
	}
}

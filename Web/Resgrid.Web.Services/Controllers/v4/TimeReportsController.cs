using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Helpers;
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
	/// Daily time reports, entries and expenses (Workforce &amp; Business Operations plan, Phase C5). Mobile crews file from the
	/// field without any new claim: a member writes their own roster row (individual report) and, for every deployed unit they
	/// crew (on the deployment roster for that unit, or seated on the apparatus through an active unit role), that unit's Crew
	/// Time Report — the unit, its crew and its equipment (M0227). Entries of subjects a caller may not write are kept as stored.
	/// Approval and void need TimeReports_Approve. Entry times travel as UTC instants plus department-local wall clock
	/// (StartLocal/EndLocal) so field apps never do zone math. Receipts upload as base64; DTOs carry UpdatedOn for delta-sync.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class TimeReportsController : V4AuthenticatedApiControllerbase
	{
		private const string LocalClockFormat = "yyyy-MM-dd'T'HH:mm";
		private static readonly string[] LocalClockFormats = { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm" };

		private readonly ITimeTrackingService _timeTracking;
		private readonly IDeploymentService _deployments;
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentsService _departments;

		public TimeReportsController(ITimeTrackingService timeTracking, IDeploymentService deployments, IFeatureToggleService flags, IDepartmentsService departments)
		{
			_timeTracking = timeTracking;
			_deployments = deployments;
			_flags = flags;
			_departments = departments;
		}

		private Task<bool> EnabledAsync() => _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId);
		private static bool CanApprove() => ClaimsAuthorizationHelper.CanApproveTimeReports() || ClaimsAuthorizationHelper.IsUserDepartmentAdmin();

		/// <summary>The deployment and the caller's time scope on it; both null when the deployment is not in this department.</summary>
		private async Task<(Deployment Deployment, DeploymentTimeAccess Access)> AccessAsync(string deploymentId)
		{
			var deployment = await _deployments.GetDeploymentByIdAsync(deploymentId, DepartmentId);
			if (deployment == null) return (null, null);
			return (deployment, await _deployments.GetTimeAccessAsync(deployment, UserId, DeploymentsController.CanManage()));
		}

		private static bool CanSee(DeploymentTimeAccess access) => DeploymentsController.CanView() || (access?.CanRead ?? false);

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
			var (deployment, access) = await AccessAsync(deploymentId);
			if (deployment == null) return NotFound();
			if (!CanSee(access)) return Unauthorized();
			// With entries: the field apps open a day's report straight from this list (two reads for the whole deployment).
			var reports = await _timeTracking.GetTimeReportsWithEntriesAsync(deploymentId, DepartmentId);
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			var result = new TimeReportsResult { Data = reports.Select(r => Map(r, department, access)).ToList(), PageSize = reports.Count, Status = ResponseHelper.Success };
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
			var (_, access) = await AccessAsync(report.DeploymentId);
			if (!CanSee(access)) return Unauthorized();
			return await OkAsync(report, access);
		}

		[HttpPost("NewTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> NewTimeReport([FromBody] NewTimeReportInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return BadRequest();
			var (deployment, access) = await AccessAsync(input.DeploymentId);
			if (deployment == null) return NotFound();
			// Managers open any scope; a crew member opens their unit's crew report; anyone opens their own individual report
			// (and a crew boss one for a member of the crew). The deployment-wide DTR stays a manager's paper.
			var allowed = access.CanManage
				|| (!string.IsNullOrWhiteSpace(input.DeploymentUnitId) && string.IsNullOrWhiteSpace(input.DeploymentPersonnelId) && access.CrewUnitIds.Contains(input.DeploymentUnitId, StringComparer.OrdinalIgnoreCase))
				|| (!string.IsNullOrWhiteSpace(input.DeploymentPersonnelId) && string.IsNullOrWhiteSpace(input.DeploymentUnitId) && access.CanWriteSubject(input.DeploymentPersonnelId));
			if (!allowed) return Unauthorized();
			try { return await OkAsync(await _timeTracking.CreateTimeReportAsync(input.DeploymentId, DepartmentId, input.ReportDate, input.DeploymentUnitId, input.DeploymentPersonnelId, UserId, Ip, Agent, cancellationToken), access); }
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
			var (_, access) = await AccessAsync(report.DeploymentId);
			if (access == null || !access.CanActOn(report)) return Unauthorized();
			report.IncidentNumber = input.IncidentNumber; report.ResourceOrderNumber = input.ResourceOrderNumber; report.RequestNumber = input.RequestNumber; report.CostCode = input.CostCode; report.PointOfHire = input.PointOfHire;
			report.NoClear8 = input.NoClear8; report.UnsafeConditionsStandDown = input.UnsafeConditionsStandDown; report.Notes = input.Notes; report.RmsExternalOrderFillId = input.RmsExternalOrderFillId;
			try { return await OkAsync(await _timeTracking.UpdateTimeReportAsync(report, UserId, Ip, Agent, cancellationToken), access); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		/// <summary>
		/// Replaces the entries of the subjects the caller may write (every subject for a manager); other subjects' entries stay as
		/// stored. StartLocal/EndLocal, when sent, are department-local wall clock and win over StartTime/EndTime. Validation
		/// errors come back with the unchanged report and status Failure.
		/// </summary>
		[HttpPost("SaveTimeEntries")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> SaveTimeEntries([FromBody] SaveTimeEntriesInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			var report = await _timeTracking.GetTimeReportByIdAsync(input.TimeReportId, DepartmentId);
			if (report == null) return NotFound();
			var (_, access) = await AccessAsync(report.DeploymentId);
			if (access == null || !access.CanWrite) return Unauthorized();
			// A scoped report belongs to its crew or person; nobody else writes into it even for their own subjects.
			if (report.Scope != DeploymentTimeReportScopes.Deployment && !access.CanActOn(report)) return Unauthorized();
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			try
			{
				var entries = new List<DeploymentTimeEntry>();
				foreach (var e in input.Entries ?? new List<TimeEntryData>())
				{
					if (e == null) continue;
					if (!TryResolve(e.StartLocal, e.StartTime, department, out var start) || !TryResolve(e.EndLocal, e.EndTime, department, out var end)) return Failed<TimeReportResult>("timereports_time_invalid");
					entries.Add(new DeploymentTimeEntry
					{
						DeploymentTimeEntryId = string.IsNullOrWhiteSpace(e.Id) ? null : e.Id,
						DeploymentPersonnelId = e.DeploymentPersonnelId, DeploymentUnitId = e.DeploymentUnitId, DeploymentEquipmentId = e.DeploymentEquipmentId, EntryType = e.EntryType, StartTime = start, EndTime = end,
						PaidBreakMinutes = e.PaidBreakMinutes, UnpaidBreakMinutes = e.UnpaidBreakMinutes, CrewSizeSnapshot = e.CrewSizeSnapshot, CertificationCode = e.CertificationCode, MileageKm = e.MileageKm, FuelDeductionLitres = e.FuelDeductionLitres,
						AgencySuppliedMeals = e.AgencySuppliedMeals, AgencySuppliedAccommodation = e.AgencySuppliedAccommodation, Notes = e.Notes, SortOrder = e.SortOrder
					});
				}
				return await OkAsync(await _timeTracking.SaveTimeEntriesAsync(input.TimeReportId, DepartmentId, entries, access, UserId, Ip, Agent, cancellationToken), access, department);
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
			var (_, access) = await AccessAsync(report.DeploymentId);
			if (access == null || !access.CanActOn(report)) return Unauthorized();
			try { return await OkAsync(await _timeTracking.SubmitTimeReportAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken), access); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("ApproveTimeReport")]
		[Authorize(Policy = ResgridResources.TimeReports_Approve)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> ApproveTimeReport([FromBody] TimeReportActionInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			try
			{
				var approved = await _timeTracking.ApproveTimeReportAsync(input.Id, DepartmentId, UserId, Ip, Agent, cancellationToken);
				var (_, access) = await AccessAsync(approved.DeploymentId);
				return await OkAsync(approved, access);
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpPost("VoidTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> VoidTimeReport([FromBody] TimeReportActionInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			if (!CanApprove()) return Unauthorized();
			try
			{
				var voided = await _timeTracking.VoidTimeReportAsync(input.Id, DepartmentId, input.Reason, UserId, Ip, Agent, cancellationToken);
				var (_, access) = await AccessAsync(voided.DeploymentId);
				return await OkAsync(voided, access);
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		/// <summary>Crew boss / contractor signature (the acting user) and/or the customer signer's typed name.</summary>
		[HttpPost("SignTimeReport")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TimeReportResult>> SignTimeReport([FromBody] SignTimeReportInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<TimeReportResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return BadRequest();
			var report = await _timeTracking.GetTimeReportByIdAsync(input.Id, DepartmentId);
			if (report == null) return NotFound();
			var (_, access) = await AccessAsync(report.DeploymentId);
			if (access == null || !access.CanActOn(report)) return Unauthorized();
			try { return await OkAsync(await _timeTracking.SignTimeReportAsync(input.Id, DepartmentId, input.ContractorSigned, input.CustomerSignerName, UserId, Ip, Agent, cancellationToken), access); }
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<TimeReportResult>(ex.Message); }
		}

		[HttpGet("GetTimeReportPdf")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> GetTimeReportPdf(string id)
		{
			if (!await EnabledAsync()) return StatusCode(StatusCodes.Status403Forbidden);
			var report = await _timeTracking.GetTimeReportByIdAsync(id, DepartmentId);
			if (report == null) return NotFound();
			var (_, access) = await AccessAsync(report.DeploymentId);
			if (!CanSee(access)) return Unauthorized();
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
			var (deployment, access) = await AccessAsync(deploymentId);
			if (deployment == null) return NotFound();
			if (!CanSee(access)) return Unauthorized();
			var rows = await _timeTracking.GetExpensesAsync(deploymentId, DepartmentId);
			var result = new ExpensesResult { Data = rows.Select(MapExpense).ToList(), PageSize = rows.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>A field member adds expenses on a deployment they may write time for and edits only the ones they added; managers edit any.</summary>
		[HttpPost("SaveExpense")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ExpenseResult>> SaveExpense([FromBody] SaveExpenseInput input, CancellationToken cancellationToken)
		{
			if (!await EnabledAsync()) return Failed<ExpenseResult>("deployments_disabled", StatusCodes.Status403Forbidden);
			if (input == null || string.IsNullOrWhiteSpace(input.DeploymentId)) return BadRequest();
			var (deployment, access) = await AccessAsync(input.DeploymentId);
			if (deployment == null) return NotFound();
			if (!access.CanWrite) return Unauthorized();
			if (!access.CanManage)
			{
				if (!string.IsNullOrWhiteSpace(input.Id))
				{
					var existing = await _timeTracking.GetExpenseByIdAsync(input.Id, DepartmentId);
					if (existing == null) return NotFound();
					if (!string.Equals(existing.AddedByUserId, UserId, StringComparison.OrdinalIgnoreCase)) return Unauthorized();
				}
				if (!string.IsNullOrWhiteSpace(input.TimeReportId))
				{
					var linked = await _timeTracking.GetTimeReportByIdAsync(input.TimeReportId, DepartmentId);
					if (linked == null) return NotFound();
					if (!access.CanActOn(linked)) return Unauthorized();
				}
			}
			byte[] receipt = null;
			if (!string.IsNullOrWhiteSpace(input.ReceiptData))
			{
				// Base64 is 4 characters per 3 bytes: refuse on the encoded length before decoding allocates the oversized buffer.
				if (input.ReceiptData.Length > Resgrid.Services.Invoicing.DeploymentService.MaxAttachmentBytes / 3 * 4 + 4) return Failed<ExpenseResult>("deployments_attachment_too_large");
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
			var (_, access) = await AccessAsync(expense.DeploymentId);
			if (access == null || !access.CanWrite) return Unauthorized();
			if (!access.CanManage && !string.Equals(expense.AddedByUserId, UserId, StringComparison.OrdinalIgnoreCase)) return Unauthorized();
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

		/// <summary>A department-local wall clock wins when sent; otherwise the UTC instant (the converter already normalised it).</summary>
		private static bool TryResolve(string local, DateTime utc, Department department, out DateTime value)
		{
			value = utc;
			if (string.IsNullOrWhiteSpace(local)) return utc != default;
			if (!DateTime.TryParseExact(local.Trim(), LocalClockFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
			value = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified).DepartmentLocalToUtc(department);
			return true;
		}

		private static string LocalClock(DateTime utc, Department department) => utc.TimeConverter(department ?? new Department()).ToString(LocalClockFormat, CultureInfo.InvariantCulture);

		private async Task<ActionResult<TimeReportResult>> OkAsync(DeploymentTimeReport report, DeploymentTimeAccess access)
		{
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId);
			var result = new TimeReportResult { Data = Map(report, department, access), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private async Task<ActionResult<TimeReportResult>> OkAsync(TimeReportSaveResult save, DeploymentTimeAccess access, Department department = null)
		{
			department ??= await _departments.GetDepartmentByIdAsync(DepartmentId);
			var result = new TimeReportResult
			{
				Data = save.Report == null ? null : Map(save.Report, department, access),
				Errors = save.Validation.Errors.Select(MapIssue).ToList(),
				Warnings = save.Validation.Warnings.Select(MapIssue).ToList(),
				PageSize = 1,
				Status = save.Validation.IsValid ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			if (!save.Validation.IsValid) Response.Headers["X-Resgrid-Reason"] = "timereports_validation";
			return result;
		}

		internal static TimeReportData Map(DeploymentTimeReport r, Department department = null, DeploymentTimeAccess access = null) => new TimeReportData
		{
			Id = r.DeploymentTimeReportId, DeploymentId = r.DeploymentId, ReportNumber = r.ReportNumber, ReportDate = r.ReportDate, Scope = (int)r.Scope, DeploymentUnitId = r.DeploymentUnitId, DeploymentPersonnelId = r.DeploymentPersonnelId,
			CanAct = access?.CanActOn(r) ?? false, Status = r.Status, IncidentNumber = r.IncidentNumber, ResourceOrderNumber = r.ResourceOrderNumber,
			RequestNumber = r.RequestNumber, CostCode = r.CostCode, PointOfHire = r.PointOfHire, NoClear8 = r.NoClear8, UnsafeConditionsStandDown = r.UnsafeConditionsStandDown, ContractorSignedByUserId = r.ContractorSignedByUserId,
			ContractorSignedOn = r.ContractorSignedOn, CustomerSignerName = r.CustomerSignerName, CustomerSignedOn = r.CustomerSignedOn, SubmittedByUserId = r.SubmittedByUserId, SubmittedOn = r.SubmittedOn,
			ApprovedByUserId = r.ApprovedByUserId, ApprovedOn = r.ApprovedOn, InvoiceId = r.InvoiceId, RmsExternalOrderFillId = r.RmsExternalOrderFillId, Notes = r.Notes, AddedOn = r.AddedOn, UpdatedOn = r.EditedOn ?? r.AddedOn,
			Entries = (r.Entries ?? new List<DeploymentTimeEntry>()).Select(e => new TimeEntryData
			{
				Id = e.DeploymentTimeEntryId, SubjectType = e.SubjectType, DeploymentPersonnelId = e.DeploymentPersonnelId, DeploymentUnitId = e.DeploymentUnitId, DeploymentEquipmentId = e.DeploymentEquipmentId, EntryType = e.EntryType,
				StartTime = e.StartTime, EndTime = e.EndTime, StartLocal = LocalClock(e.StartTime, department), EndLocal = LocalClock(e.EndTime, department),
				PaidBreakMinutes = e.PaidBreakMinutes, UnpaidBreakMinutes = e.UnpaidBreakMinutes, CrewSizeSnapshot = e.CrewSizeSnapshot, CertificationCode = e.CertificationCode, MileageKm = e.MileageKm,
				FuelDeductionLitres = e.FuelDeductionLitres, AgencySuppliedMeals = e.AgencySuppliedMeals, AgencySuppliedAccommodation = e.AgencySuppliedAccommodation, Notes = e.Notes, SortOrder = e.SortOrder, Hours = e.Hours
			}).ToList()
		};

		internal static TimeReportIssueData MapIssue(TimeReportIssue i) => new TimeReportIssueData { Code = i.Code, SubjectId = i.SubjectId, EntryId = i.EntryId, Detail = i.Detail };

		internal static ExpenseData MapExpense(DeploymentExpense e) => new ExpenseData
		{
			Id = e.DeploymentExpenseId, DeploymentId = e.DeploymentId, TimeReportId = e.DeploymentTimeReportId, ExpenseDate = e.ExpenseDate, ExpenseType = e.ExpenseType, MealCode = e.MealCode, City = e.City,
			Description = e.Description, Amount = e.Amount, Currency = e.Currency, PreApproved = e.PreApproved, Billable = e.Billable, ReceiptAttachmentId = e.ReceiptAttachmentId,
			AddedByUserId = e.AddedByUserId, AddedOn = e.AddedOn, UpdatedOn = e.EditedOn ?? e.AddedOn
		};

		#endregion
	}
}

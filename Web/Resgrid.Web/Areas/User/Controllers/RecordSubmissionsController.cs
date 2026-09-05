using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_Submit)]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordSubmissionsController : SecureBaseController
	{
		private readonly IRmsSubmissionsRepository _submissions;
		private readonly IRecordsSubmissionService _worker;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsCutoverService _cutover;
		public RecordSubmissionsController(IRmsSubmissionsRepository submissions, IRecordsSubmissionService worker,
			IRecordsAuthorizationService authorization, IRecordsCutoverService cutover)
		{ _submissions = submissions; _worker = worker; _authorization = authorization; _cutover = cutover; }

		[HttpGet]
		public async Task<IActionResult> Details(string submissionId, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			var submission = await _submissions.GetByIdForDepartmentAsync(DepartmentId, submissionId);
			if (submission == null) return NotFound();
			try
			{
				var isAdministrator = await _authorization.IsDepartmentAdminAsync(UserId, DepartmentId);
				var history = await _worker.GetHistoryAsync(DepartmentId, UserId, submissionId, cancellationToken);
				return View(new RecordSubmissionView { Submission = submission, Exchanges = history, IsAdministrator = isAdministrator,
					Message = TempData["SubmissionMessage"] as string, Error = TempData["SubmissionError"] as string });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: 409); }
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Reconcile(string submissionId, RmsSubmissionReconciliationInput input, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			if (input == null) return BadRequest();
			try
			{
				if (input.ConfirmedNotCreated)
				{
					if (!string.IsNullOrWhiteSpace(input.ExternalId)) return BadRequest("A recorded filing cannot also be declared absent.");
					await _worker.ConfirmNotCreatedAsync(DepartmentId, UserId, submissionId, input.RowVersion, input.VerificationReference, input.Reason, cancellationToken);
					TempData["SubmissionMessage"] = "External verification recorded. Nothing was sent. Return to the report and deliberately queue the corrected filing when ready.";
				}
				else
				{
					await _worker.ReconcileAsync(DepartmentId, UserId, submissionId, input.RowVersion, input.ExternalId, input.Reason, cancellationToken);
					TempData["SubmissionMessage"] = string.IsNullOrWhiteSpace(input.ExternalId) ? "The unsent filing was bound to the reviewed destination and queued." : "Destination receipt verified. Status polling will resume.";
				}
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["SubmissionError"] = "The submission changed. Review its current history before making another recovery decision."; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["SubmissionError"] = ex.Message; }
			return RedirectToAction(nameof(Details), new { submissionId });
		}
	}
}

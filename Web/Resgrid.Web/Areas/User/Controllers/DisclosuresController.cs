using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Public-records and access-to-information requests (RMS plan section 4.7, RMS-3).
	/// <para>
	/// For a public agency a records request is a statutory obligation with a clock, so it is tracked as a record
	/// with a due date rather than an export button: log the request, settle the scope, produce a redacted
	/// immutable artifact with a produced-set snapshot, and release. Every action needs
	/// <c>RecordDisclosure_Update</c> — including the reads, because the queue names who asked for what.
	/// </para>
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.RecordDisclosure_Update)]
	public class DisclosuresController : SecureBaseController
	{
		private readonly IRecordsDisclosureService _disclosures;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public DisclosuresController(IRecordsDisclosureService disclosures, IRecordsCutoverService cutoverService, IDepartmentsService departmentsService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer, IRecordsAuthorizationService authorization)
		{
			_disclosures = disclosures;
			_authorization = authorization;
			_cutoverService = cutoverService;
			_departmentsService = departmentsService;
			_localizer = localizer;
		}

		#region Queue

		[HttpGet]
		public async Task<IActionResult> Index(int? state)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return NotFound();

			var states = state.HasValue ? new List<RmsDisclosureState> { (RmsDisclosureState)state.Value } : null;
			var model = new DisclosureIndexView
			{
				ModuleState = moduleState,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				StateFilter = state,
				States = StateItems(),
				RedactionProfiles = ProfileItems(),
				CanViewRestricted = await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords),
				PersonnelNames = await PersonnelNamesAsync()
			};
			model.Personnel = model.PersonnelNames.OrderBy(kvp => kvp.Value).Select(kvp => new SelectListItem { Value = kvp.Key, Text = kvp.Value }).ToList();

			if (moduleState.RecordsUsable)
				model.Requests = await _disclosures.QueryAsync(DepartmentId, UserId, states, 0, 200);

			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			if (TempData["RecordsError"] is string error)
				model.ErrorMessage = error;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(string requesterName, string requesterOrganization, string requesterContact, DateTime? receivedOn,
			DateTime? statutoryDueOn, string jurisdictionProfile, string scopeNarrative, string assignedToUserId, string redactionProfile, CancellationToken cancellationToken)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.RecordsUsable)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			try
			{
				var created = await _disclosures.CreateRequestAsync(DepartmentId, UserId, new RmsDisclosureRequest
				{
					RequesterName = requesterName,
					RequesterOrganization = requesterOrganization,
					RequesterContact = requesterContact,
					ReceivedOn = ToUtc(receivedOn, department) ?? DateTime.UtcNow,
					StatutoryDueOn = ToUtc(statutoryDueOn, department),
					JurisdictionProfile = jurisdictionProfile,
					ScopeNarrative = scopeNarrative,
					AssignedToUserId = assignedToUserId,
					RedactionProfile = redactionProfile
				}, cancellationToken);

				TempData["RecordsMessage"] = _localizer["DisclosureCreated"].Value;
				return RedirectToAction("Details", new { id = created.RmsDisclosureRequestId });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				TempData["RecordsError"] = ex.Message;
				return RedirectToAction("Index");
			}
		}

		#endregion

		#region Request

		[HttpGet]
		public async Task<IActionResult> Details(string id)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();

			if (TempData["RecordsMessage"] is string message)
				model.Message = message;
			if (TempData["RecordsError"] is string error)
				model.ErrorMessage = error;
			return View(model);
		}

		/// <summary>Saves the scope; refused once a production exists, because the scope is what was produced against.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveScope(DisclosureScopeForm scope, CancellationToken cancellationToken)
		{
			if (scope == null || string.IsNullOrWhiteSpace(scope.RequestId))
				return NotFound();
			if (await _disclosures.GetAsync(DepartmentId, UserId, scope.RequestId) == null)
				return NotFound();

			try
			{
				await _disclosures.SaveScopeAsync(DepartmentId, UserId, scope.RequestId, scope.ScopeNarrative, new RmsRecordQuery
				{
					States = scope.States != null && scope.States.Count > 0 ? scope.States.ToList() : null,
					DefinitionKey = string.IsNullOrWhiteSpace(scope.DefinitionKey) ? null : scope.DefinitionKey,
					Year = scope.Year,
					CallId = scope.CallId,
					IncludeLegacy = scope.IncludeLegacy,
					Take = 200
				}, scope.RedactionProfile, cancellationToken);

				TempData["RecordsMessage"] = _localizer["DisclosureScopeSaved"].Value;
				return RedirectToAction("Details", new { id = scope.RequestId });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(scope.RequestId, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestFormLimits(ValueCountLimit = 20000)]
		[RequestSizeLimit(100 * 1024 * 1024)]
		public async Task<IActionResult> Produce(string id, string redactionProfile, CancellationToken cancellationToken, RmsDisclosureReview review)
		{
			if (await _disclosures.GetAsync(DepartmentId, UserId, id) == null)
				return NotFound();

			try
			{
				if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
				foreach (var file in Request.Form.Files)
				{
					var match = System.Text.RegularExpressions.Regex.Match(file.Name, "^derivative-([0-9]+)-([0-9]+)$");
					if (!match.Success || !int.TryParse(match.Groups[1].Value, out var recordIndex) || !int.TryParse(match.Groups[2].Value, out var attachmentIndex)
						|| review?.Records == null || recordIndex >= review.Records.Count || review.Records[recordIndex].Attachments == null || attachmentIndex >= review.Records[recordIndex].Attachments.Count) throw new ArgumentException("A replacement file does not match the review.");
					if (file.Length <= 0) continue;
					if (file.Length > 25 * 1024 * 1024) throw new ArgumentException("Replacement files must be 25 MB or smaller.");
					var decision = review.Records[recordIndex].Attachments[attachmentIndex];
					if (decision.Derivative != null) throw new ArgumentException("Review one replacement per source attachment.");
					using var data = new System.IO.MemoryStream(); await file.CopyToAsync(data, cancellationToken); var bytes = data.ToArray();
					decision.Derivative = new RmsDisclosureAttachmentDerivative { FileName = file.FileName, ContentType = file.ContentType, Data = bytes, Checksum = Resgrid.Services.Records.RecordSnapshotSerializer.Checksum(bytes) };
				}
				await _disclosures.ProduceAsync(DepartmentId, UserId, id, redactionProfile, cancellationToken, review);
				TempData["RecordsMessage"] = _localizer["DisclosureProduced"].Value;
				return RedirectToAction("Details", new { id });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Release(string id, string productionId, CancellationToken cancellationToken, string deliveryMethod, string deliveryReference)
		{
			if (await LoadProductionAsync(id, productionId) == null)
				return NotFound();

			try
			{
				await _disclosures.ReleaseAsync(DepartmentId, UserId, productionId, cancellationToken, deliveryMethod, deliveryReference);
				TempData["RecordsMessage"] = _localizer["DisclosureReleased"].Value;
				return RedirectToAction("Details", new { id });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Close(string id, int disposition, string reason, CancellationToken cancellationToken)
		{
			if (await _disclosures.GetAsync(DepartmentId, UserId, id) == null)
				return NotFound();
			if (string.IsNullOrWhiteSpace(reason))
				return await DetailsWithErrorAsync(id, _localizer["DisclosureReasonRequired"]);

			try
			{
				await _disclosures.CloseAsync(DepartmentId, UserId, id, (RmsDisclosureState)disposition, reason, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DisclosureClosed"].Value;
				return RedirectToAction("Details", new { id });
			}
			catch (Exception ex) when (ex is ArgumentException || ex is RecordTransitionException || ex is InvalidOperationException)
			{
				return await DetailsWithErrorAsync(id, ex.Message);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Review(string id, string redactionProfile)
		{
			if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try { Response.Headers["Cache-Control"] = "no-store"; return View(await _disclosures.GetReviewAsync(DepartmentId, UserId, id, redactionProfile)); }
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return await DetailsWithErrorAsync(id, ex.Message); }
		}
		[HttpGet]
		public async Task<IActionResult> ReviewAttachment(string id, string recordId, string revisionId, string attachmentId, string profile)
		{
			if (!(await _cutoverService.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try
			{
				var file = await _disclosures.GetReviewAttachmentAsync(DepartmentId, UserId, id, recordId, revisionId, attachmentId, profile);
				if (file == null) return NotFound();
				Response.Headers["Cache-Control"] = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
				return File(file.Data, "application/octet-stream", file.FileName);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}
		/// <summary>The produced artifact as it was released, so a department can hand over exactly what it recorded handing over.</summary>
		[HttpGet]
		public async Task<IActionResult> Download(string id, string productionId, string format = "zip")
		{
			var production = await LoadProductionAsync(id, productionId);
			if (production == null || string.IsNullOrWhiteSpace(production.ArtifactJson))
				return NotFound();

			try
			{
				var packet = await _disclosures.DownloadAsync(DepartmentId, UserId, productionId, format);
				Response.Headers["Cache-Control"] = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
				return File(packet.Data, packet.ContentType, packet.FileName);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return await DetailsWithErrorAsync(id, ex.Message); }
		}

		/// <summary>Re-computes the checksum from the stored artifact; a mismatch means it was tampered with.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Verify(string id, string productionId)
		{
			if (await LoadProductionAsync(id, productionId) == null)
				return NotFound();

			var intact = await _disclosures.VerifyProductionAsync(DepartmentId, productionId);
			TempData[intact ? "RecordsMessage" : "RecordsError"] = intact ? _localizer["ProductionIntact"].Value : _localizer["ProductionTampered"].Value;
			return RedirectToAction("Details", new { id });
		}

		#endregion

		#region Helpers

		private async Task<RmsDisclosureProduction> LoadProductionAsync(string requestId, string productionId)
		{
			if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(productionId))
				return null;
			if (await _disclosures.GetAsync(DepartmentId, UserId, requestId) == null)
				return null;

			var productions = await _disclosures.GetProductionsAsync(DepartmentId, UserId, requestId);
			return productions.FirstOrDefault(p => string.Equals(p.RmsDisclosureProductionId, productionId, StringComparison.Ordinal));
		}

		private async Task<DisclosureDetailView> BuildDetailAsync(string id)
		{
			var moduleState = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!moduleState.FlagEnabled)
				return null;

			var request = await _disclosures.GetAsync(DepartmentId, UserId, id);
			if (request == null)
				return null;

			var model = new DisclosureDetailView
			{
				Request = request,
				Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false),
				Productions = await _disclosures.GetProductionsAsync(DepartmentId, UserId, id),
				PersonnelNames = await PersonnelNamesAsync(),
				CanViewRestricted = await _authorization.HasPermissionAsync(UserId, DepartmentId, PermissionTypes.ViewRestrictedRecords),
				RedactionProfiles = ProfileItems(),
				RecordStates = Enum.GetValues(typeof(RmsRecordState)).Cast<RmsRecordState>()
					.Select(s => new SelectListItem { Value = ((int)s).ToString(), Text = s.ToString() }).ToList(),
				Dispositions = new List<SelectListItem>
				{
					new SelectListItem { Value = ((int)RmsDisclosureState.Denied).ToString(), Text = RmsDisclosureState.Denied.ToString() },
					new SelectListItem { Value = ((int)RmsDisclosureState.Withdrawn).ToString(), Text = RmsDisclosureState.Withdrawn.ToString() },
					new SelectListItem { Value = ((int)RmsDisclosureState.Closed).ToString(), Text = RmsDisclosureState.Closed.ToString() }
				},
				DefinitionKeys = RmsDefinitionKeys.LockedTypes.Keys.Append(RmsDefinitionKeys.NerisIncidentReport).OrderBy(k => k).Select(k => new SelectListItem { Value = k, Text = k }).ToList()
			};

			model.Scope = new DisclosureScopeForm
			{
				RequestId = id,
				ScopeNarrative = request.ScopeNarrative,
				RedactionProfile = request.RedactionProfile ?? RmsRedactionProfiles.Standard
			};

			// The stored scope is the same RmsRecordQuery the queue runs; re-render it so an officer edits what
			// will actually be produced against rather than a paraphrase of it.
			if (!string.IsNullOrWhiteSpace(request.ScopeQueryJson))
			{
				var stored = JsonConvert.DeserializeObject<RmsRecordQuery>(request.ScopeQueryJson);
				if (stored != null)
				{
					model.Scope.DefinitionKey = stored.DefinitionKey;
					model.Scope.Year = stored.Year;
					model.Scope.CallId = stored.CallId;
					model.Scope.IncludeLegacy = stored.IncludeLegacy;
					model.Scope.States = stored.States?.ToList() ?? new List<int>();
				}

				try
				{
					model.Preview = await _disclosures.PreviewScopeAsync(DepartmentId, UserId, id);
				}
				catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
				{
					// A scope that no longer resolves is a thing the officer needs told, not a broken page.
					model.ErrorMessage = ex.Message;
				}
			}

			return model;
		}

		private async Task<IActionResult> DetailsWithErrorAsync(string id, string error)
		{
			var model = await BuildDetailAsync(id);
			if (model == null)
				return NotFound();
			model.ErrorMessage = error;
			return View("Details", model);
		}

		private static List<SelectListItem> StateItems()
		{
			return Enum.GetValues(typeof(RmsDisclosureState)).Cast<RmsDisclosureState>()
				.Select(s => new SelectListItem { Value = ((int)s).ToString(), Text = s.ToString() }).ToList();
		}

		private static List<SelectListItem> ProfileItems()
		{
			return new List<SelectListItem>
			{
				new SelectListItem { Value = RmsRedactionProfiles.Standard, Text = RmsRedactionProfiles.Standard },
				new SelectListItem { Value = RmsRedactionProfiles.NoPersonalIdentifiers, Text = RmsRedactionProfiles.NoPersonalIdentifiers },
				new SelectListItem { Value = RmsRedactionProfiles.FullDisclosure, Text = RmsRedactionProfiles.FullDisclosure }
			};
		}

		private static DateTime? ToUtc(DateTime? local, Department department)
		{
			if (!local.HasValue || local.Value == DateTime.MinValue)
				return null;
			if (department == null || string.IsNullOrWhiteSpace(department.TimeZone))
				return DateTime.SpecifyKind(local.Value, DateTimeKind.Utc);

			return DateTimeHelpers.ConvertToUtc(local.Value, department.TimeZone, true);
		}

		private async Task<Dictionary<string, string>> PersonnelNamesAsync()
		{
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var name in names ?? new List<PersonName>())
			{
				if (!string.IsNullOrWhiteSpace(name.UserId))
					map[name.UserId] = name.Name;
			}
			return map;
		}

		#endregion
	}
}

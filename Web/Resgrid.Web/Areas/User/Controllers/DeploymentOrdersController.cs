using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Create Deployment from External Order (RMS plan section 4.1, operational workspace): the coordinator records the
	/// external order and the requests the department fills, then walks each fill through mobilization, release and
	/// the actual return home. Manual entry and artifact snapshots only; no ordering-system connector.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Deployments_Update)]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class DeploymentOrdersController : SecureBaseController
	{
		private readonly IRecordDeploymentsService _deployments;
		private readonly IDeploymentService _operations;
		private readonly IFeatureToggleService _flags;
		private readonly IRecordsCutoverService _cutover;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUnitsService _units;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public DeploymentOrdersController(IRecordDeploymentsService deployments, IDeploymentService operations, IFeatureToggleService flags, IRecordsCutoverService cutover, IDepartmentsService departments, IDepartmentGroupsService groups, IUnitsService units,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			_deployments = deployments;
			_operations = operations;
			_flags = flags;
			_cutover = cutover;
			_departments = departments;
			_groups = groups;
			_units = units;
			_localizer = localizer;
		}

        public override async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context, Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
        {
            if (!(ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || ClaimsAuthorizationHelper.CanManageDeployments())) { context.Result = Forbid(); return; }
            if (!await _flags.IsEnabledAsync(FeatureFlagKeys.Deployments, DepartmentId)) { context.Result = NotFound(); return; }
            if (HttpMethods.IsPost(Request.Method) && !(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) { context.Result = NotFound(); return; }
            await next();
        }

		[HttpGet]
		public async Task<IActionResult> Index(bool includeClosed = false)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			var model = new RecordDeploymentsIndexView { Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false), Orders = await _deployments.ListAsync(DepartmentId, UserId, includeClosed), IncludeClosed = includeClosed, CanCreate = ClaimsAuthorizationHelper.CanCreateRecord(), IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin() };
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> New()
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			var model = new RecordDeploymentNewView { IdempotencyKey = Guid.NewGuid().ToString() };
			await PopulateAsync(model);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> New(RecordDeploymentNewView model, IFormFile artifact, CancellationToken cancellationToken)
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).RecordsUsable) return NotFound();
			try
			{
				var input = new RecordDeploymentCreateInput
				{
					IdempotencyKey = model.IdempotencyKey, ProfileKey = model.ProfileKey, SourceScheme = model.SourceScheme, SourceSystem = model.SourceSystem, OrderNumber = model.OrderNumber, IncidentName = model.IncidentName, IncidentNumber = model.IncidentNumber,
					IncidentCountry = model.IncidentCountry, IncidentSubdivision = model.IncidentSubdivision, OrderingOffice = model.OrderingOffice, DispatchOffice = model.DispatchOffice, RequestingAgency = model.RequestingAgency,
					ReceivingAgency = model.ReceivingAgency, SendingAgency = model.SendingAgency, DepartmentRole = model.DepartmentRole, CostCode = model.CostCode, AgreementReference = model.AgreementReference,
					CurrencyCode = string.IsNullOrWhiteSpace(model.CurrencyCode) ? null : model.CurrencyCode, MeasurementSystem = string.IsNullOrWhiteSpace(model.MeasurementSystem) ? null : model.MeasurementSystem, TimeZoneId = model.TimeZoneId,
					SourceCapturedOn = model.SourceCapturedOn, SourceVersion = model.SourceVersion, ArtifactSafeUrl = model.ArtifactSafeUrl, StationGroupId = model.StationGroupId, OriginClient = RmsOriginClient.Web,
					Fills = (model.Fills ?? new List<RecordDeploymentFillInput>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.RequestNumber)).ToList()
				};
				if (artifact != null && artifact.Length > 0)
				{
					using var stream = new MemoryStream();
					await artifact.CopyToAsync(stream, cancellationToken);
					input.ArtifactData = stream.ToArray(); input.ArtifactFileName = Path.GetFileName(artifact.FileName); input.ArtifactContentType = artifact.ContentType;
				}
				foreach (var fill in input.Fills) fill.NeededOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(fill.NeededOn);
				var created = await _deployments.CreateFromExternalOrderAsync(DepartmentId, UserId, input, cancellationToken);
				var operation = await LinkAsync(created.Order.RmsExternalOrderId, cancellationToken);
                TempData["DeploymentsSaved"] = true;
                return RedirectToAction("View", "Deployments", new { id = operation.DeploymentId });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
			{
				model.ErrorMessage = ex.Message;
				await PopulateAsync(model);
				return View(model);
			}
		}

		[HttpGet]
		public async Task<IActionResult> Details(string id)
		{
			var model = await BuildDetailsAsync(id);
			if (model == null) return NotFound();
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			return View(model);
        }

        private async Task<Resgrid.Model.Invoicing.Deployment> LinkAsync(string orderId, CancellationToken cancellationToken)
        {
            var linked = await _operations.GetDeploymentByExternalOrderIdAsync(orderId, DepartmentId);
            linked ??= await _operations.CreateFromExternalOrderAsync(DepartmentId,
                new Resgrid.Model.Invoicing.ExternalOrderDeploymentInput { RmsExternalOrderId = orderId, PrefillRoster = true },
                UserId, Request.HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers["User-Agent"].ToString(), cancellationToken);
            return await _operations.SynchronizeExternalOrderAsync(orderId, DepartmentId, UserId, null, null, cancellationToken) ?? linked;
        }

        [HttpGet]
        public async Task<IActionResult> ForRecord(string id)
        {
            try
            {
                var order = await _deployments.GetForRecordAsync(DepartmentId, UserId, id);
                return order == null ? NotFound() : RedirectToAction("Details", new { id = order.Order.RmsExternalOrderId });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Policy = ResgridResources.Record_Create)]
        public async Task<IActionResult> Link(string id, CancellationToken cancellationToken)
        {
            try
            {
                // Resolve through Records first, including its department and record visibility checks.
                if (await _deployments.GetAsync(DepartmentId, UserId, id) == null) return NotFound();
                var linked = await LinkAsync(id, cancellationToken);
                return RedirectToAction("View", "Deployments", new { id = linked.DeploymentId });
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                TempData["RecordsError"] = ex.Message;
                return RedirectToAction("Details", new { id });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = ResgridResources.Record_Create)]
        public async Task<IActionResult> AddFill(string id, RecordDeploymentFillInput newFill, CancellationToken cancellationToken)
		{
			try
			{
				newFill.NeededOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(newFill.NeededOn);
				await _deployments.AddFillAsync(DepartmentId, UserId, id, newFill, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DeploymentFillAdded"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["RecordsError"] = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Details", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Transition(string id, string fillId, int status, DateTime? occurredOn, string reason, string notes, long? rowVersion, CancellationToken cancellationToken)
		{
			try
			{
                var order = await _deployments.GetAsync(DepartmentId, UserId, id);
                if (order == null || !order.Fills.Any(f => f.RmsExternalOrderFillId == fillId)) return NotFound();
				await _deployments.TransitionFillAsync(DepartmentId, UserId, fillId, new RecordDeploymentFillTransitionInput { ExpectedRowVersion = rowVersion, Status = (RmsDeploymentFillStatus)status, OccurredOn = Resgrid.Web.Helpers.DepartmentTime.From(ViewData).ToUtc(occurredOn), Reason = reason, Notes = notes }, cancellationToken);
				await LinkAsync(id, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DeploymentFillUpdated"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["RecordsError"] = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Details", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Snapshot(string id, string sourceVersion, IFormFile artifact, CancellationToken cancellationToken)
		{
			try
			{
				if (artifact == null || artifact.Length == 0) throw new ArgumentException(_localizer["DeploymentSnapshotNeedsFile"].Value);
				using var stream = new MemoryStream();
				await artifact.CopyToAsync(stream, cancellationToken);
				await _deployments.RecordSourceSnapshotAsync(DepartmentId, UserId, id, sourceVersion, stream.ToArray(), Path.GetFileName(artifact.FileName), artifact.ContentType, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DeploymentSnapshotRecorded"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["RecordsError"] = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Details", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Record_Create)]
		public async Task<IActionResult> Closeout(string id, long rowVersion, string notes, CancellationToken cancellationToken)
		{
			try
			{
				await _deployments.CloseoutAsync(DepartmentId, UserId, id, rowVersion, notes, cancellationToken);
				await LinkAsync(id, cancellationToken);
				TempData["RecordsMessage"] = _localizer["DeploymentClosedOut"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { TempData["RecordsError"] = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Details", new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Artifact(string id)
		{
			try
			{
				var aggregate = await _deployments.GetAsync(DepartmentId, UserId, id, true);
				if (aggregate?.Order?.ArtifactData == null) return NotFound();
				return File(aggregate.Order.ArtifactData, string.IsNullOrWhiteSpace(aggregate.Order.ArtifactContentType) ? "application/octet-stream" : aggregate.Order.ArtifactContentType, aggregate.Order.ArtifactFileName ?? "order-artifact");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
		}

		private async Task<RecordDeploymentDetailsView> BuildDetailsAsync(string id)
		{
			RecordDeploymentAggregate aggregate;
			try { aggregate = await _deployments.GetAsync(DepartmentId, UserId, id); }
			catch (UnauthorizedAccessException) { return null; }
			if (aggregate == null) return null;
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			var units = await _units.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>();
			return new RecordDeploymentDetailsView
			{
				Deployment = aggregate, Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false),
				PersonnelNames = names.GroupBy(n => n.UserId).ToDictionary(g => g.Key, g => g.First().Name), CanEdit = ClaimsAuthorizationHelper.CanCreateRecord() && aggregate.Order.Status != (int)RmsExternalOrderStatus.ClosedOut,
				ProvenanceStatement = _localizer["DeploymentsIntro"].Value,
				OperationalDeploymentId = (await _operations.GetDeploymentByExternalOrderIdAsync(id, DepartmentId))?.DeploymentId,
				Personnel = names.OrderBy(n => n.Name).Select(n => new SelectListItem { Value = n.UserId, Text = n.Name }).ToList(),
				AvailableUnits = units.OrderBy(u => u.Name).Select(u => new SelectListItem { Value = u.UnitId.ToString(), Text = u.Name }).ToList()
			};
		}

		private async Task PopulateAsync(RecordDeploymentNewView model)
		{
			model.Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			model.Profiles = RmsDeploymentProfiles.All.Select(p => new SelectListItem { Value = p, Text = p }).ToList();
			var groups = await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			model.Stations = groups.OrderBy(g => g.Name).Select(g => new SelectListItem { Value = g.DepartmentGroupId.ToString(), Text = g.Name }).ToList();
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			model.Personnel = names.OrderBy(n => n.Name).Select(n => new SelectListItem { Value = n.UserId, Text = n.Name }).ToList();
			var units = await _units.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>();
			model.AvailableUnits = units.OrderBy(u => u.Name).Select(u => new SelectListItem { Value = u.UnitId.ToString(), Text = u.Name }).ToList();
			while (model.Fills.Count < 3) model.Fills.Add(new RecordDeploymentFillInput());
		}
	}
}

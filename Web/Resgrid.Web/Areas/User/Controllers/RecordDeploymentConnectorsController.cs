using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// External ordering-system connectors for mutual-aid deployments (RMS plan section 4.1, RMS-1C). Department
	/// administration only. A connector reads the documented Resgrid Mutual-Aid Order Feed under an encrypted
	/// credential, an hourly request limit and an acknowledgement of the source's terms; write authority is
	/// refused. The reconciliation table lists where the source and the department's own record disagree; it is
	/// never applied automatically.
	/// </summary>
	[Area("User")]
	[Authorize(Policy = ResgridResources.Record_View)]
	public class RecordDeploymentConnectorsController : SecureBaseController
	{
		private readonly IRecordDeploymentConnectorsService _connectors;
		private readonly IRecordsCutoverService _cutover;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> _localizer;

		public RecordDeploymentConnectorsController(IRecordDeploymentConnectorsService connectors, IRecordsCutoverService cutover, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.Records.Records> localizer)
		{
			_connectors = connectors;
			_cutover = cutover;
			_departments = departments;
			_localizer = localizer;
		}

		[HttpGet]
		public async Task<IActionResult> Index()
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			var model = new RecordDeploymentConnectorsIndexView
			{
				Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false),
				ConnectorsEnabled = RecordsConnectorConfig.Enabled,
				Connectors = await _connectors.ListAsync(DepartmentId, UserId),
				Reconciliation = await _connectors.GetReconciliationAsync(DepartmentId, UserId)
			};
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			if (TempData["ConnectorToken"] is string token) model.InboundToken = token;
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> New()
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			var model = new RecordDeploymentConnectorEditView { IsNew = true };
			Populate(model);
			return View("Edit", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> New(RecordDeploymentConnectorEditView model, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var created = await _connectors.CreateAsync(DepartmentId, UserId, model.ToInput(), cancellationToken);
				TempData["RecordsMessage"] = _localizer["ConnectorCreated"].Value;
				TempData["ConnectorToken"] = created.InboundToken;
				return RedirectToAction("Edit", new { id = created.Connector.RmsExternalOrderConnectorId });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { model.ErrorMessage = ex.Message; }
			model.IsNew = true;
			Populate(model);
			return View("Edit", model);
		}

		[HttpGet]
		public async Task<IActionResult> Edit(string id)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			var connector = await _connectors.GetAsync(DepartmentId, UserId, id);
			if (connector == null) return NotFound();
			var model = RecordDeploymentConnectorEditView.From(connector);
			await PopulateDetailsAsync(model, connector);
			if (TempData["RecordsMessage"] is string message) model.Message = message;
			if (TempData["RecordsError"] is string error) model.ErrorMessage = error;
			if (TempData["ConnectorToken"] is string token) model.InboundToken = token;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Edit(string id, RecordDeploymentConnectorEditView model, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				await _connectors.UpdateAsync(DepartmentId, UserId, id, model.RowVersion, model.ToInput(), cancellationToken);
				TempData["RecordsMessage"] = _localizer["ConnectorSaved"].Value;
				return RedirectToAction("Edit", new { id });
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (RecordConcurrencyException) { model.ErrorMessage = _localizer["ConcurrencyError"].Value; }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { model.ErrorMessage = ex.Message; }
			var connector = await _connectors.GetAsync(DepartmentId, UserId, id);
			if (connector == null) return NotFound();
			model.Id = id;
			await PopulateDetailsAsync(model, connector);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public Task<IActionResult> SetEnabled(string id, bool enabled, CancellationToken cancellationToken)
			=> ActAsync(id, enabled ? "ConnectorEnabledMessage" : "ConnectorDisabledMessage", () => _connectors.SetEnabledAsync(DepartmentId, UserId, id, enabled, cancellationToken));

		[HttpPost]
		[ValidateAntiForgeryToken]
		public Task<IActionResult> AcknowledgeTerms(string id, CancellationToken cancellationToken)
			=> ActAsync(id, "ConnectorTermsAcknowledgedMessage", () => _connectors.AcknowledgeTermsAsync(DepartmentId, UserId, id, cancellationToken));

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RotateToken(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				TempData["ConnectorToken"] = await _connectors.RotateInboundTokenAsync(DepartmentId, UserId, id, cancellationToken);
				TempData["RecordsMessage"] = _localizer["ConnectorTokenRotated"].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Edit", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Run(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				var result = await _connectors.RunAsync(DepartmentId, UserId, id, cancellationToken);
				var summary = $"{_localizer["ConnectorRunFinished"].Value} {result.Run.Outcome}: {result.Run.OrdersCreated}/{result.Run.SnapshotsRecorded}/{result.Run.RequestsAdded}/{result.Run.Conflicts}.";
				if (result.Run.Outcome == RmsConnectorRunOutcomes.Ok) TempData["RecordsMessage"] = summary;
				else TempData["RecordsError"] = summary + " " + string.Join(" ", result.Messages);
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Edit", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				await _connectors.DeleteAsync(DepartmentId, UserId, id, cancellationToken);
				TempData["RecordsMessage"] = _localizer["ConnectorDeleted"].Value;
				return RedirectToAction("Index");
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Edit", new { id });
		}

		private async Task<IActionResult> ActAsync(string id, string messageKey, Func<Task> action)
		{
			var gate = await GateAsync();
			if (gate != null) return gate;
			try
			{
				await action();
				TempData["RecordsMessage"] = _localizer[messageKey].Value;
			}
			catch (UnauthorizedAccessException) { return Forbid(); }
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { TempData["RecordsError"] = ex.Message; }
			return RedirectToAction("Edit", new { id });
		}

		private async Task<IActionResult> GateAsync()
		{
			if (!(await _cutover.GetModuleStateAsync(DepartmentId)).FlagEnabled) return NotFound();
			return ClaimsAuthorizationHelper.IsUserDepartmentAdmin() ? null : Forbid();
		}

		private static void Populate(RecordDeploymentConnectorEditView model)
		{
			model.Providers = RmsExternalOrderConnectorProviders.All.Select(p => new SelectListItem { Value = p, Text = p }).ToList();
			model.Profiles = new List<SelectListItem> { new SelectListItem { Value = "", Text = "-" } }.Concat(RmsDeploymentProfiles.All.Select(p => new SelectListItem { Value = p, Text = p })).ToList();
			model.CredentialKinds = RmsConnectorCredentialKinds.All.Select(k => new SelectListItem { Value = k, Text = k }).ToList();
			model.ConnectorsEnabled = RecordsConnectorConfig.Enabled;
			model.MinPollIntervalMinutes = RecordsConnectorConfig.MinPollIntervalMinutes;
		}

		private async Task PopulateDetailsAsync(RecordDeploymentConnectorEditView model, RmsExternalOrderConnector connector)
		{
			Populate(model);
			model.Connector = connector;
			model.Department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			model.Runs = await _connectors.GetRunsAsync(DepartmentId, UserId, connector.RmsExternalOrderConnectorId, 50);
			model.Reconciliation = await _connectors.GetReconciliationAsync(DepartmentId, UserId, connector.RmsExternalOrderConnectorId);
			var names = await _departments.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			model.PersonnelNames = names.GroupBy(n => n.UserId).ToDictionary(g => g.Key, g => g.First().Name);
		}
	}
}

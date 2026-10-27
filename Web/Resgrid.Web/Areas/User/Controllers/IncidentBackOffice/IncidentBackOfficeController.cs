using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Areas.User.Models.WorkOrders;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The module's front door (plan §9.0, §14.5): the incident selector, the activation wizard, one activation's record and
	/// lifecycle, the command desk, and the department policies. The snapshot, metric and notification tables of the command
	/// section are listed through the generic ledger actions.
	/// </summary>
	public sealed class IncidentBackOfficeController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentBackOfficeLedgerKinds _kinds;
		private readonly IIncidentLedgerService<IncidentActivation> _activations;
		private readonly IIncidentLedgerService<DepartmentIncidentAuthorityProfile> _profiles;
		private readonly IIncidentOrganizationService _organization;
		private readonly IIncidentOperationalIntelligenceService _intelligence;
		private readonly IIncidentBackOfficeSettingsService _settings;
		private readonly ICallsService _calls;

		public IncidentBackOfficeController(IncidentDeskServices desk, IncidentBackOfficeLedgerKinds kinds, IIncidentLedgerService<IncidentActivation> activations,
			IIncidentLedgerService<DepartmentIncidentAuthorityProfile> profiles, IIncidentOrganizationService organization, IIncidentOperationalIntelligenceService intelligence,
			IIncidentBackOfficeSettingsService settings, ICallsService calls) : base(desk)
		{
			_kinds = kinds;
			_activations = activations;
			_profiles = profiles;
			_organization = organization;
			_intelligence = intelligence;
			_settings = settings;
			_calls = calls;
		}

		protected override string Section => "Command";
		protected override bool RequiresActivation => false;
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		#region Selector

		[HttpGet]
		public override async Task<IActionResult> Index(CancellationToken cancellationToken = default) => await Selector(false, cancellationToken);

		[HttpGet]
		public async Task<IActionResult> Selector(bool closed = false, CancellationToken cancellationToken = default)
		{
			var view = Prepare(new IncidentSelectorView
			{
				IncludeClosed = closed,
				CanOpen = CanWrite && ClaimsAuthorizationHelper.CanActivateIncident(),
				SelectedId = Context?.Activation?.IncidentActivationId
			}, "Selector");
			view.Activations = (await Desk.Activations.GetActivationsAsync(Actor, closed, cancellationToken)).ToList();
			ViewData["IboTab"] = "IncidentBackOffice";
			return View("Index", view);
		}

		/// <summary>Remembers the activation (the base sets the cookie from <c>aid</c>) and opens its command desk.</summary>
		[HttpGet]
		public IActionResult Select(string aid)
		{
			if (Context == null) return NotFound();
			return RedirectToAction(nameof(Command), new { aid });
		}

		#endregion

		#region Open

		[HttpGet]
		public async Task<IActionResult> Open(string parent = null, int? callId = null, CancellationToken cancellationToken = default)
		{
			if (!CanWrite || !ClaimsAuthorizationHelper.CanActivateIncident()) return Forbid();
			var department = await Desk.Departments.GetDepartmentByIdAsync(DepartmentId);
			var defaults = await _settings.GetAsync<IncidentBackOfficeDefaults>(DepartmentId);
			var view = Prepare(new IncidentOpenView
			{
				Input = new IncidentActivationInput
				{
					ParentActivationId = parent,
					CallId = callId,
					TimeZoneId = department?.TimeZone,
					Currency = defaults.DefaultCurrency,
					MeasurementSystem = defaults.DefaultMeasurementSystem,
					StartActive = true,
					CreateFirstPeriod = true
				}
			}, "Open");
			await FillOpenOptionsAsync(view, cancellationToken);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Open(IncidentActivationInput input, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanActivateIncident()) return Forbid();
			var view = Prepare(new IncidentOpenView { Input = input ?? new IncidentActivationInput() }, "Open");
			if (string.IsNullOrWhiteSpace(view.Input.Name)) view.Errors["Name"] = Desk.Strings["Error.Required"].Value;
			if (string.IsNullOrWhiteSpace(view.Input.ShortCode)) view.Errors["ShortCode"] = Desk.Strings["Error.Required"].Value;
			if (!Enum.IsDefined(typeof(IncidentActivationKinds), view.Input.Kind)) view.Errors["Kind"] = Desk.Strings["Error.InvalidValue"].Value;
			if (view.Errors.Count == 0)
			{
				try
				{
					var activation = await Desk.Activations.OpenAsync(Actor, view.Input, cancellationToken);
					TempData["IboSaved"] = true;
					return RedirectToAction(nameof(Command), new { aid = activation.IncidentActivationId });
				}
				catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
				{
					view.Message = RuleText(ex);
				}
			}
			await FillOpenOptionsAsync(view, cancellationToken);
			return View(view);
		}

		private async Task FillOpenOptionsAsync(IncidentOpenView view, CancellationToken cancellationToken)
		{
			view.Calls = await CallOptionsAsync(view.Input.CallId);
			view.Parents = (await Desk.Activations.GetActivationsAsync(Actor, false, cancellationToken))
				.Where(a => a.Kind == (int)IncidentActivationKinds.Complex)
				.Select(a => new LedgerOption { Value = a.IncidentActivationId, Text = a.Name + " (" + a.ShortCode + ")" }).ToList();
			view.Profiles = (await _profiles.ListAsync(Actor, new IncidentLedgerQuery().Order("Name", false), cancellationToken))
				.Select(p => new LedgerOption { Value = p.DepartmentIncidentAuthorityProfileId, Text = p.Name + (p.IsDefault ? " *" : string.Empty) }).ToList();
			view.TimeZones = WorkOrderSettingChoices.TimeZones(view.Input.TimeZoneId).Select(z => new LedgerOption { Value = z.Id, Text = z.DisplayName }).ToList();
		}

		/// <summary>The department's active calls, plus the linked one when it has since closed.</summary>
		private async Task<List<LedgerOption>> CallOptionsAsync(int? selected)
		{
			var calls = await _calls.GetActiveCallsByDepartmentAsync(DepartmentId) ?? new List<Call>();
			var options = calls.OrderByDescending(c => c.LoggedOn).Select(c => new LedgerOption { Value = c.CallId.ToString(CultureInfo.InvariantCulture), Text = CallText(c) }).ToList();
			if (selected.HasValue && options.All(o => o.Value != selected.Value.ToString(CultureInfo.InvariantCulture)))
			{
				var call = await _calls.GetCallByIdAsync(selected.Value);
				if (call != null && call.DepartmentId == DepartmentId)
					options.Insert(0, new LedgerOption { Value = call.CallId.ToString(CultureInfo.InvariantCulture), Text = CallText(call) });
			}
			return options;
		}

		private static string CallText(Call call) => string.IsNullOrWhiteSpace(call.Number) ? call.Name : call.Number + " " + call.Name;

		#endregion

		#region Activation

		[HttpGet]
		public async Task<IActionResult> Activation(string aid, CancellationToken cancellationToken = default)
		{
			if (Context == null) return NotFound();
			var activation = Context.Activation;
			var view = Prepare(new IncidentActivationView
			{
				Activation = activation,
				CanManage = CanWrite && ClaimsAuthorizationHelper.CanActivateIncident() && activation.Status != (int)IncidentActivationStatuses.Sealed,
				CanChangeStatus = CanWrite && ClaimsAuthorizationHelper.CanCloseIncidentActivation()
			}, "Activation");
			if (view.CanChangeStatus && _activations.Lifecycle != null)
				view.NextStatuses = _activations.Lifecycle.NextStatuses(activation.Status);
			if (view.CanManage)
			{
				view.Calls = await CallOptionsAsync(activation.CallId);
				view.Parents = (await Desk.Activations.GetActivationsAsync(Actor, false, cancellationToken))
					.Where(a => a.Kind == (int)IncidentActivationKinds.Complex && a.IncidentActivationId != activation.IncidentActivationId)
					.Select(a => new LedgerOption { Value = a.IncidentActivationId, Text = a.Name + " (" + a.ShortCode + ")" }).ToList();
			}
			if (activation.CallId.HasValue)
			{
				var call = await _calls.GetCallByIdAsync(activation.CallId.Value);
				view.CallLabel = call != null && call.DepartmentId == DepartmentId ? CallText(call) : activation.CallId.Value.ToString(CultureInfo.InvariantCulture);
			}
			view.People = await Desk.PeopleAsync(DepartmentId);
			ViewData["IboTab"] = "IncidentBackOffice";
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> ChangeStatus(string aid, int status, string reason, CancellationToken cancellationToken = default)
		{
			if (Context == null) return NotFound();
			if (!ClaimsAuthorizationHelper.CanCloseIncidentActivation() || !Enum.IsDefined(typeof(IncidentActivationStatuses), status)) return Forbid();
			try
			{
				await Desk.Activations.ChangeStatusAsync(Actor, aid, (IncidentActivationStatuses)status, reason, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Activation), new { aid });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Link(string aid, int? callId, CancellationToken cancellationToken = default)
		{
			if (Context == null) return NotFound();
			if (!ClaimsAuthorizationHelper.CanActivateIncident()) return Forbid();
			try
			{
				await Desk.Activations.LinkAsync(Actor, aid, callId, null, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Activation), new { aid });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Complex(string aid, string parentActivationId, CancellationToken cancellationToken = default)
		{
			if (Context == null) return NotFound();
			if (!ClaimsAuthorizationHelper.CanActivateIncident()) return Forbid();
			try
			{
				await Desk.Activations.SetComplexAsync(Actor, aid, string.IsNullOrWhiteSpace(parentActivationId) ? null : parentActivationId, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Activation), new { aid });
		}

		#endregion

		#region Command desk

		[HttpGet]
		public async Task<IActionResult> Command(string aid = null, CancellationToken cancellationToken = default)
		{
			if (Context == null) return RedirectToAction(nameof(Index));
			var activationId = Context.Activation.IncidentActivationId;
			var view = Prepare(new IncidentCommandDeskView(), "Command");
			view.Summary = await _intelligence.GetDeskSummaryAsync(Actor, activationId, cancellationToken);
			view.Holders = (await _organization.GetHoldersAsync(Actor, activationId, DateTime.UtcNow, cancellationToken)).ToList();
			view.PositionNames = (await _organization.GetPositionsAsync(Actor, activationId, cancellationToken))
				.GroupBy(p => p.IncidentPositionDefinitionId).ToDictionary(g => g.Key, g => g.First().Code + " · " + g.First().Name, StringComparer.OrdinalIgnoreCase);
			view.People = await Desk.PeopleAsync(DepartmentId);
			ViewData["IboTab"] = "IncidentBackOffice";
			return View(view);
		}

		#endregion

		#region Settings

		private sealed class PolicyEntry
		{
			public IncidentBackOfficeArea Area { get; set; }
			public int SettingType { get; set; }
			public Func<Task<object>> Load { get; set; }
			public Func<object, Task> Save { get; set; }
		}

		private PolicyEntry Entry<T>(IncidentBackOfficeArea area) where T : class, new() => new PolicyEntry
		{
			Area = area,
			SettingType = (int)_settings.SettingTypeFor<T>(),
			Load = async () => await _settings.GetAsync<T>(DepartmentId, true),
			Save = policy => _settings.SaveAsync(DepartmentId, (T)policy, Actor)
		};

		private Dictionary<string, PolicyEntry> Policies() => new Dictionary<string, PolicyEntry>(StringComparer.Ordinal)
		{
			[nameof(IncidentBackOfficeDefaults)] = Entry<IncidentBackOfficeDefaults>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentEnrollmentPolicy)] = Entry<IncidentEnrollmentPolicy>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentNotificationRoutingPolicy)] = Entry<IncidentNotificationRoutingPolicy>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentForecastPolicy)] = Entry<IncidentForecastPolicy>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentAssignmentRulePolicy)] = Entry<IncidentAssignmentRulePolicy>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentVendorPolicy)] = Entry<IncidentVendorPolicy>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentSelfServicePolicy)] = Entry<IncidentSelfServicePolicy>(IncidentBackOfficeArea.Foundation),
			[nameof(IncidentFinancePolicy)] = Entry<IncidentFinancePolicy>(IncidentBackOfficeArea.Business),
			[nameof(IncidentRetentionPolicy)] = Entry<IncidentRetentionPolicy>(IncidentBackOfficeArea.Business),
			[nameof(CoordinationBoardDefaults)] = Entry<CoordinationBoardDefaults>(IncidentBackOfficeArea.Coordination)
		};

		private bool AreaOn(IncidentBackOfficeArea area) => area switch
		{
			IncidentBackOfficeArea.Business => Access.BusinessEnabled,
			IncidentBackOfficeArea.Coordination => Access.CoordinationEnabled,
			_ => true
		};

		[HttpGet]
		public async Task<IActionResult> Settings()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin()) return Forbid();
			var view = Prepare(new IncidentSettingsView(), "Settings");
			foreach (var (key, entry) in Policies().Where(p => AreaOn(p.Value.Area)))
				view.Sections.Add(new IncidentPolicySection { Key = key, SettingType = entry.SettingType, Fields = IncidentPolicyForm.Build(await entry.Load()) });
			ViewData["IboSection"] = "Department";
			ViewData["IboTab"] = "IncidentBackOffice";
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings(string section)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin()) return Forbid();
			var policies = Policies();
			if (string.IsNullOrWhiteSpace(section) || !policies.TryGetValue(section, out var entry) || !AreaOn(entry.Area)) return NotFound();
			var policy = await entry.Load();
			var errors = IncidentPolicyForm.Bind(policy, Request.Form);
			if (policy is IncidentFinancePolicy finance)
			{
				// Single-person operation is a recorded acknowledgment (plan §15.1): who turned it on, and when.
				var stored = (IncidentFinancePolicy)await entry.Load();
				if (finance.AllowSinglePersonOperation && !stored.AllowSinglePersonOperation)
				{
					finance.SinglePersonAcknowledgedByUserId = UserId;
					finance.SinglePersonAcknowledgedOn = DateTime.UtcNow;
				}
				else if (!finance.AllowSinglePersonOperation)
				{
					finance.SinglePersonAcknowledgedByUserId = null;
					finance.SinglePersonAcknowledgedOn = null;
				}
			}
			if (errors.Count > 0)
			{
				var view = Prepare(new IncidentSettingsView(), "Settings");
				foreach (var (key, other) in policies.Where(p => AreaOn(p.Value.Area)))
				{
					var shown = key == section ? policy : await other.Load();
					var part = new IncidentPolicySection { Key = key, SettingType = other.SettingType, Fields = IncidentPolicyForm.Build(shown) };
					if (key == section)
						foreach (var (field, error) in errors)
							part.Errors[field] = Desk.Strings["Error." + error].Value;
					view.Sections.Add(part);
				}
				ViewData["IboSection"] = "Department";
				ViewData["IboTab"] = "IncidentBackOffice";
				return View(view);
			}
			await entry.Save(policy);
			TempData["IboSaved"] = true;
			return RedirectToAction(nameof(Settings), null, section);
		}

		#endregion
	}
}

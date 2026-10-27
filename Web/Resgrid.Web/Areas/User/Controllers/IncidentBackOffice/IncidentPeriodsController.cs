using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Operational periods and the plan (plan §9.1): create, start (the previous current period closes), copy forward, freeze, and
	/// assemble then approve the plan version. Objectives, weather blocks and plan versions are edited through the generic ledger.
	/// </summary>
	public sealed class IncidentPeriodsController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentPeriodsLedgerKinds _kinds;
		private readonly IIncidentPlanningService _planning;
		private readonly IIncidentLedgerService<IncidentPlanVersion> _plans;

		public IncidentPeriodsController(IncidentDeskServices desk, IncidentPeriodsLedgerKinds kinds, IIncidentPlanningService planning, IIncidentLedgerService<IncidentPlanVersion> plans) : base(desk)
		{
			_kinds = kinds;
			_planning = planning;
			_plans = plans;
		}

		protected override string Section => "Planning";
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		private bool CanManage => CanWrite && ClaimsAuthorizationHelper.CanManageIncidentPeriods();
		private bool CanPlan => CanWrite && ClaimsAuthorizationHelper.CanManageIncidentIap();

		[HttpGet]
		public override async Task<IActionResult> Index(CancellationToken cancellationToken = default)
		{
			var activationId = Context.Activation.IncidentActivationId;
			var view = Prepare(new IncidentPeriodsView { CanManage = CanManage, CanPlan = CanPlan });
			view.Periods = (await _planning.GetPeriodsAsync(Actor, activationId, cancellationToken)).OrderByDescending(p => p.Number).ToList();
			view.Plans = (await _plans.ListAsync(Actor, new Model.Repositories.IncidentLedgerQuery { ActivationIds = new[] { activationId } }.Order("VersionNumber", true), cancellationToken)).ToList();
			var last = view.Periods.FirstOrDefault();
			var hours = 12;
			view.NextStart = Time.Input(last?.EndOn ?? DateTime.UtcNow);
			view.NextEnd = Time.Input((last?.EndOn ?? DateTime.UtcNow).AddHours(last != null ? (last.EndOn - last.StartOn).TotalHours : hours));
			view.People = await Desk.PeopleAsync(DepartmentId);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Create(string startOn, string endOn, string briefingOn, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentPeriods()) return Forbid();
			var start = Time.Parse(startOn);
			var end = Time.Parse(endOn);
			if (!start.HasValue || !end.HasValue || end <= start)
			{
				TempData["IboMessage"] = Desk.Strings["Error.InvalidPeriodWindow"].Value;
				return RedirectToAction(nameof(Index));
			}
			return await RunAsync(() => _planning.CreatePeriodAsync(Actor, Context.Activation.IncidentActivationId, start.Value, end.Value, Time.Parse(briefingOn), cancellationToken));
		}

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Start(string id, CancellationToken cancellationToken = default) =>
			ClaimsAuthorizationHelper.CanManageIncidentPeriods() ? RunAsync(() => _planning.StartPeriodAsync(Actor, id, cancellationToken)) : Task.FromResult<IActionResult>(Forbid());

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> CopyForward(string id, CancellationToken cancellationToken = default) =>
			ClaimsAuthorizationHelper.CanManageIncidentPeriods() ? RunAsync(() => _planning.CopyForwardAsync(Actor, id, cancellationToken)) : Task.FromResult<IActionResult>(Forbid());

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Freeze(string id, bool approve = false, CancellationToken cancellationToken = default) =>
			ClaimsAuthorizationHelper.CanManageIncidentPeriods() ? RunAsync(() => _planning.FreezeAsync(Actor, id, approve, cancellationToken)) : Task.FromResult<IActionResult>(Forbid());

		/// <summary>Assembles a plan version from the period's current IAP records (the manifest is read by the planning service).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Assemble(string id, int printCopies = 0, CancellationToken cancellationToken = default) =>
			ClaimsAuthorizationHelper.CanManageIncidentIap()
				? RunAsync(() => _planning.AssemblePlanAsync(Actor, id, new IncidentPlanManifest { PrintCopies = Math.Clamp(printCopies, 0, 999) }, cancellationToken))
				: Task.FromResult<IActionResult>(Forbid());

		[HttpPost, ValidateAntiForgeryToken]
		public Task<IActionResult> Approve(string planVersionId, CancellationToken cancellationToken = default) =>
			ClaimsAuthorizationHelper.CanManageIncidentIap() ? RunAsync(() => _planning.ApprovePlanAsync(Actor, planVersionId, cancellationToken)) : Task.FromResult<IActionResult>(Forbid());

		private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action)
		{
			try
			{
				await action();
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Index));
		}
	}
}

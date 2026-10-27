using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Standing incident management teams (plan §9.2): rosters through the generic ledger; computed readiness for a window (never
	/// stored) and team activation into an incident, where each slot's chosen candidate becomes a position assignment in one
	/// transaction and conflicts are reported instead of double-booked.
	/// </summary>
	public sealed class IncidentTeamsController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentTeamsLedgerKinds _kinds;
		private readonly IIncidentOrganizationService _organization;
		private readonly IIncidentLedgerService<DepartmentIncidentTeam> _teams;

		public IncidentTeamsController(IncidentDeskServices desk, IncidentTeamsLedgerKinds kinds, IIncidentOrganizationService organization,
			IIncidentLedgerService<DepartmentIncidentTeam> teams) : base(desk)
		{
			_kinds = kinds;
			_organization = organization;
			_teams = teams;
		}

		protected override string Section => "Department";
		protected override bool RequiresActivation => false;
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		protected override IEnumerable<LedgerDeskAction> DeskActions()
		{
			yield return new LedgerDeskAction { Action = nameof(Readiness), LabelKey = "TeamReadiness", Icon = "fa-check-square-o", Primary = true };
		}

		protected override IEnumerable<LedgerDeskAction> RowActions(LedgerKindDescriptor kind, LedgerEditView row)
		{
			if (kind.EntityType == typeof(DepartmentIncidentTeam))
				yield return new LedgerDeskAction { Action = nameof(ReadinessFor), LabelKey = "TeamReadiness", Icon = "fa-check-square-o" };
		}

		/// <summary>Row action target: a POST from the team's record opens its readiness page.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public IActionResult ReadinessFor(string id) => RedirectToAction(nameof(Readiness), new { teamId = id });

		[HttpGet]
		public async Task<IActionResult> Readiness(string teamId = null, string start = null, string end = null, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanViewIncidentPosition()) return Forbid();
			var view = Prepare(new IncidentTeamReadinessView { TeamId = teamId });
			view.Teams = (await _teams.ListAsync(Actor, new IncidentLedgerQuery().Order("Name", false), cancellationToken)).Where(t => t.IsActive).ToList();
			var from = Time.Parse(start) ?? DateTime.UtcNow;
			var until = Time.Parse(end) ?? from.AddDays(14);
			if (until <= from) until = from.AddDays(14);
			view.WindowStart = Time.Input(from);
			view.WindowEnd = Time.Input(until);
			if (!string.IsNullOrWhiteSpace(teamId))
			{
				try
				{
					view.Readiness = await _organization.GetTeamReadinessAsync(Actor, teamId, from, until, cancellationToken);
				}
				catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
				{
					view.Message = RuleText(ex);
				}
				view.CanActivate = CanWrite && ClaimsAuthorizationHelper.CanCreateIncidentPosition() && view.Readiness != null;
				if (view.CanActivate)
					view.Activations = (await Desk.Activations.GetActivationsAsync(Actor, false, cancellationToken))
						.Select(a => new LedgerOption { Value = a.IncidentActivationId, Text = a.Name + " (" + a.ShortCode + ")" }).ToList();
			}
			return View(view);
		}

		[HttpGet]
		public async Task<IActionResult> Propose(string teamId, string activationId, CancellationToken cancellationToken = default)
		{
			if (!CanWrite || !ClaimsAuthorizationHelper.CanCreateIncidentPosition()) return Forbid();
			var view = Prepare(new IncidentTeamReadinessView { TeamId = teamId, ActivationId = activationId, CanActivate = true });
			try
			{
				view.Proposals = (await _organization.ProposeTeamActivationAsync(Actor, teamId, activationId, cancellationToken)).ToList();
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
				return RedirectToAction(nameof(Readiness), new { teamId });
			}
			view.Teams = (await _teams.ListAsync(Actor, new IncidentLedgerQuery().Order("Name", false), cancellationToken)).ToList();
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Activate(string teamId, string activationId, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanCreateIncidentPosition()) return Forbid();
			var choices = Request.Form.Keys.Where(k => k.StartsWith("slot_", StringComparison.Ordinal))
				.Select(k => new IncidentTeamActivationChoice { SlotId = k.Substring(5), CandidateId = string.IsNullOrWhiteSpace(Request.Form[k]) ? null : Request.Form[k].ToString() })
				.Take(500).ToList();
			try
			{
				var context = await Desk.Activations.GetContextAsync(Actor, activationId, cancellationToken);
				if (context == null) return NotFound();
				var result = await _organization.ActivateTeamAsync(Actor, teamId, activationId, context.CurrentPeriod?.IncidentOperationalPeriodId, choices, cancellationToken);
				TempData["IboSaved"] = true;
				TempData["IboMessage"] = Desk.Strings["TeamActivated", result.Assigned, result.Gaps, result.Conflicts.Count].Value;
				return RedirectToAction("Index", "IncidentOrganization", new { aid = activationId });
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
				return RedirectToAction(nameof(Propose), new { teamId, activationId });
			}
		}
	}
}

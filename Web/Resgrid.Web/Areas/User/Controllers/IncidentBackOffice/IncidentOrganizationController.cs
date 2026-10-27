using System;
using System.Collections.Generic;
using System.Globalization;
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
	/// The incident organization (plan §9.2): nodes through the generic ledger; assignments through the organization service so the
	/// qualification check, the conflict check and trigger 118 run on every assignment, never on a raw row write.
	/// </summary>
	public sealed class IncidentOrganizationController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentOrganizationLedgerKinds _kinds;
		private readonly IIncidentOrganizationService _organization;
		private readonly IIncidentPlanningService _planning;
		private readonly IIncidentLedgerService<IncidentOrganizationNode> _nodes;

		public IncidentOrganizationController(IncidentDeskServices desk, IncidentOrganizationLedgerKinds kinds, IIncidentOrganizationService organization,
			IIncidentPlanningService planning, IIncidentLedgerService<IncidentOrganizationNode> nodes) : base(desk)
		{
			_kinds = kinds;
			_organization = organization;
			_planning = planning;
			_nodes = nodes;
		}

		protected override string Section => "Planning";
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		protected override IEnumerable<LedgerDeskAction> DeskActions()
		{
			if (CanWrite && ClaimsAuthorizationHelper.CanCreateIncidentPosition())
				yield return new LedgerDeskAction { Action = nameof(Assign), LabelKey = "AssignPosition", Icon = "fa-user-plus", Primary = true };
		}

		protected override IEnumerable<LedgerDeskAction> RowActions(LedgerKindDescriptor kind, LedgerEditView row)
		{
			if (kind.EntityType == typeof(IncidentPositionAssignment) && ClaimsAuthorizationHelper.CanManageIncidentPosition() && string.IsNullOrEmpty(row.Values.GetValueOrDefault("EndOn")))
				yield return new LedgerDeskAction { Action = nameof(EndAssignment), LabelKey = "EndAssignment", Icon = "fa-stop-circle", ConfirmKey = "ConfirmEndAssignment" };
		}

		/// <summary>Assignments are made here, not through the generic editor (which would bypass the checks).</summary>
		public override Task<IActionResult> Edit(string kind, string id = null, bool reveal = false, string pf = null, string pv = null, CancellationToken cancellationToken = default)
		{
			if (string.Equals(kind, nameof(IncidentPositionAssignment), StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(id))
				return Task.FromResult<IActionResult>(RedirectToAction(nameof(Assign)));
			return base.Edit(kind, id, reveal, pf, pv, cancellationToken);
		}

		[HttpGet]
		public async Task<IActionResult> Assign(string positionId = null, CancellationToken cancellationToken = default)
		{
			if (!CanWrite || !ClaimsAuthorizationHelper.CanCreateIncidentPosition()) return Forbid();
			var view = Prepare(new IncidentAssignView
			{
				Assignment = new IncidentPositionAssignment { IncidentPositionDefinitionId = positionId, IncidentOperationalPeriodId = Context.CurrentPeriod?.IncidentOperationalPeriodId },
				StartOn = Time.Input(DateTime.UtcNow)
			});
			await FillAsync(view, cancellationToken);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Assign(string incidentPositionDefinitionId, string userId, string placeholderLabel, int assignmentKind, int shift, string startOn,
			string incidentOperationalPeriodId, string incidentOrganizationNodeId, bool isTrainee = false, bool overrideQualification = false, string qualificationNote = null,
			CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanCreateIncidentPosition()) return Forbid();
			var view = Prepare(new IncidentAssignView
			{
				Assignment = new IncidentPositionAssignment
				{
					DepartmentId = DepartmentId,
					IncidentActivationId = Context.Activation.IncidentActivationId,
					IncidentPositionDefinitionId = incidentPositionDefinitionId,
					IncidentOperationalPeriodId = string.IsNullOrWhiteSpace(incidentOperationalPeriodId) ? null : incidentOperationalPeriodId,
					IncidentOrganizationNodeId = string.IsNullOrWhiteSpace(incidentOrganizationNodeId) ? null : incidentOrganizationNodeId,
					SubjectType = string.IsNullOrWhiteSpace(userId) ? (int)IncidentAssignmentSubjectTypes.Placeholder : (int)IncidentAssignmentSubjectTypes.User,
					UserId = string.IsNullOrWhiteSpace(userId) ? null : userId,
					PlaceholderLabel = string.IsNullOrWhiteSpace(placeholderLabel) ? null : placeholderLabel.Trim(),
					AssignmentKind = Enum.IsDefined(typeof(IncidentAssignmentKinds), assignmentKind) ? assignmentKind : 0,
					Shift = Enum.IsDefined(typeof(IncidentShiftPatterns), shift) ? shift : 0,
					IsTrainee = isTrainee,
					QualificationNote = string.IsNullOrWhiteSpace(qualificationNote) ? null : qualificationNote.Trim()
				},
				StartOn = startOn
			});
			var start = Time.Parse(startOn);
			if (string.IsNullOrWhiteSpace(incidentPositionDefinitionId)) view.Errors["IncidentPositionDefinitionId"] = Desk.Strings["Error.Required"].Value;
			if (!start.HasValue) view.Errors["StartOn"] = Desk.Strings["Error.InvalidDate"].Value;
			if (view.Assignment.UserId == null && view.Assignment.PlaceholderLabel == null) view.Errors["UserId"] = Desk.Strings["Error.Required"].Value;
			if (view.Errors.Count == 0)
			{
				view.Assignment.StartOn = start.Value;
				try
				{
					var result = await _organization.AssignAsync(Actor, view.Assignment, overrideQualification && ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), cancellationToken);
					TempData["IboSaved"] = true;
					if (result.Conflicts.Count > 0)
						TempData["IboMessage"] = Desk.Strings["AssignedWithConflicts", result.Conflicts.Count].Value;
					return RedirectToAction(nameof(Edit), new { kind = nameof(IncidentPositionAssignment), id = result.Assignment.IncidentPositionAssignmentId });
				}
				catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
				{
					view.Message = RuleText(ex);
				}
			}
			await FillAsync(view, cancellationToken);
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> EndAssignment(string id, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentPosition()) return Forbid();
			try
			{
				await _organization.EndAssignmentAsync(Actor, id, DateTime.UtcNow, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Edit), new { kind = nameof(IncidentPositionAssignment), id });
		}

		private async Task FillAsync(IncidentAssignView view, CancellationToken cancellationToken)
		{
			var activationId = Context.Activation.IncidentActivationId;
			view.CanOverride = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			view.Positions = (await _organization.GetPositionsAsync(Actor, activationId, cancellationToken)).Where(p => p.IsActive)
				.OrderBy(p => p.Section).ThenBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
				.Select(p => new LedgerOption { Value = p.IncidentPositionDefinitionId, Text = p.Code + " · " + p.Name }).ToList();
			view.People = (await Desk.PeopleAsync(DepartmentId)).OrderBy(p => p.Value).Select(p => new LedgerOption { Value = p.Key, Text = p.Value }).ToList();
			view.Periods = (await _planning.GetPeriodsAsync(Actor, activationId, cancellationToken)).OrderByDescending(p => p.Number)
				.Select(p => new LedgerOption { Value = p.IncidentOperationalPeriodId, Text = p.Number.ToString(CultureInfo.InvariantCulture) + " · " + Time.Format(p.StartOn) }).ToList();
			view.Nodes = (await _nodes.ListAsync(Actor, new IncidentLedgerQuery { ActivationIds = Scope }.Order("Name", false), cancellationToken))
				.Select(n => new LedgerOption { Value = n.IncidentOrganizationNodeId, Text = n.Name }).ToList();
		}
	}
}

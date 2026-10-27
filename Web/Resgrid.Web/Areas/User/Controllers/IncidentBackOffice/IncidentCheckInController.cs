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
	/// The check-in desk (plan §9.5): find a participant by the code on their badge or confirmation, or pick them from the expected
	/// list, and check them in (trigger 119). Resources, their components, movements, status history and extensions are listed here
	/// through the generic ledger.
	/// </summary>
	public sealed class IncidentCheckInController : IncidentBackOfficeControllerBase
	{
		private readonly IncidentCheckInLedgerKinds _kinds;
		private readonly IIncidentParticipantsService _participants;
		private readonly IIncidentLedgerService<IncidentParticipant> _participantRows;

		public IncidentCheckInController(IncidentDeskServices desk, IncidentCheckInLedgerKinds kinds, IIncidentParticipantsService participants,
			IIncidentLedgerService<IncidentParticipant> participantRows) : base(desk)
		{
			_kinds = kinds;
			_participants = participants;
			_participantRows = participantRows;
		}

		protected override string Section => "Planning";
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		[HttpGet]
		public override async Task<IActionResult> Index(CancellationToken cancellationToken = default) => View(await DeskAsync(null, null, cancellationToken));

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Find(string code, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanViewIncidentParticipant()) return Forbid();
			var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
			IncidentParticipant participant = null;
			if (normalized.Length is > 0 and <= 32)
				participant = await _participants.FindByCheckInCodeAsync(Actor, normalized, cancellationToken);
			if (participant != null && !Scope.Contains(participant.IncidentActivationId, StringComparer.OrdinalIgnoreCase))
				participant = null;
			var view = await DeskAsync(normalized, participant, cancellationToken);
			view.NotFound = participant == null;
			return View(nameof(Index), view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CheckIn(string participantId, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentParticipant()) return Forbid();
			try
			{
				await _participants.CheckInAsync(Actor, participantId, IncidentCheckInSources.Scan, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Index));
		}

		private async Task<IncidentCheckInDeskView> DeskAsync(string code, IncidentParticipant participant, CancellationToken cancellationToken)
		{
			var view = Prepare(new IncidentCheckInDeskView
			{
				Code = code,
				Participant = participant,
				CanCheckIn = CanWrite && ClaimsAuthorizationHelper.CanManageIncidentParticipant()
			});
			if (ClaimsAuthorizationHelper.CanViewIncidentParticipant())
			{
				var expected = new IncidentLedgerQuery { ActivationIds = Scope }
					.WhereIn("SupportStatus", new[] { (int)IncidentSupportStatuses.Ordered, (int)IncidentSupportStatuses.Mobilizing, (int)IncidentSupportStatuses.EnRoute })
					.Order("DisplayName", false)
					.Page(0, 200);
				view.Expected = (await _participantRows.ListAsync(Actor, expected, cancellationToken)).ToList();
			}
			return view;
		}
	}
}

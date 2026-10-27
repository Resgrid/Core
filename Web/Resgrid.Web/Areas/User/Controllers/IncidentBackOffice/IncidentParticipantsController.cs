using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.IncidentBackOffice;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Participants (plan §9.2, §9.5): the enrollment queue of operational subjects without a support record, roster import (stage,
	/// review, commit), qualification snapshots and desk check-in of one participant. Rows are edited through the generic ledger.
	/// </summary>
	public sealed class IncidentParticipantsController : IncidentBackOfficeControllerBase
	{
		private const int MaxRosterBytes = 2 * 1024 * 1024;

		private readonly IncidentParticipantsLedgerKinds _kinds;
		private readonly IIncidentParticipantsService _participants;
		private readonly IIncidentSelfServiceService _selfService;

		public IncidentParticipantsController(IncidentDeskServices desk, IncidentParticipantsLedgerKinds kinds, IIncidentParticipantsService participants,
			IIncidentSelfServiceService selfService) : base(desk)
		{
			_kinds = kinds;
			_participants = participants;
			_selfService = selfService;
		}

		protected override string Section => "Planning";
		protected override IReadOnlyList<ILedgerKind> Kinds => _kinds.All;

		private bool CanEnroll => CanWrite && ClaimsAuthorizationHelper.CanCreateIncidentParticipant();

		protected override IEnumerable<LedgerDeskAction> DeskActions()
		{
			yield return new LedgerDeskAction { Action = nameof(Enrollment), LabelKey = "EnrollmentQueue", Icon = "fa-inbox", Primary = true };
			if (CanEnroll)
				yield return new LedgerDeskAction { Action = nameof(Import), LabelKey = "RosterImport", Icon = "fa-upload" };
		}

		protected override IEnumerable<LedgerDeskAction> RowActions(LedgerKindDescriptor kind, LedgerEditView row)
		{
			if (kind.EntityType == typeof(IncidentSelfServiceLink) && ClaimsAuthorizationHelper.CanManageIncidentParticipant() && string.IsNullOrEmpty(row.Values.GetValueOrDefault("RevokedOn")))
				yield return new LedgerDeskAction { Action = nameof(RevokeLink), LabelKey = "RevokeLink", Icon = "fa-ban", ConfirmKey = "ConfirmRevokeLink" };
			if (kind.EntityType != typeof(IncidentParticipant) || !ClaimsAuthorizationHelper.CanManageIncidentParticipant())
				yield break;
			yield return new LedgerDeskAction { Action = nameof(IssueLink), LabelKey = "IssueSelfServiceLink", Icon = "fa-link" };
			if (string.IsNullOrEmpty(row.Values.GetValueOrDefault("CheckedInOn")))
				yield return new LedgerDeskAction { Action = nameof(CheckIn), LabelKey = "CheckIn", Icon = "fa-sign-in", Primary = true };
			yield return new LedgerDeskAction { Action = nameof(SnapshotQualifications), LabelKey = "SnapshotQualifications", Icon = "fa-certificate" };
		}

		#region Enrollment

		[HttpGet]
		public async Task<IActionResult> Enrollment(CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanViewIncidentParticipant()) return Forbid();
			var view = Prepare(new IncidentEnrollmentView { CanEnroll = CanEnroll });
			view.Candidates = (await _participants.GetEnrollmentQueueAsync(Actor, Context.Activation.IncidentActivationId, cancellationToken)).ToList();
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> Enroll(List<string> keys, bool asDraft = false, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanCreateIncidentParticipant()) return Forbid();
			var selected = (keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(500).ToList();
			if (selected.Count == 0)
			{
				TempData["IboMessage"] = Desk.Strings["NothingSelected"].Value;
				return RedirectToAction(nameof(Enrollment));
			}
			try
			{
				var result = await _participants.EnrollAsync(Actor, Context.Activation.IncidentActivationId, selected, asDraft, cancellationToken);
				TempData["IboSaved"] = true;
				if (result.Skipped > 0)
					TempData["IboMessage"] = Desk.Strings["EnrollSkipped", result.Skipped].Value;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Enrollment));
		}

		#endregion

		#region Roster import

		[HttpGet]
		public IActionResult Import()
		{
			if (!CanEnroll) return Forbid();
			return View(Prepare(new IncidentRosterImportView()));
		}

		/// <summary>Stages the roster (file or pasted text) and shows each row with its errors; nothing is written.</summary>
		[HttpPost, ValidateAntiForgeryToken]
		[RequestSizeLimit(MaxRosterBytes + 64 * 1024)]
		public async Task<IActionResult> Import(IFormFile file, string csv, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanCreateIncidentParticipant()) return Forbid();
			var view = Prepare(new IncidentRosterImportView());
			var text = await ReadRosterAsync(file, csv, cancellationToken);
			if (text == null)
			{
				view.Message = Desk.Strings["Error.RosterTooLarge"].Value;
				return View(view);
			}
			view.Csv = text;
			try
			{
				view.Stage = await _participants.StageRosterImportAsync(Actor, Context.Activation.IncidentActivationId, text, cancellationToken);
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				view.Message = RuleText(ex);
			}
			return View(view);
		}

		[HttpPost, ValidateAntiForgeryToken]
		[RequestSizeLimit(MaxRosterBytes + 64 * 1024)]
		public async Task<IActionResult> CommitImport(string csv, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanCreateIncidentParticipant()) return Forbid();
			if (string.IsNullOrWhiteSpace(csv) || csv.Length > MaxRosterBytes) return RedirectToAction(nameof(Import));
			try
			{
				var committed = await _participants.CommitRosterImportAsync(Actor, Context.Activation.IncidentActivationId, csv, cancellationToken);
				TempData["IboSaved"] = true;
				TempData["IboMessage"] = Desk.Strings["RosterCommitted", committed].Value;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
				return RedirectToAction(nameof(Import));
			}
			return RedirectToAction(nameof(List), new { kind = nameof(IncidentParticipant) });
		}

		private static async Task<string> ReadRosterAsync(IFormFile file, string csv, CancellationToken cancellationToken)
		{
			if (file != null && file.Length > 0)
			{
				if (file.Length > MaxRosterBytes) return null;
				using var reader = new StreamReader(file.OpenReadStream(), detectEncodingFromByteOrderMarks: true);
				return await reader.ReadToEndAsync(cancellationToken);
			}
			csv ??= string.Empty;
			return csv.Length > MaxRosterBytes ? null : csv;
		}

		#endregion

		#region Row actions

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> CheckIn(string id, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentParticipant()) return Forbid();
			try
			{
				await _participants.CheckInAsync(Actor, id, IncidentCheckInSources.Desk, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Edit), new { kind = nameof(IncidentParticipant), id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> SnapshotQualifications(string id, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentParticipant()) return Forbid();
			try
			{
				var count = await _participants.SnapshotQualificationsAsync(Actor, id, cancellationToken);
				TempData["IboSaved"] = true;
				TempData["IboMessage"] = Desk.Strings["QualificationsSnapshotted", count].Value;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Edit), new { kind = nameof(IncidentParticipant), id });
		}

		/// <summary>Issues a self-service link and shows it once; only its hash is stored (plan §9.28).</summary>
		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> IssueLink(string id, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentParticipant()) return Forbid();
			try
			{
				var issue = await _selfService.IssueAsync(Actor, id, IncidentSelfServicePurposes.View | IncidentSelfServicePurposes.Lodging, cancellationToken);
				var url = $"{(Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/')}/incident-self-service/{issue.Token}";
				TempData["IboNotice"] = Desk.Strings["SelfServiceLinkIssued", url, Time.Format(issue.ExpiresOn)].Value;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Edit), new { kind = nameof(IncidentParticipant), id });
		}

		[HttpPost, ValidateAntiForgeryToken]
		public async Task<IActionResult> RevokeLink(string id, CancellationToken cancellationToken = default)
		{
			if (!ClaimsAuthorizationHelper.CanManageIncidentParticipant()) return Forbid();
			try
			{
				await _selfService.RevokeAsync(Actor, id, cancellationToken);
				TempData["IboSaved"] = true;
			}
			catch (Exception ex) when (ex is IncidentRuleException or IncidentConcurrencyException)
			{
				TempData["IboMessage"] = RuleText(ex);
			}
			return RedirectToAction(nameof(Edit), new { kind = nameof(IncidentSelfServiceLink), id });
		}

		#endregion
	}
}

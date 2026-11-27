using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.ContentSets;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Round 10 A3 on the web: protocol sets (Protocols P5 regional publisher/subscriber) and course sets (Training T5) over Department Links,
	/// the pending-update inbox with its redline, customizing a copy's open blocks, proposals upstream, LMS sponsorship and the update time
	/// setting. Every page is scoped to the caller's department; managing needs Manage Content Sets (permission 136) and the services re-check
	/// it, the link and the department on every call. Every POST carries antiforgery.
	/// </summary>
	[Area("User")]
	[Authorize]
	[DepartmentLocalTime]
	public class ContentSetsController : SecureBaseController
	{
		private const string MessageKey = "ContentSetMessage";
		private const string ErrorKey = "ContentSetError";

		private readonly IContentSetService _sets;
		private readonly ILmsSponsorshipService _sponsorships;
		private readonly IDepartmentLinksService _links;
		private readonly IPersonnelRolesService _roles;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ContentSets.ContentSets> _text;

		public ContentSetsController(IContentSetService sets, ILmsSponsorshipService sponsorships, IDepartmentLinksService links, IPersonnelRolesService roles,
			IStringLocalizer<Resgrid.Localization.Areas.User.ContentSets.ContentSets> text)
		{
			_sets = sets;
			_sponsorships = sponsorships;
			_links = links;
			_roles = roles;
			_text = text;
		}

		private string Text(string key) => _text[key].Value;

		private void Say(string key) { if (TempData != null) TempData[MessageKey] = Text(key); }

		private void Fail(string message) { if (TempData != null) TempData[ErrorKey] = message; }

		private void Fail(ContentSetErrors error, string reason = null) =>
			Fail(Text("Error" + error) + (string.IsNullOrWhiteSpace(reason) ? string.Empty : " (" + ReasonText(reason) + ")"));

		private string ReasonText(string reason)
		{
			var key = "Problem_" + reason;
			var value = _text[key];
			return value.ResourceNotFound ? reason : value.Value;
		}

		private static ContentSetKinds KindOf(int kind) => kind == (int)ContentSetKinds.Training ? ContentSetKinds.Training : ContentSetKinds.Protocols;

		private Task<ContentSetActor> ActorAsync() => _sets.GetActorAsync(DepartmentId, UserId);

		private async Task<T> FillAsync<T>(T model, ContentSetActor actor, ContentSetKinds kind) where T : ContentSetPageModel
		{
			model.Kind = kind;
			model.CanManage = actor.CanManage;
			model.Message ??= TempData?[MessageKey] as string;
			model.Error ??= TempData?[ErrorKey] as string;
			model.OpenUpdates = (await _sets.GetInboxAsync(actor, true)).Count(r => r.Update.Status == (int)ContentSetUpdateStatuses.Pending);
			return model;
		}

		private DateTime? ParseLocal(string value)
		{
			var time = ViewData[nameof(DepartmentTime)] as DepartmentTime;
			return time?.Parse(value);
		}

		#region Overview and publisher

		[HttpGet]
		public async Task<IActionResult> Index(int kind = 0)
		{
			var actor = await ActorAsync();
			if (!actor.IsCurrentMember)
				return Unauthorized();

			var setKind = KindOf(kind);
			var model = new ContentSetIndexModel
			{
				Published = await _sets.GetPublishedSetsAsync(actor, setKind),
				Available = await _sets.GetAvailableSetsAsync(actor, setKind),
				Subscriptions = (await _sets.GetSubscriptionsAsync(actor)).Where(s => s.Set != null && s.Set.Kind == (int)setKind).ToList()
			};
			return View(await FillAsync(model, actor, setKind));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveSet(int id, int kind, string name, string description, int lockedDeferDays, CancellationToken cancellationToken)
		{
			var result = await _sets.SaveSetAsync(await ActorAsync(), id, KindOf(kind), name, description, lockedDeferDays, cancellationToken);
			if (!result.Succeeded)
			{
				Fail(result.Error);
				return id > 0 ? RedirectToAction("Set", new { id }) : RedirectToAction("Index", new { kind });
			}

			Say("SetSaved");
			return RedirectToAction("Set", new { id = result.Set.ContentSetId });
		}

		[HttpGet]
		public async Task<IActionResult> Set(int id)
		{
			var actor = await ActorAsync();
			var detail = await _sets.GetSetAsync(actor, id);
			if (detail == null)
				return Unauthorized();

			var model = new ContentSetEditModel { Detail = detail, Candidates = detail.Set.IsArchived ? new List<ContentSetCandidate>() : await _sets.GetCandidatesAsync(actor, id) };
			return View(await FillAsync(model, actor, (ContentSetKinds)detail.Set.Kind));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AddItem(int setId, string item, int lockMode, List<string> keys, CancellationToken cancellationToken)
		{
			// The picker posts "{kind}:{id}" so one list can offer courses and trainings together.
			var parts = (item ?? string.Empty).Split(':');
			if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var kind) ||
				!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId))
			{
				Fail(ContentSetErrors.InvalidInput);
				return RedirectToAction("Set", new { id = setId });
			}

			var result = await _sets.AddItemAsync(await ActorAsync(), setId, (ContentSetItemKinds)kind, itemId, (ContentSetLockModes)lockMode, keys, cancellationToken);
			if (result.Succeeded) Say("ItemAdded"); else Fail(result.Error, result.Reason);
			return RedirectToAction("Set", new { id = setId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> UpdateItem(int setId, int itemId, int lockMode, List<string> keys, CancellationToken cancellationToken)
		{
			var result = await _sets.UpdateItemAsync(await ActorAsync(), itemId, (ContentSetLockModes)lockMode, keys, cancellationToken);
			if (result.Succeeded) Say("ItemSaved"); else Fail(result.Error, result.Reason);
			return RedirectToAction("Set", new { id = setId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveItem(int setId, int itemId, CancellationToken cancellationToken)
		{
			var result = await _sets.RemoveItemAsync(await ActorAsync(), itemId, cancellationToken);
			if (result.Succeeded) Say("ItemRemoved"); else Fail(result.Error);
			return RedirectToAction("Set", new { id = setId });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ArchiveSet(int id, CancellationToken cancellationToken)
		{
			var result = await _sets.ArchiveSetAsync(await ActorAsync(), id, cancellationToken);
			if (result.Succeeded) Say("SetArchived"); else Fail(result.Error);
			return RedirectToAction("Set", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RemoveSubscriber(int setId, int subscriptionId, CancellationToken cancellationToken)
		{
			var result = await _sets.RemoveSubscriberAsync(await ActorAsync(), subscriptionId, cancellationToken);
			if (result.Succeeded) Say("SubscriberRemoved"); else Fail(result.Error);
			return RedirectToAction("Set", new { id = setId });
		}

		#endregion Overview and publisher

		#region Subscriber

		[HttpGet]
		public async Task<IActionResult> Available(int id)
		{
			var actor = await ActorAsync();
			var set = await _sets.GetAvailableSetAsync(actor, id);
			if (set == null)
				return Unauthorized();
			return View(await FillAsync(new ContentSetAvailableModel { Set = set }, actor, (ContentSetKinds)set.Set.Kind));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Subscribe(int id, bool autoAccept, CancellationToken cancellationToken)
		{
			var result = await _sets.SubscribeAsync(await ActorAsync(), id, autoAccept, cancellationToken);
			if (!result.Succeeded)
			{
				Fail(result.Error);
				return RedirectToAction("Available", new { id });
			}

			if (result.Skipped.Count > 0)
				Fail(Text("SubscribedWithSkipped") + " " + string.Join(", ", result.Skipped));
			else
				Say("Subscribed");
			return RedirectToAction("Index", new { kind = result.Set.Kind });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Unsubscribe(int id, int kind, CancellationToken cancellationToken)
		{
			var result = await _sets.UnsubscribeAsync(await ActorAsync(), id, cancellationToken);
			if (result.Succeeded) Say("Unsubscribed"); else Fail(result.Error);
			return RedirectToAction("Index", new { kind });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AutoAccept(int id, int kind, bool autoAccept, CancellationToken cancellationToken)
		{
			var result = await _sets.SetAutoAcceptAsync(await ActorAsync(), id, autoAccept, cancellationToken);
			if (result.Succeeded) Say("SubscriptionSaved"); else Fail(result.Error);
			return RedirectToAction("Index", new { kind });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Sync(int id, int kind, CancellationToken cancellationToken)
		{
			var result = await _sets.SyncAsync(await ActorAsync(), id, cancellationToken);
			if (!result.Succeeded) Fail(result.Error);
			else if (result.Skipped.Count > 0) Fail(Text("SubscribedWithSkipped") + " " + string.Join(", ", result.Skipped));
			else Say("Synced");
			return RedirectToAction("Index", new { kind });
		}

		[HttpGet]
		public async Task<IActionResult> Inbox(bool all = false)
		{
			var actor = await ActorAsync();
			if (!actor.IsCurrentMember)
				return Unauthorized();
			var model = new ContentSetInboxModel { Rows = await _sets.GetInboxAsync(actor, !all), ShowAll = all };
			return View(await FillAsync(model, actor, ContentSetKinds.Protocols));
		}

		[HttpGet]
		public async Task<IActionResult> Update(int id)
		{
			var actor = await ActorAsync();
			var view = await _sets.GetUpdateAsync(actor, id);
			if (view == null)
				return Unauthorized();
			return View(await FillAsync(new ContentSetUpdateModel { View = view }, actor, ContentSetKinds.Protocols));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Accept(int id, string applyOn, CancellationToken cancellationToken)
		{
			var result = await _sets.AcceptUpdateAsync(await ActorAsync(), id, ParseLocal(applyOn), cancellationToken);
			if (result.Succeeded) Say(result.Update?.Status == (int)ContentSetUpdateStatuses.Applied ? "UpdateApplied" : "UpdateScheduled"); else Fail(result.Error);
			return RedirectToAction("Update", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Defer(int id, string until, CancellationToken cancellationToken)
		{
			var parsed = ParseLocal(until);
			var result = parsed.HasValue ? await _sets.DeferUpdateAsync(await ActorAsync(), id, parsed.Value, cancellationToken) : ContentSetResult.Fail(ContentSetErrors.DateOutOfRange);
			if (result.Succeeded) Say("UpdateDeferred"); else Fail(result.Error);
			return RedirectToAction("Update", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Decline(int id, CancellationToken cancellationToken)
		{
			var result = await _sets.DeclineUpdateAsync(await ActorAsync(), id, cancellationToken);
			if (result.Succeeded) Say("UpdateDeclined"); else Fail(result.Error);
			return RedirectToAction("Update", new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Customize(int id)
		{
			var actor = await ActorAsync();
			var view = await _sets.GetCustomizeAsync(actor, id);
			if (view == null || !actor.CanManage)
				return Unauthorized();
			return View(await FillAsync(new ContentSetCustomizeModel { View = view }, actor, ContentSetKinds.Protocols));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Customize(int id, Dictionary<string, string> overrides, List<string> additions, CancellationToken cancellationToken)
		{
			var result = await _sets.SaveCustomizationAsync(await ActorAsync(), id, overrides, additions, cancellationToken);
			if (result.Succeeded) Say("Customized"); else Fail(result.Error, result.Reason);
			return RedirectToAction("Customize", new { id });
		}

		[HttpGet]
		public async Task<IActionResult> Propose(int kind, int localId)
		{
			var actor = await ActorAsync();
			var copy = await _sets.GetCopyInfoAsync(actor, (ContentSetItemKinds)kind, localId);
			if (copy == null || !copy.CanPropose)
				return Unauthorized();
			var title = (await _sets.GetSubscriptionsAsync(actor)).SelectMany(s => s.Copies).FirstOrDefault(c => c.Copy.ContentSetCopyId == copy.Copy.ContentSetCopyId)?.Title;
			return View(await FillAsync(new ContentSetProposeModel { Copy = copy, ItemTitle = title }, actor, (ContentSetKinds)(copy.Set?.Kind ?? 0)));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Propose(int copyId, string anchor, string body, CancellationToken cancellationToken)
		{
			var result = await _sets.ProposeAsync(await ActorAsync(), copyId, anchor, body, cancellationToken);
			if (result.Succeeded) Say("Proposed"); else Fail(result.Error);
			return RedirectToAction("Proposals");
		}

		[HttpGet]
		public async Task<IActionResult> Proposals()
		{
			var actor = await ActorAsync();
			if (!actor.IsCurrentMember)
				return Unauthorized();
			var model = new ContentSetProposalsModel
			{
				Received = await _sets.GetProposalsToPublisherAsync(actor, false),
				Sent = await _sets.GetProposalsFromDepartmentAsync(actor)
			};
			return View(await FillAsync(model, actor, ContentSetKinds.Protocols));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Respond(int id, bool accept, string response, CancellationToken cancellationToken)
		{
			var result = await _sets.RespondToProposalAsync(await ActorAsync(), id, accept, response, cancellationToken);
			if (result.Succeeded) Say("ProposalAnswered"); else Fail(result.Error);
			return RedirectToAction("Proposals");
		}

		#endregion Subscriber

		#region Sponsorships and settings

		[HttpGet]
		public async Task<IActionResult> Sponsorships()
		{
			var actor = await ActorAsync();
			if (!actor.IsCurrentMember)
				return Unauthorized();

			var model = new ContentSetSponsorshipsModel
			{
				DepartmentId = DepartmentId,
				Sponsorships = await _sponsorships.GetForDepartmentAsync(DepartmentId),
				Grant = await _sponsorships.GetGrantAsync(DepartmentId)
			};
			if (actor.CanManage)
			{
				foreach (var link in await _links.GetAllLinksForDepartmentAsync(DepartmentId) ?? new List<DepartmentLink>())
				{
					var other = DepartmentLinkSharing.GetOtherDepartmentId(link, DepartmentId);
					if (other.HasValue && DepartmentLinkSharing.Shares(link, DepartmentId, other.Value, DepartmentLinkShareTypes.Training) && model.Sponsorable.All(p => p.Key != other.Value))
						model.Sponsorable.Add(new KeyValuePair<int, string>(other.Value, link.DepartmentId == other.Value ? link.Department?.Name : link.LinkedDepartment?.Name));
				}
			}

			return View(await FillAsync(model, actor, ContentSetKinds.Training));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Sponsor(int departmentId, int? seatLimit, CancellationToken cancellationToken)
		{
			var error = await _sponsorships.SponsorAsync(await ActorAsync(), departmentId, seatLimit, cancellationToken);
			if (error == LmsSponsorshipErrors.None) Say("SponsorshipStarted"); else Fail(Text("SponsorError" + error));
			return RedirectToAction("Sponsorships");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SeatLimit(int id, int? seatLimit, CancellationToken cancellationToken)
		{
			var error = await _sponsorships.SetSeatLimitAsync(await ActorAsync(), id, seatLimit, cancellationToken);
			if (error == LmsSponsorshipErrors.None) Say("SponsorshipSaved"); else Fail(Text("SponsorError" + error));
			return RedirectToAction("Sponsorships");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EndSponsorship(int id, CancellationToken cancellationToken)
		{
			var error = await _sponsorships.EndAsync(await ActorAsync(), id, cancellationToken);
			if (error == LmsSponsorshipErrors.None) Say("SponsorshipEnded"); else Fail(Text("SponsorError" + error));
			return RedirectToAction("Sponsorships");
		}

		[HttpGet]
		public async Task<IActionResult> Settings()
		{
			var actor = await ActorAsync();
			if (!actor.CanManage)
				return Unauthorized();
			var roles = ((await _roles.GetRolesForDepartmentAsync(DepartmentId)) ?? new List<PersonnelRole>())
				.Where(r => r != null && r.DepartmentId == DepartmentId).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(r => new KeyValuePair<int, string>(r.PersonnelRoleId, r.Name)).ToList();
			return View(await FillAsync(new ContentSetSettingsModel { Settings = await _sets.GetSettingsAsync(DepartmentId), Roles = roles }, actor, ContentSetKinds.Protocols));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings(string updateTime, bool notifyManagers, bool notifyAdmins, int[] notifyRoleIds, CancellationToken cancellationToken)
		{
			var minutes = ContentSetSettings.DefaultUpdateTimeOfDayMinutes;
			if (TimeSpan.TryParseExact(updateTime ?? string.Empty, new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var time))
				minutes = (int)time.TotalMinutes;
			var saved = await _sets.SaveSettingsAsync(await ActorAsync(), new ContentSetSettings
			{
				UpdateTimeOfDayMinutes = minutes,
				NotifyManagers = notifyManagers,
				NotifyAdmins = notifyAdmins,
				NotifyRoleIds = (notifyRoleIds ?? Array.Empty<int>()).ToList()
			}, cancellationToken);
			if (saved != null) Say("SettingsSaved"); else Fail(ContentSetErrors.NotAuthorized);
			return RedirectToAction("Settings");
		}

		#endregion Sponsorships and settings
	}
}

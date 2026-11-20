using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The View Call / Update Call text conversation panel (call-intake-and-ai-destinations-plan.md §8 D17, for departments without the
	/// dispatcher desk; it stays when the desk took the conversation, which follows through the router callbacks): the thread with the
	/// member of the public who texted the department's dedicated number, "Take conversation", Release, Close (optionally telling the
	/// texter) and a send box. Reading needs the right to view the call (the texter's number masked unless the member may edit it);
	/// every action needs the right to edit the call. The same rules as v4 IntakeConversations.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class IntakeConversationsController : SecureBaseController
	{
		private readonly IIntakeConversationService _conversations;
		private readonly ICallsService _callsService;
		private readonly IUserProfileService _profiles;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;

		public IntakeConversationsController(IIntakeConversationService conversations, ICallsService callsService, IUserProfileService profiles,
			Resgrid.Model.Services.IAuthorizationService authorizationService)
		{
			_conversations = conversations;
			_callsService = callsService;
			_profiles = profiles;
			_authorizationService = authorizationService;
		}

		/// <summary>The call's conversation (open, else the newest closed one) and its messages; exists = false when the call has none.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Thread(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId, false);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
				return Json(new { success = false, exists = false });

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			var conversation = await _conversations.GetForCallAsync(DepartmentId, callId);
			if (conversation == null)
				return Json(new { success = true, exists = false });

			var canEdit = await _authorizationService.CanUserEditCallAsync(UserId, callId);
			return Json(await ShapeAsync(conversation.IntakeConversationId, canEdit, null));
		}

		/// <summary>Takes the conversation (TakeOver: from another dispatcher); the texter is told who they are connected with.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Connect([FromForm] string conversationId, [FromForm] bool takeOver)
		{
			var (conversation, denied) = await AuthorizeAsync(conversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Json(new { success = false, exists = false, outcome = IntakeConversationActionResults.NotFound.ToString() });

			var outcome = await _conversations.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, UserId, takeOver, CancellationToken.None);
			return Json(await ShapeAsync(conversation.IntakeConversationId, true, outcome));
		}

		/// <summary>Texts the texter from the master number (the connected dispatcher only; with nobody connected, connects first).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Send([FromForm] string conversationId, [FromForm] string text)
		{
			var (conversation, denied) = await AuthorizeAsync(conversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Json(new { success = false, exists = false, outcome = IntakeConversationActionResults.NotFound.ToString() });

			var outcome = await _conversations.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, UserId, text, CancellationToken.None);
			return Json(await ShapeAsync(conversation.IntakeConversationId, true, outcome));
		}

		/// <summary>Steps away: Enhanced AI resumes when the department has it, else the conversation waits for a dispatcher.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Release([FromForm] string conversationId)
		{
			var (conversation, denied) = await AuthorizeAsync(conversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Json(new { success = false, exists = false, outcome = IntakeConversationActionResults.NotFound.ToString() });

			var outcome = await _conversations.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, UserId, CancellationToken.None);
			return Json(await ShapeAsync(conversation.IntakeConversationId, true, outcome));
		}

		/// <summary>Ends the conversation; notify = text the texter that it ended.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<IActionResult> Close([FromForm] string conversationId, [FromForm] string reason, [FromForm] bool notify)
		{
			var (conversation, denied) = await AuthorizeAsync(conversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Json(new { success = false, exists = false, outcome = IntakeConversationActionResults.NotFound.ToString() });

			var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
			if (why != null && why.Length > 200)
				why = why.Substring(0, 200);
			var outcome = await _conversations.CloseAsync(DepartmentId, conversation.IntakeConversationId, UserId, why, notify, CancellationToken.None);
			return Json(await ShapeAsync(conversation.IntakeConversationId, true, outcome));
		}

		/// <summary>
		/// The department's conversation and, when it exists, whether the member may edit its call (Unauthorized otherwise). A deleted call's
		/// conversation is not found, as Thread treats it: nothing is texted for a call that no longer exists.
		/// </summary>
		private async Task<(IntakeConversation Conversation, IActionResult Denied)> AuthorizeAsync(string conversationId)
		{
			if (string.IsNullOrWhiteSpace(conversationId))
				return (null, null);

			var conversation = await _conversations.GetByIdAsync(DepartmentId, conversationId.Trim());
			if (conversation == null || !conversation.CallId.HasValue)
				return (null, null);

			var call = await _callsService.GetCallByIdAsync(conversation.CallId.Value, false);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
				return (null, null);

			if (!await _authorizationService.CanUserEditCallAsync(UserId, conversation.CallId.Value))
				return (null, Unauthorized());

			return (conversation, null);
		}

		private async Task<object> ShapeAsync(string conversationId, bool canEdit, IntakeConversationActionResults? outcome)
		{
			IntakeConversationView view;
			try
			{
				view = await _conversations.GetViewAsync(DepartmentId, conversationId, UserId);
			}
			catch (Exception ex) when (outcome.HasValue)
			{
				// After an action (a text may be with the carrier) a failed refresh still reports the outcome rather than a 500 that invites a
				// second Send; the panel reads the thread again on the next update.
				Resgrid.Framework.Logging.LogException(ex, $"Intake conversation {conversationId}: the thread could not be read after {outcome}.");
				return new { success = outcome == IntakeConversationActionResults.Done, exists = true, outcome = outcome.Value.ToString() };
			}

			var conversation = view?.Conversation;
			if (conversation == null)
				return new { success = false, exists = false, outcome = (outcome ?? IntakeConversationActionResults.NotFound).ToString() };

			string assignedName = null;
			if (!string.IsNullOrWhiteSpace(conversation.AssignedUserId))
			{
				var profile = await _profiles.GetProfileByUserIdAsync(conversation.AssignedUserId);
				assignedName = profile == null ? null : profile.FullName.AsFirstNameLastName.Trim();
			}

			return new
			{
				success = outcome == null || outcome == IntakeConversationActionResults.Done,
				exists = true,
				outcome = outcome?.ToString(),
				canEdit,
				canConnect = canEdit && conversation.IsOpen && !conversation.OptedOut,
				maxLength = Math.Max(1, IntakeConfig.ConversationMaxOutboundLength),
				conversation = new
				{
					id = conversation.IntakeConversationId,
					callId = conversation.CallId,
					state = conversation.State,
					handler = conversation.Handler,
					assignedUserId = conversation.AssignedUserId,
					assignedName = string.IsNullOrWhiteSpace(assignedName) ? null : assignedName,
					isMine = !string.IsNullOrWhiteSpace(conversation.AssignedUserId) && string.Equals(conversation.AssignedUserId, UserId, StringComparison.OrdinalIgnoreCase),
					participant = canEdit ? conversation.ParticipantAddress : Framework.StringHelpers.MaskPhoneNumber(conversation.ParticipantAddress),
					optedOut = conversation.OptedOut,
					closeReason = conversation.IsOpen ? null : conversation.CloseReason,
					startedOn = Iso(conversation.StartedOn),
					lastActivityOn = Iso(conversation.LastActivityOn)
				},
				messages = view.Messages.Select(m => new
				{
					id = m.MessageId,
					direction = m.Direction,
					author = m.Author,
					authorName = m.AuthorName,
					text = m.Text,
					status = m.Status,
					createdOn = Iso(m.CreatedOn)
				}).ToList()
			};
		}

		private static string Iso(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
	}
}

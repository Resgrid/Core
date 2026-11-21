using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.IntakeConversations;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Intake SMS conversations (call-intake-and-ai-destinations-plan.md §8 D15–D17): a member of the public texted the department's
	/// dedicated number, the text became a pending call, and the conversation continues from the master Resgrid number. Anyone who can
	/// view the call reads the thread (the texter's number masked unless they may edit the call); taking it ("You are now connected with
	/// {FirstName} …"), texting, releasing it back to Enhanced AI / unassisted and closing it need the right to edit the call. Live changes
	/// arrive on the eventing hub as <c>intakeConversationUpdated(callId, payload)</c> (payload: conversation id, state, handler).
	/// The dispatcher desk uses the same service; this is the call screen's (web View Call, Omni call detail) way in.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class IntakeConversationsController : V4AuthenticatedApiControllerbase
	{
		private readonly IIntakeConversationService _conversations;
		private readonly IIntakeConversationOperations _operations;
		private readonly ICallsService _callsService;
		private readonly IUserProfileService _profiles;
		private readonly Model.Services.IAuthorizationService _authorizationService;

		public IntakeConversationsController(IIntakeConversationService conversations, IIntakeConversationOperations operations, ICallsService callsService,
			IUserProfileService profiles, Model.Services.IAuthorizationService authorizationService)
		{
			_conversations = conversations;
			_operations = operations;
			_callsService = callsService;
			_profiles = profiles;
			_authorizationService = authorizationService;
		}

		/// <summary>The call's conversation (open, else the newest closed one) with its messages; Conversation is null when the call has none.</summary>
		/// <param name="callId">The call.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		[HttpGet("GetForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<IntakeConversationResult>> GetForCall(int callId, CancellationToken cancellationToken)
		{
			if (callId <= 0)
				return BadRequest();

			var result = new IntakeConversationResult();
			var call = await _callsService.GetCallByIdAsync(callId, false);
			if (call == null || call.DepartmentId != DepartmentId || call.IsDeleted)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			var canEdit = await _authorizationService.CanUserEditCallAsync(UserId, callId);
			var conversation = await _conversations.GetForCallAsync(DepartmentId, callId);
			await FillAsync(result, conversation, canEdit, cancellationToken);
			return Ok(Done(result));
		}

		/// <summary>Takes the conversation: Enhanced AI goes silent and the texter is told who they are connected with. TakeOver takes it from another dispatcher.</summary>
		[HttpPost("Connect")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<IntakeConversationResult>> Connect([FromBody] ConnectIntakeConversationInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.ConversationId))
				return BadRequest();

			var (conversation, denied) = await AuthorizeAsync(input.ConversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Ok(NotFoundResult());

			var outcome = await _conversations.ConnectDispatcherAsync(DepartmentId, conversation.IntakeConversationId, UserId, input.TakeOver, CancellationToken.None);
			return Ok(await ActedAsync(conversation.IntakeConversationId, outcome, cancellationToken));
		}

		/// <summary>Texts the texter (from the master number). Only the connected dispatcher may; with nobody connected, the caller connects first.</summary>
		[HttpPost("Send")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<IntakeConversationResult>> Send([FromBody] SendIntakeConversationInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.ConversationId))
				return BadRequest();

			var (conversation, denied) = await AuthorizeAsync(input.ConversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Ok(NotFoundResult());

			var outcome = await _conversations.SendDispatcherMessageAsync(DepartmentId, conversation.IntakeConversationId, UserId, input.Text, CancellationToken.None);
			return Ok(await ActedAsync(conversation.IntakeConversationId, outcome, cancellationToken));
		}

		/// <summary>Steps away: Enhanced AI resumes when the department has it, else the conversation waits for a dispatcher. No text is sent.</summary>
		[HttpPost("Release")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<IntakeConversationResult>> Release([FromBody] ReleaseIntakeConversationInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.ConversationId))
				return BadRequest();

			var (conversation, denied) = await AuthorizeAsync(input.ConversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Ok(NotFoundResult());

			var outcome = await _conversations.ReleaseDispatcherAsync(DepartmentId, conversation.IntakeConversationId, UserId, CancellationToken.None);
			return Ok(await ActedAsync(conversation.IntakeConversationId, outcome, cancellationToken));
		}

		/// <summary>Ends the conversation; Notify texts the texter that it ended (from the master number). The pending call stays.</summary>
		[HttpPost("Close")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<IntakeConversationResult>> Close([FromBody] CloseIntakeConversationInput input, CancellationToken cancellationToken)
		{
			if (input == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(input.ConversationId))
				return BadRequest();

			var (conversation, denied) = await AuthorizeAsync(input.ConversationId);
			if (denied != null)
				return denied;
			if (conversation == null)
				return Ok(NotFoundResult());

			var outcome = await _conversations.CloseAsync(DepartmentId, conversation.IntakeConversationId, UserId, input.Reason, input.Notify, CancellationToken.None);
			return Ok(await ActedAsync(conversation.IntakeConversationId, outcome, cancellationToken));
		}

		/// <summary>
		/// The department's conversation and, when it exists, whether the caller may edit its call (Unauthorized otherwise). A deleted call's
		/// conversation is not found, as GetForCall treats it: nothing is texted for a call that no longer exists.
		/// </summary>
		private async Task<(IntakeConversation Conversation, ActionResult Denied)> AuthorizeAsync(string conversationId)
		{
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

		private async Task<IntakeConversationResult> ActedAsync(string conversationId, IntakeConversationActionResults outcome, CancellationToken cancellationToken)
		{
			var result = new IntakeConversationResult();
			result.Data.Outcome = outcome.ToString();

			// The action already happened (a text may be with the carrier): a refresh that fails must not turn its outcome into a 500, which
			// a client would retry and so text the caller twice. The outcome comes back without the thread; the client reads it again.
			try
			{
				await FillAsync(result, await _conversations.GetByIdAsync(DepartmentId, conversationId), true, cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversationId}: the thread could not be read after {outcome}.");
				result.Data.Conversation = null;
				result.Data.Messages = new System.Collections.Generic.List<IntakeConversationMessageData>();
				result.Data.CanConnect = false;
			}

			if (outcome == IntakeConversationActionResults.NotFound)
				result.Status = ResponseHelper.NotFound;
			return Done(result);
		}

		private IntakeConversationResult NotFoundResult()
		{
			var result = new IntakeConversationResult();
			result.Data.Outcome = IntakeConversationActionResults.NotFound.ToString();
			ResponseHelper.PopulateV4ResponseNotFound(result);
			return result;
		}

		private async Task FillAsync(IntakeConversationResult result, IntakeConversation conversation, bool canEdit, CancellationToken cancellationToken)
		{
			if (conversation == null)
				return;

			var view = await _operations.GetViewAsync(DepartmentId, conversation.IntakeConversationId, UserId,
				Request?.Headers[DataProtectionController.GrantHeader].ToString(), cancellationToken);
			if (view?.Conversation == null)
				return;

			result.Data.CanConnect = canEdit && view.Conversation.IsOpen && !view.Conversation.OptedOut;
			result.Data.Conversation = await ConvertAsync(view.Conversation, canEdit);
			result.Data.Messages = view.Messages.Select(ConvertMessage).ToList();
		}

		private async Task<IntakeConversationData> ConvertAsync(IntakeConversation conversation, bool canEdit)
		{
			string assignedName = null;
			if (!string.IsNullOrWhiteSpace(conversation.AssignedUserId))
			{
				var profile = await _profiles.GetProfileByUserIdAsync(conversation.AssignedUserId);
				assignedName = profile == null ? null : profile.FullName.AsFirstNameLastName.Trim();
			}

			return ConvertConversation(conversation, canEdit, assignedName);
		}

		/// <summary>The v4 shape of a conversation; the texter's number is masked unless the caller may edit the call.</summary>
		public static IntakeConversationData ConvertConversation(IntakeConversation conversation, bool canEdit, string assignedName) => new IntakeConversationData
		{
			Id = conversation.IntakeConversationId,
			CallId = conversation.CallId,
			State = conversation.State,
			Handler = conversation.Handler,
			AssignedUserId = conversation.AssignedUserId,
			AssignedName = string.IsNullOrWhiteSpace(assignedName) ? null : assignedName,
			ParticipantAddress = canEdit ? conversation.ParticipantAddress : Framework.StringHelpers.MaskPhoneNumber(conversation.ParticipantAddress),
			StartedOn = DateTime.SpecifyKind(conversation.StartedOn, DateTimeKind.Utc),
			LastActivityOn = DateTime.SpecifyKind(conversation.LastActivityOn, DateTimeKind.Utc),
			OptedOut = conversation.OptedOut,
			CloseReason = conversation.IsOpen ? null : conversation.CloseReason,
			DeskItemId = conversation.DeskItemId
		};

		public static IntakeConversationMessageData ConvertMessage(IntakeConversationEntry entry) => new IntakeConversationMessageData
		{
			Id = entry.MessageId,
			Direction = entry.Direction,
			Author = entry.Author,
			AuthorName = entry.AuthorName,
			Text = entry.Text,
			Status = entry.Status,
			CreatedOn = DateTime.SpecifyKind(entry.CreatedOn, DateTimeKind.Utc)
		};

		private static IntakeConversationResult Done(IntakeConversationResult result)
		{
			result.PageSize = result.Data.Conversation == null ? 0 : result.Data.Messages.Count;
			result.Status ??= ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}

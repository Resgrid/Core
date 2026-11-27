using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.ContentSets;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Protocol and course sets over Department Links (round 10 A3, Protocols P5 / Training T5): the subscriber's update inbox and its
	/// accept / defer / decline decisions, the department's subscriptions, the sets linked departments offer it, the publisher's own sets and
	/// subscribers, and proposals upstream. Every call runs as the caller in the caller's department; the set service re-checks Manage Content
	/// Sets (permission 136), the link and the department on every call, so a refusal here is the service's.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class ContentSetsController : V4AuthenticatedApiControllerbase
	{
		private readonly IContentSetService _sets;

		public ContentSetsController(IContentSetService sets)
		{
			_sets = sets;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private Task<ContentSetActor> ActorAsync() => _sets.GetActorAsync(DepartmentId, UserId);

		/// <summary>A decision's answer: 404 / 403 for a missing or forbidden subject, otherwise 200 with Status success or failure and the error code.</summary>
		private ActionResult<ContentSetActionResult> Answer(ContentSetResult result, int id)
		{
			if (result == null || result.Error == ContentSetErrors.NotFound)
				return NotFound();
			if (result.Error == ContentSetErrors.NotAuthorized)
				return Forbid();
			var answer = new ContentSetActionResult { Id = id, Error = result.Error == ContentSetErrors.None ? null : result.Error.ToString(), Reason = result.Reason };
			return Ok(Populate(answer, 1, result.Error == ContentSetErrors.None ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		/// <summary>The department's set update inbox (open updates only unless openOnly is false).</summary>
		[HttpGet("GetInbox")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<ContentSetInboxResult>> GetInbox(bool openOnly = true)
		{
			var actor = await ActorAsync();
			if (!actor.IsCurrentMember)
				return Forbid();
			var result = new ContentSetInboxResult { Data = (await _sets.GetInboxAsync(actor, openOnly) ?? new List<ContentSetInboxRow>()).Select(ContentSetUpdateData.From).Where(r => r != null).ToList() };
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>One update with its redline (the department's current text and the publisher's candidate) and when accepting would apply it.</summary>
		[HttpGet("GetUpdate")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetUpdateResult>> GetUpdate(int contentSetUpdateId)
		{
			var view = await _sets.GetUpdateAsync(await ActorAsync(), contentSetUpdateId);
			if (view?.Row?.Update == null)
				return NotFound();
			return Ok(Populate(new ContentSetUpdateResult
			{
				Data = new ContentSetUpdateDetailData
				{
					Update = ContentSetUpdateData.From(view.Row),
					CurrentHtml = view.CurrentHtml,
					CandidateHtml = view.CandidateHtml,
					DroppedChanges = view.DroppedChanges ?? new List<string>(),
					SuggestedApplyOn = view.SuggestedApplyOnUtc
				}
			}, 1));
		}

		/// <summary>Accepts an update: it goes into force at the suggested time (or the later one given), never before the publisher's effective date.</summary>
		[HttpPost("AcceptUpdate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetActionResult>> AcceptUpdate([FromBody] ContentSetAcceptInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ContentSetUpdateId <= 0)
				return BadRequest();
			var applyOn = input.ApplyOn.HasValue ? DateTime.SpecifyKind(input.ApplyOn.Value.ToUniversalTime(), DateTimeKind.Utc) : (DateTime?)null;
			return Answer(await _sets.AcceptUpdateAsync(await ActorAsync(), input.ContentSetUpdateId, applyOn, cancellationToken), input.ContentSetUpdateId);
		}

		/// <summary>Defers an update to a later time (a locked update no later than its deadline).</summary>
		[HttpPost("DeferUpdate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetActionResult>> DeferUpdate([FromBody] ContentSetDeferInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ContentSetUpdateId <= 0 || input.Until == default)
				return BadRequest();
			return Answer(await _sets.DeferUpdateAsync(await ActorAsync(), input.ContentSetUpdateId, DateTime.SpecifyKind(input.Until.ToUniversalTime(), DateTimeKind.Utc), cancellationToken),
				input.ContentSetUpdateId);
		}

		/// <summary>Declines an update (open items only; locked content can only be deferred).</summary>
		[HttpPost("DeclineUpdate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetActionResult>> DeclineUpdate([FromBody] ContentSetUpdateIdInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ContentSetUpdateId <= 0)
				return BadRequest();
			return Answer(await _sets.DeclineUpdateAsync(await ActorAsync(), input.ContentSetUpdateId, cancellationToken), input.ContentSetUpdateId);
		}

		/// <summary>The department's set subscriptions with their managed copies.</summary>
		[HttpGet("GetSubscriptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<ContentSetSubscriptionsResult>> GetSubscriptions()
		{
			var actor = await ActorAsync();
			if (!actor.CanManage)
				return Forbid();
			var result = new ContentSetSubscriptionsResult
			{
				Data = (await _sets.GetSubscriptionsAsync(actor) ?? new List<ContentSetSubscriptionView>()).Where(v => v?.Subscription != null).Select(v => new ContentSetSubscriptionData
				{
					ContentSetSubscriptionId = v.Subscription.ContentSetSubscriptionId,
					ContentSetId = v.Subscription.ContentSetId,
					SetName = v.Set?.Name,
					PublisherName = v.PublisherName,
					Status = ((ContentSetSubscriptionStatuses)v.Subscription.Status).ToString(),
					AutoAccept = v.Subscription.AutoAccept,
					OpenUpdates = v.OpenUpdates,
					LinkOk = v.LinkOk,
					SponsoredByPublisher = v.SponsoredByPublisher,
					Copies = (v.Copies ?? new List<ContentSetCopyRow>()).Where(c => c?.Copy != null).Select(c => new ContentSetCopyData
					{
						ContentSetCopyId = c.Copy.ContentSetCopyId,
						ItemKind = ((ContentSetItemKinds)c.Copy.ItemKind).ToString(),
						LocalItemId = c.Copy.LocalItemId,
						Title = c.Title,
						VersionLabel = c.Copy.SourceVersionLabel,
						HasLocalChanges = c.HasLocalChanges
					}).ToList()
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>The sets of one kind (0 protocols, 1 training) linked departments offer this department.</summary>
		[HttpGet("GetAvailableSets")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<ContentSetsResult>> GetAvailableSets(int kind = 0)
		{
			var actor = await ActorAsync();
			if (!actor.CanManage)
				return Forbid();
			var result = new ContentSetsResult
			{
				Data = (await _sets.GetAvailableSetsAsync(actor, kind == (int)ContentSetKinds.Training ? ContentSetKinds.Training : ContentSetKinds.Protocols) ?? new List<AvailableContentSet>())
					.Select(ContentSetData.From).Where(s => s != null).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>Publisher view: the department's own sets of one kind (0 protocols, 1 training) with their items and subscribers.</summary>
		[HttpGet("GetPublishedSets")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<ContentSetsResult>> GetPublishedSets(int kind = 0)
		{
			var actor = await ActorAsync();
			if (!actor.CanManage)
				return Forbid();
			var result = new ContentSetsResult
			{
				Data = (await _sets.GetPublishedSetsAsync(actor, kind == (int)ContentSetKinds.Training ? ContentSetKinds.Training : ContentSetKinds.Protocols) ?? new List<ContentSetDetail>())
					.Select(ContentSetData.From).Where(s => s != null).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>Publisher view: one of the department's sets with its items and subscribers.</summary>
		[HttpGet("GetSet")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetResultData>> GetSet(int contentSetId)
		{
			var detail = await _sets.GetSetAsync(await ActorAsync(), contentSetId);
			if (detail?.Set == null)
				return NotFound();
			return Ok(Populate(new ContentSetResultData { Data = ContentSetData.From(detail) }, 1));
		}

		/// <summary>Proposals: those other departments sent to this department's sets (managers) and those this department sent upstream.</summary>
		[HttpGet("GetProposals")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		public async Task<ActionResult<ContentSetProposalsResult>> GetProposals(bool openOnly = false)
		{
			var actor = await ActorAsync();
			if (!actor.IsCurrentMember)
				return Forbid();
			var result = new ContentSetProposalsResult();
			if (actor.CanManage)
				result.Data.Received = (await _sets.GetProposalsToPublisherAsync(actor, openOnly) ?? new List<ContentSetProposalView>()).Select(ContentSetProposalData.From).Where(p => p != null).ToList();
			result.Data.Sent = (await _sets.GetProposalsFromDepartmentAsync(actor) ?? new List<ContentSetProposalView>()).Select(ContentSetProposalData.From).Where(p => p != null).ToList();
			return Ok(Populate(result, result.Data.Received.Count + result.Data.Sent.Count));
		}

		/// <summary>Proposes a change to the publisher of one of the department's managed copies (it reaches the publisher's next draft as a suggestion).</summary>
		[HttpPost("Propose")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetActionResult>> Propose([FromBody] ContentSetProposeInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ContentSetCopyId <= 0 || string.IsNullOrWhiteSpace(input.Body))
				return BadRequest();
			var result = await _sets.ProposeAsync(await ActorAsync(), input.ContentSetCopyId, input.Anchor, input.Body, cancellationToken);
			return Answer(result, result?.Proposal?.ContentSetProposalId ?? 0);
		}

		/// <summary>Publisher: accepts or declines a proposal with an optional response the proposing department sees.</summary>
		[HttpPost("RespondToProposal")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ContentSetActionResult>> RespondToProposal([FromBody] ContentSetRespondInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ContentSetProposalId <= 0)
				return BadRequest();
			return Answer(await _sets.RespondToProposalAsync(await ActorAsync(), input.ContentSetProposalId, input.Accept, input.Response, cancellationToken), input.ContentSetProposalId);
		}
	}
}

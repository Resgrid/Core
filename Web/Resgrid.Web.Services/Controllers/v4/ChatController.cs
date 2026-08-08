using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Chat;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Realtime chat system interaction (channels, messages, reactions, attachments and presence)
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ChatController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors

		private const long MaxAttachmentRequestBytes = 26_214_400;

		private static readonly string[] AllowedAttachmentContentTypes = new[]
		{
			"image/png", "image/jpeg", "image/gif", "image/webp", "application/pdf"
		};

		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IChatMessageService _chatMessageService;
		private readonly IChatModerationService _chatModerationService;
		private readonly IModerationService _moderationService;
		private readonly IChatPresenceService _chatPresenceService;
		private readonly IChatAttachmentRepository _chatAttachmentRepository;
		private readonly IGifProvider _gifProvider;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICacheProvider _cacheProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly IQueueService _queueService;

		public ChatController(
			IChatChannelService chatChannelService,
			IChatPermissionService chatPermissionService,
			IChatMessageService chatMessageService,
			IChatModerationService chatModerationService,
			IModerationService moderationService,
			IChatPresenceService chatPresenceService,
			IChatAttachmentRepository chatAttachmentRepository,
			IGifProvider gifProvider,
			IFeatureToggleService featureToggleService,
			IAuthorizationService authorizationService,
			ICacheProvider cacheProvider,
			IEventAggregator eventAggregator,
			IQueueService queueService)
		{
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_chatMessageService = chatMessageService;
			_chatModerationService = chatModerationService;
			_moderationService = moderationService;
			_chatPresenceService = chatPresenceService;
			_chatAttachmentRepository = chatAttachmentRepository;
			_gifProvider = gifProvider;
			_featureToggleService = featureToggleService;
			_authorizationService = authorizationService;
			_cacheProvider = cacheProvider;
			_eventAggregator = eventAggregator;
			_queueService = queueService;
		}

		#endregion Members and Constructors

		#region Channels

		/// <summary>
		/// Returns all the chat channels the current user can access, with per-channel unread counts.
		/// </summary>
		/// <param name="activeUnitId">Optional unit the user is actively operating as</param>
		/// <returns>Array of ChatChannelResultData objects for the channels the user can access</returns>
		[HttpGet("GetChannels")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatChannelsResult>> GetChannels(int? activeUnitId = null)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var result = new GetChatChannelsResult();
			var channels = await _chatChannelService.GetChannelsForUserAsync(DepartmentId, UserId, activeUnitId);
			var memberRows = await _chatChannelService.GetActiveMembershipsForUserAsync(DepartmentId, UserId);

			var membersByChannel = new Dictionary<string, ChatChannelMember>();
			if (memberRows != null)
			{
				foreach (var member in memberRows)
				{
					if (!membersByChannel.ContainsKey(member.ChatChannelId))
						membersByChannel.Add(member.ChatChannelId, member);
				}
			}

			if (channels != null && channels.Any())
			{
				foreach (var channel in channels)
				{
					membersByChannel.TryGetValue(channel.ChatChannelId, out var member);
					result.Data.Add(ConvertChannelResultData(channel, member));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Gets a single chat channel by its Id.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <returns>ChatChannelResultData object for the requested channel</returns>
		[HttpGet("GetChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatChannelResult>> GetChannel(string channelId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var result = new GetChatChannelResult();
			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);

			if (channel != null && channel.DepartmentId == DepartmentId)
			{
				if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
					return Unauthorized();

				var member = await _chatChannelService.GetUserMembershipAsync(channel.ChatChannelId, UserId);

				result.Data = ConvertChannelResultData(channel, member);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Finds or creates the 1:1 direct message channel between the current user and a user or unit.
		/// </summary>
		/// <param name="input">Target user or unit for the direct message</param>
		/// <returns>ChatChannelCreatedResult with the existing or newly created channel</returns>
		[HttpPost("CreateDirectMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatChannelCreatedResult>> CreateDirectMessage([FromBody] CreateDirectMessageInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || (String.IsNullOrWhiteSpace(input.TargetUserId) && !input.TargetUnitId.HasValue))
				return BadRequest();

			var result = new ChatChannelCreatedResult();
			var channel = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(DepartmentId, UserId, input.TargetUserId, input.TargetUnitId, cancellationToken);

			if (channel != null)
			{
				var member = await _chatChannelService.GetUserMembershipAsync(channel.ChatChannelId, UserId);

				result.Data = ConvertChannelResultData(channel, member);
				result.PageSize = 1;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Creates an ad-hoc group channel with an explicit member list.
		/// </summary>
		/// <param name="input">Name and initial members of the channel</param>
		/// <returns>ChatChannelCreatedResult with the newly created channel</returns>
		[HttpPost("CreateAdHocChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatChannelCreatedResult>> CreateAdHocChannel([FromBody] CreateAdHocChannelInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(input.Name) || input.MemberUserIds == null || input.MemberUserIds.Count <= 0)
				return BadRequest();

			var result = new ChatChannelCreatedResult();
			var channel = await _chatChannelService.CreateAdHocGroupChannelAsync(DepartmentId, UserId, input.Name, input.MemberUserIds, cancellationToken);

			if (channel != null)
			{
				result.Data = ConvertChannelResultData(channel, null);
				result.PageSize = 1;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Creates a permission-locked custom channel. Only department admins can create custom channels.
		/// </summary>
		/// <param name="input">Name, topic and OR-evaluated access rules for the channel</param>
		/// <returns>ChatChannelCreatedResult with the newly created channel</returns>
		[HttpPost("CreateCustomChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatChannelCreatedResult>> CreateCustomChannel([FromBody] CreateCustomChannelInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(input.Name))
				return BadRequest();

			if (!await _authorizationService.CanUserModifyDepartmentAsync(UserId, DepartmentId))
				return Unauthorized();

			var rules = new List<ChatChannelAccessRule>();
			if (input.Rules != null)
			{
				foreach (var rule in input.Rules)
				{
					rules.Add(new ChatChannelAccessRule
					{
						RuleType = rule.RuleType,
						GroupId = rule.GroupId,
						PersonnelRoleId = rule.PersonnelRoleId,
						UserId = rule.UserId
					});
				}
			}

			var result = new ChatChannelCreatedResult();
			var channel = await _chatChannelService.CreateCustomChannelAsync(DepartmentId, UserId, input.Name, input.Topic, rules, cancellationToken);

			if (channel != null)
			{
				result.Data = ConvertChannelResultData(channel, null);
				result.PageSize = 1;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Updates a channel's name and topic. Requires channel moderator rights.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">New name and topic</param>
		/// <returns>GetChatChannelResult with the updated channel</returns>
		[HttpPut("UpdateChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatChannelResult>> UpdateChannel(string channelId, [FromBody] UpdateChannelInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new GetChatChannelResult();
			var updated = await _chatChannelService.UpdateChannelAsync(channelId, input?.Name, input?.Topic, UserId, cancellationToken);

			if (updated != null)
			{
				result.Data = ConvertChannelResultData(updated, null);
				result.PageSize = 1;
				result.Status = ResponseHelper.Updated;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Archives a channel. Requires channel moderator rights.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <returns>ChatActionResult indicating whether the channel was archived</returns>
		[HttpDelete("ArchiveChannel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> ArchiveChannel(string channelId, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();
			result.Success = await _chatChannelService.SetChannelArchivedAsync(channelId, true, UserId, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Success : ResponseHelper.Failure;

			if (result.Success)
			{
				_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = DepartmentId,
					UserId = UserId,
					Type = AuditLogTypes.ChatChannelArchived,
					After = channel.CloneJsonToString(),
					Successful = true,
					IpAddress = IpAddressHelper.GetRequestIP(Request, true),
					ServerName = Environment.MachineName,
					UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
				});
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Channels

		#region Members

		/// <summary>
		/// Returns the members of a chat channel.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <returns>Array of ChatMemberResultData objects for the channel</returns>
		[HttpGet("GetMembers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMembersResult>> GetMembers(string channelId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			var result = new GetChatMembersResult();
			var members = await _chatChannelService.GetMembersAsync(channelId);
			var canModerate = await _chatPermissionService.CanModerateChannelAsync(channel, UserId);

			if (members != null && members.Any())
			{
				foreach (var member in members)
				{
					result.Data.Add(ConvertMemberResultData(member, canModerate));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Adds members to a DM, ad-hoc or custom locked channel. The requester must be an active member
		/// of the channel or a channel moderator.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">UserIds to add</param>
		/// <returns>Array of ChatMemberResultData objects for the added members</returns>
		[HttpPost("AddMembers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMembersResult>> AddMembers(string channelId, [FromBody] AddMembersInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || input.UserIds == null || input.UserIds.Count <= 0)
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (channel.ChannelType == (int)ChatChannelType.DirectMessage)
				return BadRequest("Members cannot be added to a direct message channel.");

			if (channel.ChannelType != (int)ChatChannelType.AdHocGroup &&
				channel.ChannelType != (int)ChatChannelType.CustomLocked)
				return BadRequest();

			var requesterMember = await _chatChannelService.GetUserMembershipAsync(channelId, UserId);
			var isActiveMember = requesterMember != null && !requesterMember.RemovedOn.HasValue;
			var canModerate = await _chatPermissionService.CanModerateChannelAsync(channel, UserId);

			if (channel.ChannelType == (int)ChatChannelType.CustomLocked && !canModerate)
				return StatusCode(StatusCodes.Status403Forbidden);

			if (!isActiveMember && !canModerate)
				return Unauthorized();

			var result = new GetChatMembersResult();
			List<ChatChannelMember> added;

			try
			{
				added = await _chatChannelService.AddMembersAsync(channelId, input.UserIds, UserId, cancellationToken);
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			if (added != null && added.Any())
			{
				foreach (var member in added)
				{
					result.Data.Add(ConvertMemberResultData(member, canModerate));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Removes a member from a channel. Users can always remove themselves (leave); removing another
		/// member requires channel moderator rights.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="userId">UserId of the member to remove</param>
		/// <returns>ChatActionResult indicating whether the member was removed</returns>
		[HttpDelete("RemoveMember")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> RemoveMember(string channelId, string userId, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (String.IsNullOrWhiteSpace(userId))
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!String.Equals(userId, UserId, StringComparison.OrdinalIgnoreCase) &&
				!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();
			result.Success = await _chatChannelService.RemoveMemberAsync(channelId, userId, UserId, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Deleted : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Sets the current user's notification preference for a channel.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">Notification preference to apply</param>
		/// <returns>ChatActionResult indicating whether the preference was saved</returns>
		[HttpPut("SetNotificationPreference")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> SetNotificationPreference(string channelId, [FromBody] SetNotificationPreferenceInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			var result = new ChatActionResult();
			result.Success = await _chatChannelService.SetNotificationPreferenceAsync(channelId, DepartmentId, UserId, (ChatNotificationPreference)(input?.Preference ?? 0), cancellationToken);
			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Members

		#region Messages

		/// <summary>
		/// Returns a keyset page of messages for a channel, newest first, enriched with reactions and
		/// attachment metadata.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="beforeSeq">Return messages with a sequence lower than this (null = latest page)</param>
		/// <param name="limit">Maximum number of messages to return</param>
		/// <returns>Array of ChatMessageResultData objects for the page</returns>
		[HttpGet("GetMessages")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessagesResult>> GetMessages(string channelId, long? beforeSeq = null, int limit = 50, CancellationToken cancellationToken = default)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			await _chatChannelService.EnsureMemberStateAsync(channelId, DepartmentId, UserId, null, cancellationToken);

			var result = new GetChatMessagesResult();
			var messages = await _chatMessageService.GetMessagesPageAsync(channelId, beforeSeq, limit);

			await PopulateMessagesResultAsync(result, messages);

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Delta sync for reconnect: returns every message after the supplied sequence, ascending,
		/// enriched with reactions and attachment metadata.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="afterSeq">Return messages with a sequence higher than this</param>
		/// <param name="limit">Maximum number of messages to return</param>
		/// <returns>Array of ChatMessageResultData objects sent after the sequence</returns>
		[HttpGet("GetMessagesAfter")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessagesResult>> GetMessagesAfter(string channelId, long afterSeq, int limit = 50, CancellationToken cancellationToken = default)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			await _chatChannelService.EnsureMemberStateAsync(channelId, DepartmentId, UserId, null, cancellationToken);

			var result = new GetChatMessagesResult();
			var messages = await _chatMessageService.GetMessagesAfterAsync(channelId, afterSeq, limit);

			await PopulateMessagesResultAsync(result, messages);

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns a keyset page of replies for a message thread, newest first.
		/// </summary>
		/// <param name="messageId">Thread root chat message identifier</param>
		/// <param name="beforeSeq">Return replies with a sequence lower than this (null = latest page)</param>
		/// <param name="limit">Maximum number of replies to return</param>
		/// <returns>Array of ChatMessageResultData objects for the thread page</returns>
		[HttpGet("GetThread")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessagesResult>> GetThread(string messageId, long? beforeSeq = null, int limit = 50)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var rootMessage = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (rootMessage == null || rootMessage.DepartmentId != DepartmentId)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(rootMessage.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			var result = new GetChatMessagesResult();
			var messages = await _chatMessageService.GetThreadPageAsync(messageId, beforeSeq, limit);

			await PopulateMessagesResultAsync(result, messages);

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns a single message by id (with reactions and attachment metadata), access-checked via its
		/// channel. Used for resolving flag/report context.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <returns>GetChatMessageResult with the message, or NotFound</returns>
		[HttpGet("GetMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessageResult>> GetMessage(string messageId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var message = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (message == null || message.DepartmentId != DepartmentId)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(message.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			var converted = await ConvertMessagesAsync(new List<ChatMessage> { message });

			var result = new GetChatMessageResult
			{
				Data = converted.FirstOrDefault(),
				Status = converted.Any() ? ResponseHelper.Success : ResponseHelper.NotFound
			};

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Sends a message to a channel. Supports unit and incident-commander identities, threads,
		/// urgent priority and mentions. Resending with the same ClientMessageId returns the original.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">Message content and options</param>
		/// <returns>ChatMessageSentResult with the persisted message</returns>
		[HttpPost("SendMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatMessageSentResult>> SendMessage(string channelId, [FromBody] SendChatMessageInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(channelId))
				return BadRequest();

			if (await IsRateLimitedAsync("send", ChatConfig.SendRateLimitPerWindow))
				return RateLimitedResult<ChatMessageSentResult>();

			var request = new ChatMessageSendRequest
			{
				ChatChannelId = channelId,
				DepartmentId = DepartmentId,
				AsUnitId = input.AsUnitId,
				AsIncidentCommander = input.AsIncidentCommander,
				Body = input.Body,
				MessageType = (ChatMessageType)input.MessageType,
				Priority = (ChatMessagePriority)input.Priority,
				ThreadRootMessageId = input.ThreadRootMessageId,
				AlsoSendToChannel = input.AlsoSendToChannel,
				ClientMessageId = input.ClientMessageId,
				MetadataJson = input.MetadataJson
			};

			if (input.Mentions != null && input.Mentions.Any())
			{
				request.Mentions = new List<ChatMessageMention>();

				foreach (var mention in input.Mentions)
				{
					request.Mentions.Add(new ChatMessageMention
					{
						MentionType = mention.MentionType,
						TargetUserId = mention.TargetUserId,
						TargetUnitId = mention.TargetUnitId,
						TargetRoleId = mention.TargetRoleId,
						TargetGroupId = mention.TargetGroupId
					});
				}
			}

			ChatMessage message;

			try
			{
				message = await _chatMessageService.SendMessageAsync(UserId, request, cancellationToken);
			}
			catch (UnauthorizedAccessException)
			{
				return StatusCode(StatusCodes.Status403Forbidden);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			if (message == null)
				return BadRequest();

			// The assistant channel is also reachable from the regular chat page, so sends that arrive
			// through this generic endpoint must still feed the chatbot pipeline (the dedicated
			// ChatbotController.SendChatMessage does the same for the assistant panel).
			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel != null && channel.ChannelType == (int)ChatChannelType.Chatbot && !String.IsNullOrWhiteSpace(message.Body))
			{
				var queued = await _queueService.EnqueueChatbotMessageAsync(new Resgrid.Model.Queue.ChatbotMessageQueueItem
				{
					DepartmentId = DepartmentId,
					From = UserId,
					Body = message.Body,
					MessageId = message.ChatMessageId,
					Platform = (int)Resgrid.Chatbot.Models.ChatbotPlatform.WebChat
				});

				// The message persisted but the assistant will never see it; surface the failure the
				// same way ChatbotController.SendChatMessage does instead of pretending it was sent.
				if (!queued)
				{
					var failedResult = new ChatMessageSentResult();
					failedResult.Data = (await ConvertMessagesAsync(new List<ChatMessage> { message })).FirstOrDefault();
					failedResult.PageSize = 1;
					failedResult.Status = ResponseHelper.Failure;

					ResponseHelper.PopulateV4ResponseData(failedResult);
					return StatusCode(StatusCodes.Status500InternalServerError, failedResult);
				}

				// Typing indicator to the user's devices while the worker runs the pipeline.
				_eventAggregator.SendMessage<ChatEventRaised>(new ChatEventRaised
				{
					DepartmentId = DepartmentId,
					ChatChannelId = channel.ChatChannelId,
					Kind = ChatEventKinds.ChatbotTyping,
					TargetUserId = UserId,
					PayloadJson = Newtonsoft.Json.JsonConvert.SerializeObject(new { channel.ChatChannelId, IsTyping = true })
				});
			}

			var result = new ChatMessageSentResult();
			result.Data = (await ConvertMessagesAsync(new List<ChatMessage> { message })).FirstOrDefault();
			result.PageSize = 1;
			result.Status = ResponseHelper.Created;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Edits a message's body. Only the original sender can edit; the prior body is preserved for audit.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <param name="input">New message body</param>
		/// <returns>GetChatMessageResult with the updated message</returns>
		[HttpPut("EditMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessageResult>> EditMessage(string messageId, [FromBody] EditMessageInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(input.Body))
				return BadRequest();

			var message = await _chatMessageService.EditMessageAsync(messageId, UserId, input.Body, cancellationToken);

			if (message == null)
				return BadRequest();

			var result = new GetChatMessageResult();
			result.Data = (await ConvertMessagesAsync(new List<ChatMessage> { message })).FirstOrDefault();
			result.PageSize = 1;
			result.Status = ResponseHelper.Updated;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Deletes a message the current user sent (tombstone delete).
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <returns>ChatActionResult indicating whether the message was deleted</returns>
		[HttpDelete("DeleteMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> DeleteMessage(string messageId, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var result = new ChatActionResult();
			result.Success = await _chatMessageService.DeleteMessageAsync(messageId, UserId, false, null, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Deleted : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Messages

		#region Reactions, Acks, Read Pointers and Pins

		/// <summary>
		/// Adds an emoji reaction to a message.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <param name="input">Emoji to react with</param>
		/// <returns>ChatActionResult indicating whether the reaction was added</returns>
		[HttpPost("AddReaction")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> AddReaction(string messageId, [FromBody] AddReactionInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null || String.IsNullOrWhiteSpace(input.Emoji))
				return BadRequest();

			if (await IsRateLimitedAsync("reaction", ChatConfig.ReactionRateLimitPerWindow))
				return RateLimitedResult<ChatActionResult>();

			var accessCheck = await CheckMessageChannelAccessAsync(messageId);
			if (accessCheck != null)
				return accessCheck;

			var result = new ChatActionResult();
			result.Success = await _chatMessageService.AddReactionAsync(messageId, UserId, null, input.Emoji, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Created : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Removes the current user's emoji reaction from a message.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <param name="emoji">Emoji to remove</param>
		/// <returns>ChatActionResult indicating whether the reaction was removed</returns>
		[HttpDelete("RemoveReaction")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> RemoveReaction(string messageId, string emoji, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (String.IsNullOrWhiteSpace(emoji))
				return BadRequest();

			var accessCheck = await CheckMessageChannelAccessAsync(messageId);
			if (accessCheck != null)
				return accessCheck;

			var result = new ChatActionResult();
			result.Success = await _chatMessageService.RemoveReactionAsync(messageId, UserId, null, emoji, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Deleted : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Acknowledges an urgent message for the current user.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <returns>ChatActionResult; Success is true when a pending acknowledgment was stamped</returns>
		[HttpPost("Ack")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> Ack(string messageId, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var accessCheck = await CheckMessageChannelAccessAsync(messageId);
			if (accessCheck != null)
				return accessCheck;

			var result = new ChatActionResult();
			var acknowledged = await _chatMessageService.AcknowledgeMessageAsync(messageId, UserId, cancellationToken);

			result.Success = acknowledged > 0;
			result.Status = result.Success ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns the acknowledgment status rows for an urgent message. Only the message sender or a
		/// channel moderator can view acks.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <returns>Array of ChatAckResultData objects for the message</returns>
		[HttpGet("GetAcks")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatAcksResult>> GetAcks(string messageId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var message = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (message == null || message.DepartmentId != DepartmentId)
				return NotFound();

			if (!String.Equals(message.SenderUserId, UserId, StringComparison.OrdinalIgnoreCase))
			{
				var channel = await _chatChannelService.GetChannelByIdAsync(message.ChatChannelId);
				if (channel == null || !await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
					return Unauthorized();
			}

			var result = new GetChatAcksResult();
			var acks = await _chatMessageService.GetAcksForMessageAsync(messageId);

			if (acks != null && acks.Any())
			{
				foreach (var ack in acks)
				{
					result.Data.Add(ConvertAckResultData(ack));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns the current user's pending urgent-message acknowledgments across the department.
		/// </summary>
		/// <returns>Array of ChatAckResultData objects still awaiting acknowledgment</returns>
		[HttpGet("GetMyPendingAcks")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatAcksResult>> GetMyPendingAcks()
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var result = new GetChatAcksResult();
			var acks = await _chatMessageService.GetPendingAcksForUserAsync(DepartmentId, UserId);

			if (acks != null && acks.Any())
			{
				foreach (var ack in acks)
				{
					result.Data.Add(ConvertAckResultData(ack));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Advances the current user's read pointer for a channel (monotonic).
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="input">Sequence read and optional unit identity</param>
		/// <returns>ChatActionResult indicating whether the pointer advanced</returns>
		[HttpPut("MarkRead")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> MarkRead(string channelId, [FromBody] MarkReadInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null)
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, input.AsUnitId))
				return Unauthorized();

			var result = new ChatActionResult();
			result.Success = await _chatMessageService.MarkReadAsync(channelId, DepartmentId, UserId, input.AsUnitId, input.Seq, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Pins a message in its channel. Requires channel moderator rights.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <returns>ChatActionResult indicating whether the message was pinned</returns>
		[HttpPost("PinMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> PinMessage(string messageId, CancellationToken cancellationToken)
		{
			return await SetPinnedAsync(messageId, true, cancellationToken);
		}

		/// <summary>
		/// Unpins a message in its channel. Requires channel moderator rights.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <returns>ChatActionResult indicating whether the message was unpinned</returns>
		[HttpDelete("UnpinMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> UnpinMessage(string messageId, CancellationToken cancellationToken)
		{
			return await SetPinnedAsync(messageId, false, cancellationToken);
		}

		/// <summary>
		/// Returns the pinned messages for a channel.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <returns>Array of ChatMessageResultData objects for the pinned messages</returns>
		[HttpGet("GetPins")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessagesResult>> GetPins(string channelId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			var result = new GetChatMessagesResult();
			var messages = await _chatMessageService.GetPinnedMessagesAsync(channelId);

			await PopulateMessagesResultAsync(result, messages);

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Reactions, Acks, Read Pointers and Pins

		#region Attachments

		/// <summary>
		/// Uploads an attachment for a message the current user already sent, using multipart/form-data.
		/// Allowed types: png, jpeg, gif, webp and pdf up to the configured size limit.
		/// </summary>
		/// <param name="channelId">Chat channel identifier</param>
		/// <param name="messageId">Chat message identifier the attachment belongs to</param>
		/// <param name="file">The file being uploaded</param>
		/// <returns>ChatAttachmentUploadedResult with the new attachment identifier</returns>
		[HttpPost("UploadAttachment")]
		[Consumes("multipart/form-data")]
		[RequestSizeLimit(MaxAttachmentRequestBytes)]
		[RequestFormLimits(MultipartBodyLengthLimit = MaxAttachmentRequestBytes)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatAttachmentUploadedResult>> UploadAttachment(string channelId, string messageId, [FromForm] IFormFile file, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (String.IsNullOrWhiteSpace(channelId) || String.IsNullOrWhiteSpace(messageId) || file == null || file.Length <= 0)
				return BadRequest();

			if (await IsRateLimitedAsync("upload", ChatConfig.UploadRateLimitPerWindow))
				return RateLimitedResult<ChatAttachmentUploadedResult>();

			var settings = await _chatChannelService.GetDepartmentSettingsAsync(DepartmentId);

			var maxAttachmentSizeMb = ChatConfig.MaxAttachmentSizeMb;
			if (settings != null && settings.MaxAttachmentSizeMb > 0)
				maxAttachmentSizeMb = Math.Min(settings.MaxAttachmentSizeMb, ChatConfig.MaxAttachmentSizeMb);

			if (file.Length > (long)maxAttachmentSizeMb * 1024 * 1024)
				return BadRequest();

			if (!AllowedAttachmentContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
				return BadRequest();

			if (settings != null && !settings.AllowImages &&
				file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
				return BadRequest();

			var channel = await _chatChannelService.GetChannelByIdAsync(channelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanPostAsync(channel, UserId, null))
				return Unauthorized();

			var message = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (message == null || message.ChatChannelId != channelId || !String.Equals(message.SenderUserId, UserId, StringComparison.OrdinalIgnoreCase))
				return BadRequest();

			byte[] data;
			await using (var stream = new MemoryStream())
			{
				await file.CopyToAsync(stream, cancellationToken);
				data = stream.ToArray();
			}

			var attachment = new ChatAttachment
			{
				ChatAttachmentId = Guid.NewGuid().ToString(),
				ChatMessageId = messageId,
				ChatChannelId = channelId,
				DepartmentId = DepartmentId,
				FileName = file.FileName,
				ContentType = file.ContentType,
				Size = data.LongLength,
				Sha256 = Convert.ToHexString(SHA256.HashData(data)),
				Data = data,
				UploadedByUserId = UserId,
				UploadedOn = DateTime.UtcNow
			};

			var saved = await _chatAttachmentRepository.InsertAsync(attachment, cancellationToken);

			var result = new ChatAttachmentUploadedResult();

			if (saved != null)
			{
				result.ChatAttachmentId = saved.ChatAttachmentId;
				result.PageSize = 1;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Downloads a chat attachment's file data.
		/// </summary>
		/// <param name="attachmentId">Chat attachment identifier</param>
		/// <returns>The attachment file</returns>
		[HttpGet("GetAttachment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> GetAttachment(string attachmentId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var attachment = await _chatAttachmentRepository.GetByIdAsync(attachmentId);
			if (attachment == null || attachment.DepartmentId != DepartmentId || attachment.Data == null)
				return NotFound();

			var message = await _chatMessageService.GetMessageByIdAsync(attachment.ChatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(attachment.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			return File(attachment.Data, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
		}

		/// <summary>
		/// Downloads a chat attachment's thumbnail (falls back to the full file when no thumbnail exists).
		/// </summary>
		/// <param name="attachmentId">Chat attachment identifier</param>
		/// <returns>The attachment thumbnail image</returns>
		[HttpGet("GetAttachmentThumbnail")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<IActionResult> GetAttachmentThumbnail(string attachmentId)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var attachment = await _chatAttachmentRepository.GetByIdAsync(attachmentId);
			if (attachment == null || attachment.DepartmentId != DepartmentId || (attachment.ThumbnailData == null && attachment.Data == null))
				return NotFound();

			var message = await _chatMessageService.GetMessageByIdAsync(attachment.ChatMessageId);
			if (message == null || message.DeletedOn.HasValue)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(attachment.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			return File(attachment.ThumbnailData ?? attachment.Data, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
		}

		#endregion Attachments

		#region Search, GIFs, Presence and Flags

		/// <summary>
		/// Searches message bodies across every channel the user can access (or one channel when supplied).
		/// </summary>
		/// <param name="q">Search text</param>
		/// <param name="channelId">Optional channel to limit the search to</param>
		/// <param name="from">Optional start of the date range</param>
		/// <param name="to">Optional end of the date range</param>
		/// <param name="page">Page number</param>
		/// <param name="pageSize">Page size</param>
		/// <returns>Array of ChatMessageResultData objects matching the search</returns>
		[HttpGet("Search")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatMessagesResult>> Search([StringLength(200)] string q, string channelId = null, DateTime? from = null, DateTime? to = null, int page = 0, int pageSize = 50)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (String.IsNullOrWhiteSpace(q))
				return BadRequest();

			var result = new GetChatMessagesResult();
			var messages = await _chatMessageService.SearchAsync(DepartmentId, UserId, null, q, channelId, from, to, page, pageSize);

			await PopulateMessagesResultAsync(result, messages);
			result.Page = page;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Searches the configured GIF provider. An empty query returns trending GIFs; when no provider is
		/// configured an empty successful result is returned.
		/// </summary>
		/// <param name="q">Search text (empty for trending)</param>
		/// <param name="limit">Maximum number of GIFs to return</param>
		/// <param name="offset">Result offset for paging</param>
		/// <returns>Array of GifResultData objects from the provider</returns>
		[HttpGet("SearchGifs")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetGifSearchResult>> SearchGifs([StringLength(200)] string q = null, int limit = 25, int offset = 0)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (await IsRateLimitedAsync("gifsearch", ChatConfig.GifSearchRateLimitPerWindow))
				return RateLimitedResult<GetGifSearchResult>();

			var result = new GetGifSearchResult();

			if (_gifProvider.IsConfigured)
			{
				var gifs = String.IsNullOrWhiteSpace(q)
					? await _gifProvider.TrendingAsync(limit)
					: await _gifProvider.SearchAsync(q, limit, offset);

				if (gifs != null && gifs.Any())
				{
					foreach (var gif in gifs)
					{
						result.Data.Add(new GifResultData
						{
							Id = gif.Id,
							Title = gif.Title,
							PreviewUrl = gif.PreviewUrl,
							GifUrl = gif.GifUrl,
							Width = gif.Width,
							Height = gif.Height
						});
					}
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Returns which of the requested users are currently online in chat.
		/// </summary>
		/// <param name="userIds">Comma-separated list of UserIds to check</param>
		/// <returns>GetChatPresenceResult with the subset of UserIds currently online</returns>
		[HttpGet("GetPresence")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<GetChatPresenceResult>> GetPresence(string userIds)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (String.IsNullOrWhiteSpace(userIds))
				return BadRequest();

			var ids = userIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

			if (ids.Count > 200)
				return BadRequest();

			var result = new GetChatPresenceResult();
			var online = await _chatPresenceService.GetOnlineUsersAsync(DepartmentId, ids);

			if (online != null)
				result.OnlineUserIds = online;

			result.PageSize = result.OnlineUserIds.Count;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Flags a message for moderator review.
		/// </summary>
		/// <param name="messageId">Chat message identifier</param>
		/// <param name="input">Reason and optional note for the flag</param>
		/// <returns>ChatActionResult indicating whether the flag was recorded</returns>
		[HttpPost("FlagMessage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ChatActionResult>> FlagMessage(string messageId, [FromBody] FlagMessageInput input, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			if (!ModelState.IsValid)
				return BadRequest();

			if (input == null)
				return BadRequest();

			var accessCheck = await CheckMessageChannelAccessAsync(messageId);
			if (accessCheck != null)
				return accessCheck;

			var result = new ChatActionResult();
			var flag = await _moderationService.FlagAsync(DepartmentId, UserId,
				ModerationItemType.ChatMessage, messageId, (ModerationReason)input.Reason, input.Note,
				BuildModerationContext("Reporter"), cancellationToken);

			result.Success = flag != null;
			result.Status = result.Success ? ResponseHelper.Created : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion Search, GIFs, Presence and Flags

		#region Private Helpers

		private Task<bool> ChatEnabledAsync()
		{
			return _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, DepartmentId);
		}

		private async Task<bool> IsRateLimitedAsync(string action, int limitPerWindow)
		{
			if (limitPerWindow <= 0 || ChatConfig.RateLimitWindowSeconds <= 0)
				return false;

			var count = await _cacheProvider.IncrementAsync($"chat:rl:{action}:{UserId}", TimeSpan.FromSeconds(ChatConfig.RateLimitWindowSeconds));

			return count > limitPerWindow;
		}

		private ActionResult<T> RateLimitedResult<T>() where T : StandardApiResponseV4Base, new()
		{
			var result = new T { Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(result);
			return StatusCode(StatusCodes.Status429TooManyRequests, result);
		}

		/// <summary>
		/// Verifies the message exists in this department and the user can access its channel.
		/// Returns null when access is allowed, otherwise the error result to return.
		/// </summary>
		private async Task<ActionResult> CheckMessageChannelAccessAsync(string messageId)
		{
			var message = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (message == null || message.DepartmentId != DepartmentId)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(message.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, UserId, null))
				return Unauthorized();

			return null;
		}

		private async Task<ActionResult<ChatActionResult>> SetPinnedAsync(string messageId, bool pinned, CancellationToken cancellationToken)
		{
			if (!await ChatEnabledAsync())
				return NotFound();

			var message = await _chatMessageService.GetMessageByIdAsync(messageId);
			if (message == null || message.DepartmentId != DepartmentId)
				return NotFound();

			var channel = await _chatChannelService.GetChannelByIdAsync(message.ChatChannelId);
			if (channel == null || channel.DepartmentId != DepartmentId)
				return NotFound();

			if (!await _chatPermissionService.CanModerateChannelAsync(channel, UserId))
				return Unauthorized();

			var result = new ChatActionResult();
			result.Success = await _chatMessageService.SetMessagePinnedAsync(messageId, UserId, pinned, cancellationToken);
			result.Status = result.Success ? ResponseHelper.Updated : ResponseHelper.Failure;

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private async Task PopulateMessagesResultAsync(GetChatMessagesResult result, List<ChatMessage> messages)
		{
			if (messages != null && messages.Any())
			{
				result.Data = await ConvertMessagesAsync(messages);
				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}
		}

		private async Task<List<ChatMessageResultData>> ConvertMessagesAsync(List<ChatMessage> messages)
		{
			var data = new List<ChatMessageResultData>();

			if (messages == null || !messages.Any())
				return data;

			var messageIds = messages.Select(x => x.ChatMessageId).ToList();
			var reactions = await _chatMessageService.GetReactionsForMessagesAsync(messageIds);
			var attachments = await _chatMessageService.GetAttachmentMetadataForMessagesAsync(messageIds);

			// Batch-fetch every distinct channel in one query, then compute the moderation flag once per
			// channel — up front, out of the per-message loop.
			var distinctChannelIds = messages
				.Select(x => x.ChatChannelId)
				.Where(id => !string.IsNullOrWhiteSpace(id))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			var channelsById = (await _chatChannelService.GetChannelsByIdsAsync(distinctChannelIds))
				.GroupBy(c => c.ChatChannelId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			var moderationByChannel = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (var channelId in distinctChannelIds)
			{
				var canModerate = channelsById.TryGetValue(channelId, out var channel)
					&& await _chatPermissionService.CanModerateChannelAsync(channel, UserId);
				moderationByChannel[channelId] = canModerate;
			}

			foreach (var message in messages)
			{
				moderationByChannel.TryGetValue(message.ChatChannelId ?? string.Empty, out var canModerate);

				data.Add(ConvertMessageResultData(message,
					reactions?.Where(x => x.ChatMessageId == message.ChatMessageId),
					attachments?.Where(x => x.ChatMessageId == message.ChatMessageId),
					canModerate));
			}

			return data;
		}

		private static ChatMessageResultData ConvertMessageResultData(ChatMessage message, IEnumerable<ChatMessageReaction> reactions, IEnumerable<ChatAttachment> attachments, bool includeModeratorInternals)
		{
			var data = new ChatMessageResultData
			{
				ChatMessageId = message.ChatMessageId,
				ChatChannelId = message.ChatChannelId,
				DepartmentId = message.DepartmentId,
				MessageSeq = message.MessageSeq,
				SenderParticipantType = message.SenderParticipantType,
				SenderUserId = message.SenderUserId,
				SenderUnitId = message.SenderUnitId,
				SenderDisplayName = message.SenderDisplayName,
				Body = message.Body,
				MessageType = message.MessageType,
				Priority = message.Priority,
				ThreadRootMessageId = message.ThreadRootMessageId,
				ThreadReplyCount = message.ThreadReplyCount,
				LastThreadReplyOn = message.LastThreadReplyOn,
				AlsoSendToChannel = message.AlsoSendToChannel,
				MetadataJson = message.MetadataJson,
				ClientMessageId = message.ClientMessageId,
				SentOn = message.SentOn,
				EditedOn = message.EditedOn,
				DeletedOn = message.DeletedOn,
				DeletedByUserId = includeModeratorInternals ? message.DeletedByUserId : null,
				IsModerated = message.IsModerated,
				PinnedOn = message.PinnedOn,
				PinnedByUserId = includeModeratorInternals ? message.PinnedByUserId : null
			};

			if (reactions != null)
			{
				foreach (var reaction in reactions)
				{
					data.Reactions.Add(new ChatReactionResultData
					{
						Emoji = reaction.Emoji,
						ParticipantType = reaction.ParticipantType,
						UserId = reaction.UserId,
						UnitId = reaction.UnitId
					});
				}
			}

			if (attachments != null)
			{
				foreach (var attachment in attachments)
				{
					data.Attachments.Add(new ChatAttachmentResultData
					{
						ChatAttachmentId = attachment.ChatAttachmentId,
						FileName = attachment.FileName,
						ContentType = attachment.ContentType,
						Size = attachment.Size
					});
				}
			}

			return data;
		}

		private static ChatChannelResultData ConvertChannelResultData(ChatChannel channel, ChatChannelMember member)
		{
			return new ChatChannelResultData
			{
				ChatChannelId = channel.ChatChannelId,
				ChannelType = channel.ChannelType,
				Name = channel.Name,
				Topic = channel.Topic,
				GroupId = channel.GroupId,
				CallId = channel.CallId,
				CommandStructureNodeId = channel.CommandStructureNodeId,
				OwnerUserId = channel.OwnerUserId,
				IsArchived = channel.IsArchived,
				IsLocked = channel.IsLocked,
				LastMessageSeq = channel.LastMessageSeq,
				LastMessageOn = channel.LastMessageOn,
				CreatedOn = channel.CreatedOn,
				UnreadCount = Math.Max(0, channel.LastMessageSeq - (member?.LastReadSeq ?? 0)),
				NotificationPreference = member?.NotificationPreference ?? 0,
				MyLastReadSeq = member?.LastReadSeq ?? 0
			};
		}

		private static ChatMemberResultData ConvertMemberResultData(ChatChannelMember member, bool includeModeratorInternals)
		{
			return new ChatMemberResultData
			{
				ChatChannelMemberId = member.ChatChannelMemberId,
				ChatChannelId = member.ChatChannelId,
				ParticipantType = member.ParticipantType,
				UserId = member.UserId,
				UnitId = member.UnitId,
				DisplayNameOverride = member.DisplayNameOverride,
				IsModerator = member.IsModerator,
				JoinedOn = member.JoinedOn,
				RemovedOn = member.RemovedOn,
				LastReadSeq = includeModeratorInternals ? member.LastReadSeq : null,
				LastReadOn = member.LastReadOn,
				LastDeliveredSeq = includeModeratorInternals ? member.LastDeliveredSeq : null,
				MutedUntil = includeModeratorInternals ? member.MutedUntil : null,
				IsBanned = includeModeratorInternals ? member.IsBanned : null,
				NotificationPreference = member.NotificationPreference
			};
		}

		private static ChatAckResultData ConvertAckResultData(ChatMessageAck ack)
		{
			return new ChatAckResultData
			{
				ChatMessageAckId = ack.ChatMessageAckId,
				ChatMessageId = ack.ChatMessageId,
				ChatChannelId = ack.ChatChannelId,
				UserId = ack.UserId,
				UnitId = ack.UnitId,
				RequiredOn = ack.RequiredOn,
				AcknowledgedOn = ack.AcknowledgedOn
			};
		}

		private ChatModerationContext BuildModerationContext(string actorRole)
		{
			return new ChatModerationContext
			{
				IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
				UserAgent = Request?.Headers["User-Agent"].ToString(),
				TraceId = HttpContext?.TraceIdentifier,
				ActorRole = actorRole
			};
		}

		#endregion Private Helpers
	}
}

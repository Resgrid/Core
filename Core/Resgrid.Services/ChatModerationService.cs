using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Chat moderation. Every action writes an immutable ChatModerationActions row, mirrors to the
	/// department AuditLog, invalidates cached permission evaluations and pings moderators/clients via
	/// the chat event pipeline. Callers gate with IChatPermissionService.CanModerateChannelAsync.
	/// </summary>
	public class ChatModerationService : IChatModerationService
	{
		private readonly IChatMessageFlagRepository _chatMessageFlagRepository;
		private readonly IChatModerationActionRepository _chatModerationActionRepository;
		private readonly IChatExportRepository _chatExportRepository;
		private readonly IChatChannelRepository _chatChannelRepository;
		private readonly IChatChannelMemberRepository _chatChannelMemberRepository;
		private readonly IChatMessageRepository _chatMessageRepository;
		private readonly IChatMessageService _chatMessageService;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IAuditService _auditService;
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		private readonly IEventAggregator _eventAggregator;

		public ChatModerationService(IChatMessageFlagRepository chatMessageFlagRepository, IChatModerationActionRepository chatModerationActionRepository,
			IChatExportRepository chatExportRepository, IChatChannelRepository chatChannelRepository, IChatChannelMemberRepository chatChannelMemberRepository,
			IChatMessageRepository chatMessageRepository, IChatMessageService chatMessageService, IChatChannelService chatChannelService,
			IChatPermissionService chatPermissionService, IAuditService auditService, IEventAggregator eventAggregator,
			Lazy<IProtectedWriteService> protectedWriteService)
		{
			_chatMessageFlagRepository = chatMessageFlagRepository;
			_chatModerationActionRepository = chatModerationActionRepository;
			_chatExportRepository = chatExportRepository;
			_chatChannelRepository = chatChannelRepository;
			_chatChannelMemberRepository = chatChannelMemberRepository;
			_chatMessageRepository = chatMessageRepository;
			_chatMessageService = chatMessageService;
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_auditService = auditService;
			_protectedWriteService = protectedWriteService;
			_eventAggregator = eventAggregator;
		}

		public async Task<ChatMessageFlag> FlagMessageAsync(int departmentId, string chatMessageId, string flaggedByUserId, ChatFlagReason reason, string note, CancellationToken cancellationToken = default(CancellationToken))
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DepartmentId != departmentId)
				return null;

			var channel = await _chatChannelRepository.GetByIdAsync(message.ChatChannelId);
			if (channel == null || channel.DepartmentId != departmentId ||
				!await _chatPermissionService.CanAccessChannelAsync(channel, flaggedByUserId, null))
				return null;

			// Dedupe: an open flag by the same user on the same message is returned, not duplicated.
			var existing = await _chatMessageFlagRepository.GetActiveFlagAsync(chatMessageId, flaggedByUserId);
			if (existing != null)
				return existing;

			var newFlag = new ChatMessageFlag
			{
				ChatMessageFlagId = Guid.NewGuid().ToString(),
				ChatMessageId = chatMessageId,
				ChatChannelId = message.ChatChannelId,
				DepartmentId = message.DepartmentId,
				FlaggedByUserId = flaggedByUserId,
				Reason = (int)reason,
				Note = note,
				FlaggedOn = DateTime.UtcNow,
				Status = (int)ChatFlagStatus.Open
			};

			// ADP write safety net (plan 5.3, catalog v8). The id is assigned here rather than by the
			// database, so the row is enveloped BEFORE the insert and no plaintext ever reaches the
			// table. Fails closed: a flag that cannot be protected is not written.
			await ProtectChatModerationWriteAsync(
				() => _protectedWriteService.Value.PrepareChatMessageFlagWriteAsync(newFlag.DepartmentId, newFlag,
					null, null, workloadCaller: true, cancellationToken),
				$"chat message flag {newFlag.ChatMessageFlagId}");

			var flag = await _chatMessageFlagRepository.InsertAsync(newFlag, cancellationToken);

			PublishModerationEvent(message.DepartmentId, message.ChatChannelId, new { Type = "flagged", flag.ChatMessageFlagId, chatMessageId });

			return flag;
		}

		public async Task<List<ChatMessageFlag>> GetFlagsAsync(int departmentId, string byUserId, ChatFlagStatus status, int page, int pageSize)
		{
			if (!await _chatPermissionService.IsDepartmentAdminAsync(departmentId, byUserId))
				return new List<ChatMessageFlag>();

			var flags = await _chatMessageFlagRepository.GetByStatusAsync(departmentId, (int)status, Math.Max(page, 1), pageSize <= 0 ? 25 : Math.Min(pageSize, 100));
			return flags?.ToList() ?? new List<ChatMessageFlag>();
		}

		public async Task<ChatMessageFlag> ResolveFlagAsync(string chatMessageFlagId, int departmentId, string byUserId, ChatFlagStatus resolution, string resolutionNote, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			if (!await _chatPermissionService.IsDepartmentAdminAsync(departmentId, byUserId))
				return null;

			var flag = await _chatMessageFlagRepository.GetByIdAsync(chatMessageFlagId);
			if (flag == null || flag.DepartmentId != departmentId)
				return null;

			// Only open flags transition; resolved flags are never reopened or re-resolved.
			if (flag.Status != (int)ChatFlagStatus.Open)
				return null;

			flag.Status = (int)resolution;
			flag.ReviewedByUserId = byUserId;
			flag.ReviewedOn = DateTime.UtcNow;
			flag.ResolutionNote = resolutionNote;

			var saved = await _chatMessageFlagRepository.UpdateAsync(flag, cancellationToken);

			await RecordActionAsync(flag.DepartmentId, flag.ChatChannelId, flag.ChatMessageId, null, null,
				ChatModerationActionType.ResolveFlag, byUserId, resolutionNote,
				JsonConvert.SerializeObject(new { flag.ChatMessageFlagId, Resolution = resolution.ToString() }),
				AuditLogTypes.ChatFlagResolved, cancellationToken, context);

			return saved;
		}

		public async Task<bool> ModeratorDeleteMessageAsync(int departmentId, string chatMessageId, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			var message = await _chatMessageRepository.GetByIdAsync(chatMessageId);
			if (message == null || message.DepartmentId != departmentId ||
				await ResolveModeratedChannelAsync(departmentId, message.ChatChannelId, byUserId) == null)
				return false;

			var deleted = await _chatMessageService.DeleteMessageAsync(departmentId, chatMessageId, byUserId, asModerator: true, reason, cancellationToken);
			if (!deleted)
				return false;

			await RecordActionAsync(message.DepartmentId, message.ChatChannelId, chatMessageId, message.SenderUserId, message.SenderUnitId,
				ChatModerationActionType.DeleteMessage, byUserId, reason, null, AuditLogTypes.ChatMessageDeletedByModerator, cancellationToken, context);

			return true;
		}

		public async Task<bool> SetUserMutedAsync(int departmentId, string chatChannelId, string targetUserId, DateTime? mutedUntil, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			var channel = await ResolveModeratedChannelAsync(departmentId, chatChannelId, byUserId);
			if (channel == null)
				return false;

			var member = await GetModerationTargetMemberAsync(channel, targetUserId, cancellationToken);
			if (member == null)
				return false;

			await _chatChannelMemberRepository.SetMemberMutedAsync(member.ChatChannelMemberId, mutedUntil, cancellationToken);

			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			var muted = mutedUntil.HasValue && mutedUntil.Value > DateTime.UtcNow;
			await RecordActionAsync(channel.DepartmentId, chatChannelId, null, targetUserId, null,
				muted ? ChatModerationActionType.MuteUser : ChatModerationActionType.UnmuteUser, byUserId, reason,
				JsonConvert.SerializeObject(new { MutedUntil = mutedUntil }),
				muted ? AuditLogTypes.ChatUserMuted : AuditLogTypes.ChatUserUnmuted, cancellationToken, context);

			return true;
		}

		public async Task<bool> SetUserBannedAsync(int departmentId, string chatChannelId, string targetUserId, bool banned, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			var channel = await ResolveModeratedChannelAsync(departmentId, chatChannelId, byUserId);
			if (channel == null)
				return false;

			var member = await GetModerationTargetMemberAsync(channel, targetUserId, cancellationToken);
			if (member == null)
				return false;

			await _chatChannelMemberRepository.SetMemberBannedAsync(member.ChatChannelMemberId, banned, banned ? byUserId : null, cancellationToken);

			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			await RecordActionAsync(channel.DepartmentId, chatChannelId, null, targetUserId, null,
				banned ? ChatModerationActionType.BanUser : ChatModerationActionType.UnbanUser, byUserId, reason, null,
				banned ? AuditLogTypes.ChatUserBanned : AuditLogTypes.ChatUserUnbanned, cancellationToken, context);

			return true;
		}

		public async Task<bool> SetChannelLockedAsync(int departmentId, string chatChannelId, bool locked, string byUserId, string reason, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			var channel = await ResolveModeratedChannelAsync(departmentId, chatChannelId, byUserId);
			if (channel == null)
				return false;

			// Targeted update: a full-row write would rewind LastMessageSeq/LastMessageOn over the
			// atomic allocator's work.
			var lockedOn = locked ? DateTime.UtcNow : (DateTime?)null;
			await _chatChannelRepository.SetLockedAsync(chatChannelId, locked, locked ? byUserId : null, lockedOn, DateTime.UtcNow, cancellationToken);

			await _chatPermissionService.InvalidateChannelCacheAsync(chatChannelId);

			await RecordActionAsync(channel.DepartmentId, chatChannelId, null, null, null,
				locked ? ChatModerationActionType.LockChannel : ChatModerationActionType.UnlockChannel, byUserId, reason, null,
				locked ? AuditLogTypes.ChatChannelLocked : AuditLogTypes.ChatChannelUnlocked, cancellationToken, context);

			return true;
		}

		public async Task<List<ChatModerationAction>> GetModerationActionsAsync(int departmentId, string byUserId, string chatChannelId, int page, int pageSize)
		{
			if (!await _chatPermissionService.IsDepartmentAdminAsync(departmentId, byUserId))
				return new List<ChatModerationAction>();

			if (!string.IsNullOrWhiteSpace(chatChannelId))
			{
				var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
				if (channel == null || channel.DepartmentId != departmentId)
					return new List<ChatModerationAction>();
			}

			var actions = await _chatModerationActionRepository.GetByDepartmentAsync(departmentId, chatChannelId, Math.Max(page, 1), pageSize <= 0 ? 25 : Math.Min(pageSize, 100));
			return actions?.ToList() ?? new List<ChatModerationAction>();
		}

		public async Task<ChatExport> RequestExportAsync(int departmentId, string byUserId, string chatChannelId, DateTime? startDate, DateTime? endDate, ChatExportFormat format, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			if (!await _chatPermissionService.IsDepartmentAdminAsync(departmentId, byUserId))
				return null;

			if (!string.IsNullOrWhiteSpace(chatChannelId))
			{
				var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
				if (channel == null || channel.DepartmentId != departmentId)
					return null;
			}

			// Queued exports carry no payload yet; the worker runs the same net when it attaches the
			// archive (ChatExportLogic).
			var export = await _chatExportRepository.InsertAsync(new ChatExport
			{
				ChatExportId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				RequestedByUserId = byUserId,
				RequestedOn = DateTime.UtcNow,
				ChatChannelId = chatChannelId,
				StartDate = startDate,
				EndDate = endDate,
				Format = (int)format,
				Status = (int)ChatExportStatus.Queued
			}, cancellationToken);

			await RecordActionAsync(departmentId, chatChannelId, null, null, null,
				ChatModerationActionType.ExportRequested, byUserId, null,
				JsonConvert.SerializeObject(new { export.ChatExportId, StartDate = startDate, EndDate = endDate, Format = format.ToString() }),
				AuditLogTypes.ChatExportRequested, cancellationToken, context);

			return export;
		}

		public async Task<List<ChatExport>> GetExportsAsync(int departmentId, string byUserId)
		{
			if (!await _chatPermissionService.IsDepartmentAdminAsync(departmentId, byUserId))
				return new List<ChatExport>();

			var exports = await _chatExportRepository.GetMetadataByDepartmentIdAsync(departmentId);
			return exports?.ToList() ?? new List<ChatExport>();
		}

		public async Task<ChatExport> GetExportForDownloadAsync(string chatExportId, int departmentId, string byUserId, CancellationToken cancellationToken = default(CancellationToken), ChatModerationContext context = null)
		{
			if (!await _chatPermissionService.IsDepartmentAdminAsync(departmentId, byUserId))
				return null;

			var export = await _chatExportRepository.GetByIdAsync(chatExportId);
			if (export == null || export.DepartmentId != departmentId || export.Status != (int)ChatExportStatus.Complete)
				return null;

			await RecordActionAsync(departmentId, export.ChatChannelId, null, null, null,
				ChatModerationActionType.ExportDownloaded, byUserId, null,
				JsonConvert.SerializeObject(new { export.ChatExportId }),
				AuditLogTypes.ChatExportDownloaded, cancellationToken, context);

			return export;
		}

		private async Task<ChatChannel> ResolveModeratedChannelAsync(int departmentId, string chatChannelId, string byUserId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(chatChannelId) || string.IsNullOrWhiteSpace(byUserId))
				return null;

			var channel = await _chatChannelRepository.GetByIdAsync(chatChannelId);
			if (channel == null || channel.DepartmentId != departmentId ||
				!await _chatPermissionService.CanModerateChannelAsync(channel, byUserId))
				return null;

			return channel;
		}

		private async Task<ChatChannelMember> GetModerationTargetMemberAsync(ChatChannel channel, string targetUserId,
			CancellationToken cancellationToken)
		{
			if (channel == null || string.IsNullOrWhiteSpace(targetUserId))
				return null;

			var member = await _chatChannelMemberRepository.GetUserMemberAsync(channel.ChatChannelId, targetUserId);
			if (member != null)
				return member.DepartmentId == channel.DepartmentId ? member : null;

			if (!await _chatPermissionService.CanAccessChannelAsync(channel, targetUserId, null))
				return null;

			member = await _chatChannelService.EnsureMemberStateAsync(channel.ChatChannelId, channel.DepartmentId,
				targetUserId, null, cancellationToken);

			return member != null && member.DepartmentId == channel.DepartmentId ? member : null;
		}

		/// <summary>
		/// ADP write safety net (plan 5.3, catalog v8). Chat moderation rows quote the message that
		/// was reported and the moderator's reasoning, so they are enveloped before they are written.
		/// Fails closed by throwing rather than storing the quote in the clear.
		/// </summary>
		private static async Task ProtectChatModerationWriteAsync(Func<Task<ProtectedWriteResult>> prepare, string description)
		{
			var result = await prepare();
			if (!result.Success)
				throw new InvalidOperationException($"Protected write blocked ({result.Reason}); {description} was NOT saved.");
		}

		private async Task RecordActionAsync(int departmentId, string chatChannelId, string chatMessageId, string targetUserId, int? targetUnitId,
			ChatModerationActionType actionType, string byUserId, string reason, string detailsJson, AuditLogTypes auditLogType, CancellationToken cancellationToken,
			ChatModerationContext context = null)
		{
			var action = new ChatModerationAction
			{
				ChatModerationActionId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ChatChannelId = chatChannelId,
				ChatMessageId = chatMessageId,
				TargetUserId = targetUserId,
				TargetUnitId = targetUnitId,
				ActionType = (int)actionType,
				PerformedByUserId = byUserId,
				PerformedOn = DateTime.UtcNow,
				Reason = reason,
				DetailsJson = detailsJson
			};

			await ProtectChatModerationWriteAsync(
				() => _protectedWriteService.Value.PrepareChatModerationActionWriteAsync(departmentId, action,
					null, null, workloadCaller: true, cancellationToken),
				$"chat moderation action {action.ChatModerationActionId}");

			await _chatModerationActionRepository.InsertAsync(action, cancellationToken);

			// Mirror to the department audit trail with full forensic context for SIEM ingestion. This
			// is only reached after the action succeeded, so result is always Success; a failed action
			// returns before RecordActionAsync. IpAddress/UserAgent/TraceId/ActorRole come from the
			// request (null for background-originated actions); ServerName is always captured.
			await _auditService.SaveAuditLogAsync(new AuditLog
			{
				LogType = (int)auditLogType,
				DepartmentId = departmentId,
				UserId = byUserId,
				Message = _auditService.GetAuditLogTypeString(auditLogType),
				Data = JsonConvert.SerializeObject(new
				{
					result = "Success",
					action = actionType.ToString(),
					actorRole = context?.ActorRole,
					traceId = context?.TraceId,
					chatChannelId,
					chatMessageId,
					targetUserId,
					targetUnitId,
					reason,
					detailsJson
				}),
				LoggedOn = DateTime.UtcNow,
				ObjectId = chatChannelId,
				ObjectDepartmentId = departmentId,
				IpAddress = context?.IpAddress,
				UserAgent = context?.UserAgent,
				ServerName = Environment.MachineName
			}, cancellationToken);

			PublishModerationEvent(departmentId, chatChannelId, new { Type = actionType.ToString(), chatMessageId, targetUserId, targetUnitId });
		}

		private void PublishModerationEvent(int departmentId, string chatChannelId, object payload)
		{
			_eventAggregator.SendMessage<ChatEventRaised>(new ChatEventRaised
			{
				DepartmentId = departmentId,
				ChatChannelId = chatChannelId,
				Kind = ChatEventKinds.ModerationApplied,
				PayloadJson = JsonConvert.SerializeObject(payload)
			});
		}
	}
}

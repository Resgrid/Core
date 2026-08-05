using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Localization.Areas.User.Moderation;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Content-agnostic moderation requests for chat messages, Resgrid Messages, call notes and call
	/// images. One request owns every report for an item and preserves the first-seen evidence forever.
	/// </summary>
	public class ModerationService : IModerationService
	{
		public static string ModeratedChatMessage => ModerationResources.GetCurrent("MessageRemovedByModeration");
		public static string ModeratedMessageSubject => ModerationResources.GetCurrent("ModeratedMessageSubject");
		public static string ModeratedMessageBody => ModerationResources.GetCurrent("ModeratedMessageBody");
		public static string ModeratedCallNote => ModerationResources.GetCurrent("ModeratedCallNote");

		private readonly IModerationRequestRepository _moderationRequestRepository;
		private readonly IModerationReportRepository _moderationReportRepository;
		private readonly IModerationActionRepository _moderationActionRepository;
		private readonly IChatMessageRepository _chatMessageRepository;
		private readonly IChatAttachmentRepository _chatAttachmentRepository;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatPermissionService _chatPermissionService;
		private readonly IChatMessageService _chatMessageService;
		private readonly IMessageService _messageService;
		private readonly ICallNotesRepository _callNotesRepository;
		private readonly ICallAttachmentRepository _callAttachmentRepository;
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IAuditService _auditService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IOutboundQueueProvider _outboundQueueProvider;

		public ModerationService(IModerationRequestRepository moderationRequestRepository,
			IModerationReportRepository moderationReportRepository, IModerationActionRepository moderationActionRepository,
			IChatMessageRepository chatMessageRepository, IChatAttachmentRepository chatAttachmentRepository,
			IChatChannelService chatChannelService, IChatPermissionService chatPermissionService,
			IChatMessageService chatMessageService, IMessageService messageService,
			ICallNotesRepository callNotesRepository, ICallAttachmentRepository callAttachmentRepository,
			ICallsService callsService, IDepartmentGroupsService departmentGroupsService,
			IAuthorizationService authorizationService, IAuditService auditService,
			IUserProfileService userProfileService, IUnitOfWork unitOfWork,
			IOutboundQueueProvider outboundQueueProvider)
		{
			_moderationRequestRepository = moderationRequestRepository;
			_moderationReportRepository = moderationReportRepository;
			_moderationActionRepository = moderationActionRepository;
			_chatMessageRepository = chatMessageRepository;
			_chatAttachmentRepository = chatAttachmentRepository;
			_chatChannelService = chatChannelService;
			_chatPermissionService = chatPermissionService;
			_chatMessageService = chatMessageService;
			_messageService = messageService;
			_callNotesRepository = callNotesRepository;
			_callAttachmentRepository = callAttachmentRepository;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
			_auditService = auditService;
			_userProfileService = userProfileService;
			_unitOfWork = unitOfWork;
			_outboundQueueProvider = outboundQueueProvider;
		}

		public async Task<ModerationReport> FlagAsync(int departmentId, string reportedByUserId,
			ModerationItemType itemType, string itemId, ModerationReason reason, string note,
			ChatModerationContext context = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(reportedByUserId) || string.IsNullOrWhiteSpace(itemId))
				throw new ArgumentException(ModerationResources.GetCurrent("RequiredModerationContext"));

			if (!Enum.IsDefined(typeof(ModerationItemType), itemType) || !Enum.IsDefined(typeof(ModerationReason), reason))
				throw new ArgumentOutOfRangeException(nameof(itemType));

			var evidence = await LoadEvidenceAsync(departmentId, reportedByUserId, itemType, itemId);
			var request = await _moderationRequestRepository.GetByItemAsync(departmentId, (int)itemType, itemId);
			var createdRequest = request == null;

			if (createdRequest)
			{
				var now = DateTime.UtcNow;
				request = new ModerationRequest
				{
					ModerationRequestId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					ItemType = (int)itemType,
					ItemId = itemId,
					CallId = evidence.CallId,
					ChatChannelId = evidence.ChatChannelId,
					ContentAuthorUserId = evidence.ContentAuthorUserId,
					ContentAuthorUnitId = evidence.ContentAuthorUnitId,
					ContentCreatedOn = evidence.ContentCreatedOn,
					OriginalSubject = evidence.Subject,
					OriginalText = evidence.Text,
					OriginalFileName = evidence.FileName,
					OriginalContentType = evidence.ContentType,
					OriginalContent = evidence.Content,
					OriginalMetadataJson = evidence.MetadataJson,
					Status = (int)ModerationRequestStatus.Pending,
					Disposition = (int)ModerationDisposition.None,
					CreatedOn = now,
					ModifiedOn = now
				};

				try
				{
					request = await _moderationRequestRepository.InsertAsync(request, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					// The unique department/type/item index is the race backstop. If another reporter won
					// the insert, join that request; otherwise preserve the original failure.
					var concurrent = await _moderationRequestRepository.GetByItemAsync(departmentId, (int)itemType, itemId);
					if (concurrent == null)
						throw;

					request = concurrent;
					createdRequest = false;
				}
			}

			var existingReport = await _moderationReportRepository.GetByRequestAndReporterAsync(
				request.ModerationRequestId, reportedByUserId);
			if (existingReport != null)
				return existingReport;

			var reopenRequest = request.Status == (int)ModerationRequestStatus.Completed;
			if (reopenRequest)
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

			try
			{
				if (reopenRequest)
				{
					var previousStatus = request.Status;
					request.Status = (int)ModerationRequestStatus.Pending;
					request.Disposition = (int)ModerationDisposition.None;
					request.CompletedByUserId = null;
					request.CompletedOn = null;
					request.AdminNote = null;
					request.ModifiedOn = DateTime.UtcNow;
					await _moderationRequestRepository.UpdateAsync(request, cancellationToken);

					// The update holds the request row for this transaction. Recheck after acquiring it so
					// concurrent submissions can still return the winning report without a failed transaction.
					existingReport = await _moderationReportRepository.GetByRequestAndReporterAsync(
						request.ModerationRequestId, reportedByUserId);
					if (existingReport != null)
					{
						_unitOfWork.DiscardChanges();
						return existingReport;
					}

					await RecordActionAsync(request, ModerationActionType.RequestReopened, reportedByUserId,
						ModerationResources.GetCurrent("RequestReopenedAudit"), previousStatus, request.Status,
						context, null, cancellationToken);
					await RecordDepartmentAuditAsync(request, AuditLogTypes.ModerationRequestReopened,
						reportedByUserId, context, new { request.ItemType, request.ItemId }, cancellationToken);
				}

				var groupMember = await _departmentGroupsService.GetGroupMemberForUserAsync(reportedByUserId, departmentId);
				var report = new ModerationReport
				{
					ModerationReportId = Guid.NewGuid().ToString(),
					ModerationRequestId = request.ModerationRequestId,
					DepartmentId = departmentId,
					ReportedByUserId = reportedByUserId,
					ReporterGroupId = groupMember?.DepartmentGroupId,
					Reason = (int)reason,
					Note = note,
					ReportedOn = DateTime.UtcNow
				};

				try
				{
					report = await _moderationReportRepository.InsertAsync(report, cancellationToken);
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					if (reopenRequest)
						throw;

					// A unique request/reporter index prevents duplicate reports under concurrent submissions.
					var concurrent = await _moderationReportRepository.GetByRequestAndReporterAsync(
						request.ModerationRequestId, reportedByUserId);
					if (concurrent == null)
						throw;

					return concurrent;
				}
				request.ModifiedOn = report.ReportedOn;
				await _moderationRequestRepository.UpdateAsync(request, cancellationToken);

				await RecordActionAsync(request, ModerationActionType.ReportSubmitted, reportedByUserId, note,
					null, request.Status, context,
					new
					{
						report.ModerationReportId,
						report.ReporterGroupId,
						Reason = reason.ToString()
					},
					cancellationToken, createdRequest);

				await RecordDepartmentAuditAsync(request, AuditLogTypes.ModerationReportSubmitted,
					reportedByUserId, context,
					new { report.ModerationReportId, report.ReporterGroupId, Reason = reason.ToString(), note },
					cancellationToken);

				cancellationToken.ThrowIfCancellationRequested();
				if (reopenRequest)
					_unitOfWork.CommitChanges();

				return report;
			}
			catch
			{
				if (reopenRequest)
					_unitOfWork.DiscardChanges();

				throw;
			}
		}

		public async Task<bool> CanModerateAsync(int departmentId, string userId)
		{
			if (await _authorizationService.CanUserModifyDepartmentAsync(userId, departmentId))
				return true;

			var groups = await GetAdminGroupIdsAsync(departmentId, userId);
			return groups.Count > 0;
		}

		public async Task<List<ModerationRequest>> SearchRequestsAsync(int departmentId, string viewerUserId,
			ModerationSearchCriteria criteria)
		{
			var isDepartmentAdmin = await _authorizationService.CanUserModifyDepartmentAsync(viewerUserId, departmentId);
			var groupIds = isDepartmentAdmin ? null : await GetAdminGroupIdsAsync(departmentId, viewerUserId);
			var reporterScope = isDepartmentAdmin ? null : viewerUserId;
			var requests = await _moderationRequestRepository.SearchAsync(departmentId, criteria, groupIds, reporterScope);
			return await HydrateAsync(requests, isDepartmentAdmin ? null : groupIds, viewerUserId);
		}

		public async Task<ModerationRequest> GetRequestAsync(string moderationRequestId, int departmentId,
			string viewerUserId)
		{
			var request = await _moderationRequestRepository.GetByIdAsync(moderationRequestId);
			if (request == null || request.DepartmentId != departmentId)
				return null;

			var reports = (await _moderationReportRepository.GetByRequestAsync(moderationRequestId))?.ToList()
				?? new List<ModerationReport>();
			if (!await CanViewRequestAsync(request, reports, viewerUserId, requireAdmin: false))
				return null;

			var actions = (await _moderationActionRepository.GetByRequestAsync(moderationRequestId))?.ToList()
				?? new List<ModerationAction>();
			await ApplyViewerScopeAsync(request, reports, actions, viewerUserId);
			return request;
		}

		public async Task<ModerationRequest> GetReporterRequestAsync(int departmentId, string reporterUserId,
			ModerationItemType itemType, string itemId)
		{
			var request = await _moderationRequestRepository.GetByItemAsync(departmentId, (int)itemType, itemId);
			if (request == null)
				return null;

			var report = await _moderationReportRepository.GetByRequestAndReporterAsync(request.ModerationRequestId,
				reporterUserId);
			if (report == null)
				return null;

			request.Reports = new List<ModerationReport> { report };
			request.Actions = (await _moderationActionRepository.GetByRequestAsync(request.ModerationRequestId))?.ToList()
				?? new List<ModerationAction>();
			return request;
		}

		public async Task<List<ModerationRequest>> GetReporterRequestsAsync(int departmentId, string reporterUserId,
			ModerationItemType itemType, IEnumerable<string> itemIds)
		{
			return (await _moderationRequestRepository.GetByItemsAndReporterAsync(departmentId, (int)itemType,
				itemIds, reporterUserId))?.ToList() ?? new List<ModerationRequest>();
		}

		public async Task<ModerationRequest> CompleteRequestAsync(string moderationRequestId, int departmentId,
			string completedByUserId, ModerationDisposition disposition, string adminNote,
			ChatModerationContext context = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (disposition != ModerationDisposition.NoAction && disposition != ModerationDisposition.ContentRemoved)
				throw new ArgumentOutOfRangeException(nameof(disposition));

			var request = await _moderationRequestRepository.GetByIdAsync(moderationRequestId);
			if (request == null || request.DepartmentId != departmentId)
				return null;

			var reports = (await _moderationReportRepository.GetByRequestAsync(moderationRequestId))?.ToList()
				?? new List<ModerationReport>();
			if (!await CanViewRequestAsync(request, reports, completedByUserId, requireAdmin: true))
				throw new UnauthorizedAccessException(ModerationResources.GetCurrent("RequestOutsideScope"));

			if (request.Status != (int)ModerationRequestStatus.Pending)
				throw new InvalidOperationException(ModerationResources.GetCurrent("OnlyPendingRequests"));

			var useTransaction = disposition == ModerationDisposition.ContentRemoved;
			if (useTransaction)
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

			try
			{
				if (useTransaction && !await RemoveLiveContentAsync(request, completedByUserId, cancellationToken))
					throw new InvalidOperationException(ModerationResources.GetCurrent("ContentCouldNotBeRemoved"));

				var previousStatus = request.Status;
				request.Status = (int)ModerationRequestStatus.Completed;
				request.Disposition = (int)disposition;
				request.CompletedByUserId = completedByUserId;
				request.CompletedOn = DateTime.UtcNow;
				request.ModifiedOn = request.CompletedOn.Value;
				request.AdminNote = adminNote;
				request = await _moderationRequestRepository.UpdateAsync(request, cancellationToken);

				var actionType = useTransaction
					? ModerationActionType.ContentRemoved
					: ModerationActionType.CompletedNoAction;
				await RecordActionAsync(request, actionType, completedByUserId, adminNote, previousStatus,
					request.Status, context, new { Disposition = disposition.ToString() }, cancellationToken);
				await RecordDepartmentAuditAsync(request, AuditLogTypes.ModerationRequestCompleted,
					completedByUserId, context, new { Disposition = disposition.ToString(), adminNote }, cancellationToken);

				var actions = (await _moderationActionRepository.GetByRequestAsync(request.ModerationRequestId))?.ToList()
					?? new List<ModerationAction>();
				await ApplyViewerScopeAsync(request, reports, actions, completedByUserId);

				cancellationToken.ThrowIfCancellationRequested();
				if (useTransaction)
					_unitOfWork.CommitChanges();
			}
			catch
			{
				if (useTransaction)
					_unitOfWork.DiscardChanges();

				throw;
			}

			if (!await _outboundQueueProvider.EnqueueNotification(new NotificationItem
			{
				DepartmentId = request.DepartmentId,
				Type = (int)EventTypes.ModerationRequestCompleted,
				Value = request.ModerationRequestId
			}))
			{
				Logging.LogError($"Unable to enqueue reporter notifications for moderation request {request.ModerationRequestId}.");
			}

			return request;
		}

		public async Task NotifyReportersAsync(string moderationRequestId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var request = await _moderationRequestRepository.GetByIdAsync(moderationRequestId);
			if (request == null || request.Status != (int)ModerationRequestStatus.Completed)
				return;

			var reports = (await _moderationReportRepository.GetByRequestAsync(moderationRequestId))?.ToList()
				?? new List<ModerationReport>();
			await NotifyReportersAsync(request, reports, (ModerationDisposition)request.Disposition,
				request.AdminNote, cancellationToken);
		}

		public async Task<bool> RecordEvidenceAccessAsync(string moderationRequestId, int departmentId,
			string viewedByUserId, ChatModerationContext context = null,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var request = await _moderationRequestRepository.GetByIdAsync(moderationRequestId);
			if (request == null || request.DepartmentId != departmentId)
				return false;

			var reports = (await _moderationReportRepository.GetByRequestAsync(moderationRequestId))?.ToList()
				?? new List<ModerationReport>();
			if (!await CanViewRequestAsync(request, reports, viewedByUserId, requireAdmin: true))
				throw new UnauthorizedAccessException(ModerationResources.GetCurrent("EvidenceOutsideScope"));

			await RecordActionAsync(request, ModerationActionType.EvidenceDownloaded, viewedByUserId,
				ModerationResources.GetCurrent("EvidenceDownloadedAudit"), request.Status, request.Status, context, null,
				cancellationToken);
			await RecordDepartmentAuditAsync(request, AuditLogTypes.ModerationEvidenceDownloaded,
				viewedByUserId, context, new { request.OriginalFileName, request.OriginalContentType },
				cancellationToken);
			return true;
		}

		private async Task<ModerationEvidence> LoadEvidenceAsync(int departmentId, string reporterUserId,
			ModerationItemType itemType, string itemId)
		{
			switch (itemType)
			{
				case ModerationItemType.ChatMessage:
				{
					var message = await _chatMessageRepository.GetByIdAsync(itemId);
					if (message == null || message.DepartmentId != departmentId || message.DeletedOn.HasValue)
						throw new InvalidOperationException(ModerationResources.GetCurrent("ChatMessageUnavailable"));

					var channel = await _chatChannelService.GetChannelByIdAsync(message.ChatChannelId);
					if (channel == null || channel.DepartmentId != departmentId ||
						!await _chatPermissionService.CanAccessChannelAsync(channel, reporterUserId, null))
						throw new UnauthorizedAccessException();

					ChatAttachment attachment = null;
					var attachmentMetadata = await _chatAttachmentRepository.GetMetadataByMessageIdsAsync(new[] { itemId });
					var firstAttachment = attachmentMetadata?.FirstOrDefault();
					if (firstAttachment != null)
						attachment = await _chatAttachmentRepository.GetByIdAsync(firstAttachment.ChatAttachmentId);

					return new ModerationEvidence
					{
						ChatChannelId = message.ChatChannelId,
						ContentAuthorUserId = message.SenderUserId,
						ContentAuthorUnitId = message.SenderUnitId,
						ContentCreatedOn = message.SentOn,
						Text = message.Body,
						FileName = attachment?.FileName,
						ContentType = attachment?.ContentType,
						Content = attachment?.Data,
						MetadataJson = JsonConvert.SerializeObject(new
						{
							message.MessageType,
							message.Priority,
							message.SenderDisplayName,
							message.MetadataJson,
							Attachment = attachment == null ? null : new
							{
								attachment.ChatAttachmentId,
								attachment.FileName,
								attachment.ContentType,
								attachment.Size,
								attachment.Sha256
							}
						})
					};
				}

				case ModerationItemType.Message:
				{
					if (!int.TryParse(itemId, out var messageId) ||
						!await _authorizationService.CanUserViewMessageAsync(reporterUserId, messageId))
						throw new UnauthorizedAccessException();

					var message = await _messageService.GetMessageByIdAsync(messageId);
					if (message == null)
						throw new InvalidOperationException(ModerationResources.GetCurrent("MessageUnavailable"));

					return new ModerationEvidence
					{
						ContentAuthorUserId = message.SendingUserId,
						ContentCreatedOn = message.SentOn,
						Subject = message.Subject,
						Text = message.Body,
						MetadataJson = JsonConvert.SerializeObject(new
						{
							message.Type,
							message.IsBroadcast,
							message.SystemGenerated,
							message.ReceivingUserId,
							Recipients = message.GetRecipients()
						})
					};
				}

				case ModerationItemType.CallNote:
				{
					if (!int.TryParse(itemId, out var callNoteId))
						throw new ArgumentException(ModerationResources.GetCurrent("InvalidCallNoteId"));

					var note = await _callNotesRepository.GetByIdAsync(callNoteId);
					var call = note == null ? null : await _callsService.GetCallByIdAsync(note.CallId, false);
					if (note == null || call == null || call.DepartmentId != departmentId || note.IsDeleted)
						throw new InvalidOperationException(ModerationResources.GetCurrent("CallNoteUnavailable"));
					if (!await _authorizationService.CanUserViewCallAsync(reporterUserId, note.CallId))
						throw new UnauthorizedAccessException();

					return new ModerationEvidence
					{
						CallId = note.CallId,
						ContentAuthorUserId = note.UserId,
						ContentCreatedOn = note.Timestamp,
						Text = note.Note,
						MetadataJson = JsonConvert.SerializeObject(new
						{
							note.Source,
							note.Latitude,
							note.Longitude
						})
					};
				}

				case ModerationItemType.CallImage:
				{
					if (!int.TryParse(itemId, out var callAttachmentId))
						throw new ArgumentException(ModerationResources.GetCurrent("InvalidCallImageId"));

					var attachment = await _callAttachmentRepository.GetByIdAsync(callAttachmentId);
					var call = attachment == null ? null : await _callsService.GetCallByIdAsync(attachment.CallId, false);
					if (attachment == null || call == null || call.DepartmentId != departmentId || attachment.IsDeleted ||
						attachment.CallAttachmentType != (int)CallAttachmentTypes.Image)
						throw new InvalidOperationException(ModerationResources.GetCurrent("CallImageUnavailable"));
					if (!await _authorizationService.CanUserViewCallAsync(reporterUserId, attachment.CallId))
						throw new UnauthorizedAccessException();

					return new ModerationEvidence
					{
						CallId = attachment.CallId,
						ContentAuthorUserId = attachment.UserId,
						ContentCreatedOn = attachment.Timestamp,
						FileName = attachment.FileName,
						ContentType = "image/jpeg",
						Content = attachment.Data,
						MetadataJson = JsonConvert.SerializeObject(new
						{
							attachment.Name,
							attachment.Size,
							attachment.Latitude,
							attachment.Longitude
						})
					};
				}

				default:
					throw new ArgumentOutOfRangeException(nameof(itemType));
			}
		}

		private async Task<bool> RemoveLiveContentAsync(ModerationRequest request, string byUserId,
			CancellationToken cancellationToken)
		{
			switch ((ModerationItemType)request.ItemType)
			{
				case ModerationItemType.ChatMessage:
					return await _chatMessageService.DeleteMessageAsync(request.ItemId, byUserId, true,
						ModeratedChatMessage, cancellationToken);

				case ModerationItemType.Message:
					if (!int.TryParse(request.ItemId, out var messageId))
						return false;
					var message = await _messageService.GetMessageByIdAsync(messageId);
					if (message == null)
						return false;
					message.Subject = ModeratedMessageSubject;
					message.Body = ModeratedMessageBody;
					return await _messageService.SaveMessageAsync(message, cancellationToken) != null;

				case ModerationItemType.CallNote:
					if (!int.TryParse(request.ItemId, out var callNoteId))
						return false;
					var note = await _callNotesRepository.GetByIdAsync(callNoteId);
					if (note == null)
						return false;
					note.Note = ModeratedCallNote;
					note.IsDeleted = true;
					note.DeletedByUserId = byUserId;
					note.DeletedOn = DateTime.UtcNow;
					note.IsFlagged = false;
					note.FlaggedReason = null;
					note.FlaggedByUserId = null;
					note.FlaggedOn = null;
					return await _callNotesRepository.SaveOrUpdateAsync(note, cancellationToken) != null;

				case ModerationItemType.CallImage:
					if (!int.TryParse(request.ItemId, out var callAttachmentId))
						return false;
					var attachment = await _callAttachmentRepository.GetByIdAsync(callAttachmentId);
					if (attachment == null)
						return false;
					attachment.Data = null;
					attachment.Size = 0;
					attachment.IsDeleted = true;
					attachment.DeletedByUserId = byUserId;
					attachment.DeletedOn = DateTime.UtcNow;
					attachment.IsFlagged = false;
					attachment.FlaggedReason = null;
					attachment.FlaggedByUserId = null;
					attachment.FlaggedOn = null;
					return await _callAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken) != null;

				default:
					return false;
			}
		}

		private async Task<List<ModerationRequest>> HydrateAsync(IEnumerable<ModerationRequest> requests,
			List<int> visibleGroupIds, string viewerUserId)
		{
			var result = requests?.ToList() ?? new List<ModerationRequest>();
			if (result.Count == 0)
				return result;

			var requestIds = result.Select(x => x.ModerationRequestId).Distinct().ToList();
			var reportsByRequest = (await _moderationReportRepository.GetByRequestIdsAsync(requestIds) ??
				Enumerable.Empty<ModerationReport>()).ToLookup(x => x.ModerationRequestId);
			var actionsByRequest = (await _moderationActionRepository.GetByRequestIdsAsync(requestIds) ??
				Enumerable.Empty<ModerationAction>()).ToLookup(x => x.ModerationRequestId);

			foreach (var request in result)
			{
				var reports = reportsByRequest[request.ModerationRequestId].ToList();
				var actions = actionsByRequest[request.ModerationRequestId].ToList();

				if (visibleGroupIds == null)
				{
					request.Reports = reports;
					request.Actions = actions;
				}
				else
				{
					ApplyGroupScope(request, reports, actions, visibleGroupIds, viewerUserId);
				}
			}

			return result;
		}

		private async Task ApplyViewerScopeAsync(ModerationRequest request, List<ModerationReport> reports,
			List<ModerationAction> actions, string viewerUserId)
		{
			if (await _authorizationService.CanUserModifyDepartmentAsync(viewerUserId, request.DepartmentId))
			{
				request.Reports = reports;
				request.Actions = actions;
				return;
			}

			ApplyGroupScope(request, reports, actions,
				await GetAdminGroupIdsAsync(request.DepartmentId, viewerUserId), viewerUserId);
		}

		private static void ApplyGroupScope(ModerationRequest request, List<ModerationReport> reports,
			List<ModerationAction> actions, IEnumerable<int> visibleGroupIds, string viewerUserId)
		{
			var groups = visibleGroupIds?.ToHashSet() ?? new HashSet<int>();
			var visibleReports = reports.Where(x =>
				string.Equals(x.ReportedByUserId, viewerUserId, StringComparison.OrdinalIgnoreCase) ||
				(x.ReporterGroupId.HasValue && groups.Contains(x.ReporterGroupId.Value))).ToList();
			var visibleReporters = visibleReports.Select(x => x.ReportedByUserId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			request.Reports = visibleReports;
			request.Actions = actions.Where(x =>
				x.ActionType == (int)ModerationActionType.CompletedNoAction ||
				x.ActionType == (int)ModerationActionType.ContentRemoved ||
				x.ActionType == (int)ModerationActionType.EvidenceDownloaded ||
				visibleReporters.Contains(x.PerformedByUserId)).ToList();
		}

		private async Task<bool> CanViewRequestAsync(ModerationRequest request, List<ModerationReport> reports,
			string userId, bool requireAdmin)
		{
			if (await _authorizationService.CanUserModifyDepartmentAsync(userId, request.DepartmentId))
				return true;

			var groupIds = await GetAdminGroupIdsAsync(request.DepartmentId, userId);
			if (groupIds.Count > 0 && reports.Any(x => x.ReporterGroupId.HasValue && groupIds.Contains(x.ReporterGroupId.Value)))
				return true;

			return !requireAdmin && reports.Any(x => string.Equals(x.ReportedByUserId, userId, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<List<int>> GetAdminGroupIdsAsync(int departmentId, string userId)
		{
			var admins = await _departmentGroupsService.GetAllGroupAdminsByDepartmentIdAsync(departmentId);
			return admins?
				.Where(x => x.IsAdmin == true && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.Select(x => x.DepartmentGroupId)
				.Distinct()
				.ToList() ?? new List<int>();
		}

		private async Task RecordActionAsync(ModerationRequest request, ModerationActionType actionType,
			string byUserId, string note, int? previousStatus, int? newStatus, ChatModerationContext context,
			object details, CancellationToken cancellationToken, bool includeEvidence = false)
		{
			await _moderationActionRepository.InsertAsync(new ModerationAction
			{
				ModerationActionId = Guid.NewGuid().ToString(),
				ModerationRequestId = request.ModerationRequestId,
				DepartmentId = request.DepartmentId,
				ActionType = (int)actionType,
				PerformedByUserId = byUserId,
				PerformedOn = DateTime.UtcNow,
				Note = note,
				PreviousStatus = previousStatus,
				NewStatus = newStatus,
				ActorRole = context?.ActorRole,
				IpAddress = context?.IpAddress,
				UserAgent = context?.UserAgent,
				TraceId = context?.TraceId,
				ServerName = Environment.MachineName,
				DetailsJson = details == null ? null : JsonConvert.SerializeObject(details),
				EvidenceText = includeEvidence ? request.OriginalText : null,
				EvidenceContent = includeEvidence ? request.OriginalContent : null,
				EvidenceMetadataJson = includeEvidence ? request.OriginalMetadataJson : null
			}, cancellationToken);
		}

		private async Task RecordDepartmentAuditAsync(ModerationRequest request, AuditLogTypes logType,
			string byUserId, ChatModerationContext context, object details, CancellationToken cancellationToken)
		{
			await _auditService.SaveAuditLogAsync(new AuditLog
			{
				LogType = (int)logType,
				DepartmentId = request.DepartmentId,
				UserId = byUserId,
				Message = _auditService.GetAuditLogTypeString(logType),
				Data = JsonConvert.SerializeObject(new
				{
					result = "Success",
					request.ModerationRequestId,
					request.ItemType,
					request.ItemId,
					actorRole = context?.ActorRole,
					traceId = context?.TraceId,
					details
				}),
				LoggedOn = DateTime.UtcNow,
				ObjectId = request.ModerationRequestId,
				ObjectDepartmentId = request.DepartmentId,
				IpAddress = context?.IpAddress,
				UserAgent = context?.UserAgent,
				ServerName = Environment.MachineName
			}, cancellationToken);
		}

		private async Task NotifyReportersAsync(ModerationRequest request, IEnumerable<ModerationReport> reports,
			ModerationDisposition disposition, string adminNote, CancellationToken cancellationToken)
		{
			var recipients = reports
				.Select(x => x.ReportedByUserId)
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.Where(x => !string.Equals(x, request.ContentAuthorUserId, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			foreach (var recipient in recipients)
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(recipient);
				var culture = string.IsNullOrWhiteSpace(profile?.Language) ? "en" : profile.Language;
				var action = ModerationResources.Get(
					disposition == ModerationDisposition.ContentRemoved
						? "CompletionContentRemoved"
						: "CompletionNoContentRemoved", culture);
				var note = string.IsNullOrWhiteSpace(adminNote)
					? string.Empty
					: ModerationResources.Get("CompletionAdminNoteHtml", culture,
						System.Net.WebUtility.HtmlEncode(adminNote));
				var itemType = ModerationResources.Get(GetItemTypeResourceKey((ModerationItemType)request.ItemType), culture);
				var message = await _messageService.SaveMessageAsync(new Message
				{
					Subject = ModerationResources.Get("CompletionSubject", culture),
					Body = ModerationResources.Get("CompletionBody", culture, itemType, request.ItemId, action, note),
					ReceivingUserId = recipient,
					SystemGenerated = true,
					SentOn = DateTime.UtcNow
				}, cancellationToken);
				if (message == null)
					continue;

				await _messageService.SendMessageAsync(message,
					ModerationResources.Get("SystemSenderName", culture), request.DepartmentId, false, cancellationToken);
			}
		}

		private static string GetItemTypeResourceKey(ModerationItemType itemType)
		{
			switch (itemType)
			{
				case ModerationItemType.ChatMessage:
					return "ItemTypeChatMessage";
				case ModerationItemType.Message:
					return "ItemTypeMessage";
				case ModerationItemType.CallNote:
					return "ItemTypeCallNote";
				case ModerationItemType.CallImage:
					return "ItemTypeCallImage";
				default:
					return "UnknownContentType";
			}
		}

		private class ModerationEvidence
		{
			public int? CallId { get; set; }
			public string ChatChannelId { get; set; }
			public string ContentAuthorUserId { get; set; }
			public int? ContentAuthorUnitId { get; set; }
			public DateTime? ContentCreatedOn { get; set; }
			public string Subject { get; set; }
			public string Text { get; set; }
			public string FileName { get; set; }
			public string ContentType { get; set; }
			public byte[] Content { get; set; }
			public string MetadataJson { get; set; }
		}
	}
}

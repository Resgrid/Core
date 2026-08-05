using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ModerationServiceTests
	{
		private Mock<IModerationRequestRepository> _requests;
		private Mock<IModerationReportRepository> _reports;
		private Mock<IModerationActionRepository> _actions;
		private Mock<IChatMessageRepository> _chatMessages;
		private Mock<IChatAttachmentRepository> _chatAttachments;
		private Mock<IChatChannelService> _chatChannels;
		private Mock<IChatPermissionService> _chatPermissions;
		private Mock<IChatMessageService> _chatMessageService;
		private Mock<IMessageService> _messages;
		private Mock<ICallNotesRepository> _callNotes;
		private Mock<ICallAttachmentRepository> _callAttachments;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IAuthorizationService> _authorization;
		private Mock<IAuditService> _audit;
		private Mock<IUserProfileService> _userProfiles;

		[SetUp]
		public void SetUp()
		{
			_requests = new Mock<IModerationRequestRepository>();
			_reports = new Mock<IModerationReportRepository>();
			_actions = new Mock<IModerationActionRepository>();
			_chatMessages = new Mock<IChatMessageRepository>();
			_chatAttachments = new Mock<IChatAttachmentRepository>();
			_chatChannels = new Mock<IChatChannelService>();
			_chatPermissions = new Mock<IChatPermissionService>();
			_chatMessageService = new Mock<IChatMessageService>();
			_messages = new Mock<IMessageService>();
			_callNotes = new Mock<ICallNotesRepository>();
			_callAttachments = new Mock<ICallAttachmentRepository>();
			_calls = new Mock<ICallsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_authorization = new Mock<IAuthorizationService>();
			_audit = new Mock<IAuditService>();
			_userProfiles = new Mock<IUserProfileService>();

			_actions.Setup(x => x.GetByRequestAsync(It.IsAny<string>()))
				.ReturnsAsync(new List<ModerationAction>());
			_actions.Setup(x => x.InsertAsync(It.IsAny<ModerationAction>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ModerationAction value, CancellationToken _, bool _) => value);
			_audit.Setup(x => x.SaveAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((AuditLog value, CancellationToken _) => value);
			_userProfiles.Setup(x => x.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
				.ReturnsAsync((string userId, bool _) => new UserProfile { UserId = userId, Language = "en" });
		}

		[Test]
		public async Task MultipleReportersShareOneRequestAndOnlyFirstActionStoresEvidence()
		{
			ModerationRequest storedRequest = null;
			var storedReports = new List<ModerationReport>();
			var storedActions = new List<ModerationAction>();
			var sourceMessage = new Message
			{
				MessageId = 42,
				SendingUserId = "author",
				Subject = "Original subject",
				Body = "Original body",
				SentOn = DateTime.UtcNow
			};

			_authorization.Setup(x => x.CanUserViewMessageAsync(It.IsAny<string>(), 42)).ReturnsAsync(true);
			_messages.Setup(x => x.GetMessageByIdAsync(42)).ReturnsAsync(sourceMessage);
			_requests.Setup(x => x.GetByItemAsync(7, (int)ModerationItemType.Message, "42"))
				.ReturnsAsync(() => storedRequest);
			_requests.Setup(x => x.InsertAsync(It.IsAny<ModerationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ModerationRequest value, CancellationToken _, bool _) => storedRequest = value);
			_requests.Setup(x => x.UpdateAsync(It.IsAny<ModerationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ModerationRequest value, CancellationToken _, bool _) => value);
			_reports.Setup(x => x.GetByRequestAndReporterAsync(It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((string requestId, string userId) => storedReports.FirstOrDefault(x =>
					x.ModerationRequestId == requestId && x.ReportedByUserId == userId));
			_reports.Setup(x => x.InsertAsync(It.IsAny<ModerationReport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ModerationReport value, CancellationToken _, bool _) =>
				{
					storedReports.Add(value);
					return value;
				});
			_groups.Setup(x => x.GetGroupMemberForUserAsync(It.IsAny<string>(), 7))
				.ReturnsAsync((string userId, int _) => new DepartmentGroupMember
				{
					DepartmentId = 7,
					DepartmentGroupId = userId == "reporter-a" ? 10 : 20,
					UserId = userId
				});
			_actions.Setup(x => x.InsertAsync(It.IsAny<ModerationAction>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ModerationAction value, CancellationToken _, bool _) =>
				{
					storedActions.Add(value);
					return value;
				});

			var service = CreateService();
			var first = await service.FlagAsync(7, "reporter-a", ModerationItemType.Message, "42",
				ModerationReason.Harassment, "First report");
			var second = await service.FlagAsync(7, "reporter-b", ModerationItemType.Message, "42",
				ModerationReason.Spam, "Second report");

			first.ModerationRequestId.Should().Be(second.ModerationRequestId);
			storedReports.Should().HaveCount(2);
			storedRequest.OriginalSubject.Should().Be("Original subject");
			storedRequest.OriginalText.Should().Be("Original body");
			storedActions.Should().HaveCount(2);
			storedActions.Count(x => x.EvidenceText == "Original body").Should().Be(1);
			_requests.Verify(x => x.InsertAsync(It.IsAny<ModerationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task RemovingMessageRetainsEvidenceAndNotifiesReportersExceptContentAuthor()
		{
			var request = CreateRequest();
			var reports = new List<ModerationReport>
			{
				CreateReport(request, "reporter", 10),
				CreateReport(request, "author", 10)
			};
			var liveMessage = new Message { MessageId = 42, Subject = "Live subject", Body = "Live body" };
			var savedMessages = new List<Message>();

			_requests.Setup(x => x.GetByIdAsync(request.ModerationRequestId)).ReturnsAsync(request);
			_requests.Setup(x => x.UpdateAsync(It.IsAny<ModerationRequest>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((ModerationRequest value, CancellationToken _, bool _) => value);
			_reports.Setup(x => x.GetByRequestAsync(request.ModerationRequestId)).ReturnsAsync(reports);
			_authorization.Setup(x => x.CanUserModifyDepartmentAsync("department-admin", 7)).ReturnsAsync(true);
			_messages.Setup(x => x.GetMessageByIdAsync(42)).ReturnsAsync(liveMessage);
			_messages.Setup(x => x.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Message value, CancellationToken _) =>
				{
					savedMessages.Add(value);
					return value;
				});
			_messages.Setup(x => x.SendMessageAsync(It.IsAny<Message>(), "System", 7, false, It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			_userProfiles.Setup(x => x.GetProfileByUserIdAsync("reporter", It.IsAny<bool>()))
				.ReturnsAsync(new UserProfile { UserId = "reporter", Language = "es" });

			var service = CreateService();
			var completed = await service.CompleteRequestAsync(request.ModerationRequestId, 7,
				"department-admin", ModerationDisposition.ContentRemoved, "Confirmed policy violation");
			var evidenceAccessRecorded = await service.RecordEvidenceAccessAsync(request.ModerationRequestId, 7,
				"department-admin");

			liveMessage.Subject.Should().Be(ModerationService.ModeratedMessageSubject);
			liveMessage.Body.Should().Be(ModerationService.ModeratedMessageBody);
			completed.OriginalText.Should().Be("Permanent original body");
			completed.Status.Should().Be((int)ModerationRequestStatus.Completed);
			evidenceAccessRecorded.Should().BeTrue();
			savedMessages.Should().ContainSingle(x => x.ReceivingUserId == "reporter" &&
				x.Subject == "Solicitud de moderación completada" &&
				x.Body.Contains("Confirmed policy violation") &&
				x.Body.Contains("se ha completado"));
			savedMessages.Should().NotContain(x => x.ReceivingUserId == "author");
			_messages.Verify(x => x.SendMessageAsync(
				It.Is<Message>(m => m.ReceivingUserId == "reporter"), "Sistema", 7, false,
				It.IsAny<CancellationToken>()), Times.Once);
			_actions.Verify(x => x.InsertAsync(
				It.Is<ModerationAction>(a => a.ActionType == (int)ModerationActionType.EvidenceDownloaded),
				It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
		}

		[Test]
		public async Task GroupAdminSearchHidesReportsFromOtherGroupsButKeepsCompletionAudit()
		{
			var request = CreateRequest();
			var reports = new List<ModerationReport>
			{
				CreateReport(request, "group-10-user", 10),
				CreateReport(request, "group-20-user", 20)
			};
			var actions = new List<ModerationAction>
			{
				CreateAction(request, ModerationActionType.ReportSubmitted, "group-10-user"),
				CreateAction(request, ModerationActionType.ReportSubmitted, "group-20-user"),
				CreateAction(request, ModerationActionType.CompletedNoAction, "group-20-admin")
			};

			_authorization.Setup(x => x.CanUserModifyDepartmentAsync("group-10-admin", 7)).ReturnsAsync(false);
			_groups.Setup(x => x.GetAllGroupAdminsByDepartmentIdAsync(7)).ReturnsAsync(new List<DepartmentGroupMember>
			{
				new DepartmentGroupMember { DepartmentId = 7, DepartmentGroupId = 10, UserId = "group-10-admin", IsAdmin = true }
			});
			_requests.Setup(x => x.SearchAsync(7, It.IsAny<ModerationSearchCriteria>(),
				It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10 })), "group-10-admin"))
				.ReturnsAsync(new[] { request });
			_reports.Setup(x => x.GetByRequestAsync(request.ModerationRequestId)).ReturnsAsync(reports);
			_actions.Setup(x => x.GetByRequestAsync(request.ModerationRequestId)).ReturnsAsync(actions);

			var result = await CreateService().SearchRequestsAsync(7, "group-10-admin",
				new ModerationSearchCriteria { Page = 1, PageSize = 50 });

			result.Should().ContainSingle();
			result[0].Reports.Should().ContainSingle(x => x.ReportedByUserId == "group-10-user");
			result[0].Reports.Should().NotContain(x => x.ReportedByUserId == "group-20-user");
			result[0].Actions.Should().Contain(x => x.ActionType == (int)ModerationActionType.CompletedNoAction);
			result[0].Actions.Should().NotContain(x => x.PerformedByUserId == "group-20-user");
		}

		private ModerationService CreateService()
		{
			return new ModerationService(_requests.Object, _reports.Object, _actions.Object,
				_chatMessages.Object, _chatAttachments.Object, _chatChannels.Object, _chatPermissions.Object,
				_chatMessageService.Object, _messages.Object, _callNotes.Object, _callAttachments.Object,
				_calls.Object, _groups.Object, _authorization.Object, _audit.Object, _userProfiles.Object);
		}

		private static ModerationRequest CreateRequest()
		{
			return new ModerationRequest
			{
				ModerationRequestId = "request-1",
				DepartmentId = 7,
				ItemType = (int)ModerationItemType.Message,
				ItemId = "42",
				ContentAuthorUserId = "author",
				OriginalSubject = "Permanent original subject",
				OriginalText = "Permanent original body",
				Status = (int)ModerationRequestStatus.Pending,
				Disposition = (int)ModerationDisposition.None,
				CreatedOn = DateTime.UtcNow,
				ModifiedOn = DateTime.UtcNow
			};
		}

		private static ModerationReport CreateReport(ModerationRequest request, string userId, int groupId)
		{
			return new ModerationReport
			{
				ModerationReportId = Guid.NewGuid().ToString(),
				ModerationRequestId = request.ModerationRequestId,
				DepartmentId = request.DepartmentId,
				ReportedByUserId = userId,
				ReporterGroupId = groupId,
				ReportedOn = DateTime.UtcNow
			};
		}

		private static ModerationAction CreateAction(ModerationRequest request, ModerationActionType type, string userId)
		{
			return new ModerationAction
			{
				ModerationActionId = Guid.NewGuid().ToString(),
				ModerationRequestId = request.ModerationRequestId,
				DepartmentId = request.DepartmentId,
				ActionType = (int)type,
				PerformedByUserId = userId,
				PerformedOn = DateTime.UtcNow
			};
		}
	}
}

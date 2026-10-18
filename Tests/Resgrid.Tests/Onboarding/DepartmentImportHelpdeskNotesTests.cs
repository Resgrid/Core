using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Staff replies reach the request's Freshdesk ticket as private notes once the ticket exists (wizard plan section 17.5),
	/// and the department hears about staff row views in one daily digest per workspace rather than once per view (section 17.2).
	/// </summary>
	[TestFixture]
	public class DepartmentImportHelpdeskNotesTests
	{
		private const int DepartmentId = 7;
		private const string WorkspaceId = "W1";

		private Mock<IImportSupportRequestsRepository> _requests;
		private Mock<IImportMessagesRepository> _messages;
		private Mock<IImportWorkspacesRepository> _workspaces;
		private Mock<IImportEventsRepository> _events;
		private Mock<IHelpdeskProvider> _helpdesk;
		private Mock<IDepartmentImportNotificationService> _notifications;
		private List<ImportMessage> _updated;

		[SetUp]
		public void SetUp()
		{
			_requests = new Mock<IImportSupportRequestsRepository>();
			_messages = new Mock<IImportMessagesRepository>();
			_workspaces = new Mock<IImportWorkspacesRepository>();
			_events = new Mock<IImportEventsRepository>();
			_helpdesk = new Mock<IHelpdeskProvider>();
			_notifications = new Mock<IDepartmentImportNotificationService>();
			_updated = new List<ImportMessage>();

			_helpdesk.SetupGet(h => h.IsConfigured).Returns(true);
			_requests.Setup(r => r.GetHelpdeskDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportSupportRequest>());
			_messages.Setup(m => m.UpdateAsync(It.IsAny<ImportMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((ImportMessage m, CancellationToken _, bool _) => _updated.Add(m))
				.ReturnsAsync((ImportMessage m, CancellationToken _, bool _) => m);
		}

		private DepartmentImportSupportService Outbox() => new(new Mock<IImportSupportGrantsRepository>().Object, _requests.Object, _messages.Object,
			new Mock<IImportSuggestionsRepository>().Object, _workspaces.Object, new Mock<IImportIssuesRepository>().Object,
			new Mock<IDepartmentImportAccessService>().Object, new Mock<IImportTimelineService>().Object, new Mock<IImportMappingService>().Object,
			new Mock<IImportIssueService>().Object, _helpdesk.Object, new Mock<IDepartmentsService>().Object, new Mock<IUsersService>().Object,
			new Mock<IUserProfileService>().Object, new Mock<IEventAggregator>().Object);

		private DepartmentImportMaintenanceService Maintenance() => new(new Mock<IImportRunService>().Object, new Mock<IImportRunsRepository>().Object,
			new Mock<IImportArtifactsRepository>().Object, _workspaces.Object, new Mock<IImportSourcesRepository>().Object,
			new Mock<IImportSupportGrantsRepository>().Object, new Mock<IImportStagedRowsRepository>().Object, new Mock<IImportIssuesRepository>().Object,
			new Mock<IImportValueMapsRepository>().Object, new Mock<IImportColumnMapsRepository>().Object, new Mock<IImportArtifactStore>().Object,
			new Mock<IImportUploadService>().Object, new Mock<IImportRunQueue>().Object, new Mock<IImportTimelineService>().Object, _notifications.Object,
			new Mock<IDepartmentImportSupportOutbox>().Object, new Mock<IDepartmentsService>().Object, _events.Object);

		private ImportSupportRequest Request(ImportHelpdeskState state, string ticketId = "4711")
		{
			var request = new ImportSupportRequest
			{
				Id = "R1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Number = "IMP-1001", HelpdeskState = (int)state,
				HelpdeskTicketId = state == ImportHelpdeskState.Created ? ticketId : null
			};
			_requests.Setup(r => r.GetByIdAsync(DepartmentId, "R1", It.IsAny<CancellationToken>())).ReturnsAsync(request);
			return request;
		}

		private ImportMessage DueMessage(int attempts = 0)
		{
			var message = new ImportMessage
			{
				Id = "M1", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, SupportRequestId = "R1", AuthorKind = (int)ImportActorKind.ResgridStaff,
				AuthorIdentity = "support@example.test", Body = "Please re-export the roster with the badge column.",
				HelpdeskState = (int)ImportHelpdeskState.Pending, HelpdeskAttempts = attempts, CreatedOn = DateTime.UtcNow.AddMinutes(-1)
			};
			_messages.Setup(m => m.GetHelpdeskDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<ImportMessage> { message });
			return message;
		}

		// ---- the note outbox ----------------------------------------------------------------------------------------------

		[Test]
		public async Task A_staff_reply_becomes_a_private_note_on_the_requests_ticket()
		{
			Request(ImportHelpdeskState.Created);
			var message = DueMessage();
			string note = null;
			_helpdesk.Setup(h => h.AddNoteAsync("4711", It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.Callback((string _, string body, CancellationToken _) => note = body).ReturnsAsync(true);

			(await Outbox().ProcessDueAsync()).Should().Be(1);

			message.HelpdeskState.Should().Be((int)ImportHelpdeskState.Created);
			message.HelpdeskAttempts.Should().Be(1);
			message.HelpdeskNextAttemptOn.Should().BeNull();
			note.Should().Contain("support@example.test").And.EndWith("\n\nPlease re-export the roster with the badge column.");
			_updated.Should().ContainSingle();
		}

		[Test]
		public async Task A_reply_waits_without_spending_an_attempt_while_its_ticket_is_being_created()
		{
			Request(ImportHelpdeskState.Pending);
			var message = DueMessage();

			(await Outbox().ProcessDueAsync()).Should().Be(0);

			message.HelpdeskState.Should().Be((int)ImportHelpdeskState.Pending);
			message.HelpdeskAttempts.Should().Be(0);
			message.HelpdeskNextAttemptOn.Should().BeAfter(DateTime.UtcNow);
			_helpdesk.Verify(h => h.AddNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(ImportHelpdeskState.Failed)]
		[TestCase(ImportHelpdeskState.NotConfigured)]
		public async Task A_reply_on_a_request_without_a_ticket_is_not_mirrored(ImportHelpdeskState requestState)
		{
			Request(requestState);
			var message = DueMessage();

			await Outbox().ProcessDueAsync();

			message.HelpdeskState.Should().Be((int)ImportHelpdeskState.NotConfigured);
			message.HelpdeskNextAttemptOn.Should().BeNull();
			_helpdesk.Verify(h => h.AddNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_refused_note_backs_off_and_gives_up_after_ten_attempts()
		{
			Request(ImportHelpdeskState.Created);
			_helpdesk.Setup(h => h.AddNoteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			var retried = DueMessage(attempts: 2);
			await Outbox().ProcessDueAsync();
			retried.HelpdeskState.Should().Be((int)ImportHelpdeskState.Pending);
			retried.HelpdeskAttempts.Should().Be(3);
			retried.HelpdeskNextAttemptOn.Should().BeAfter(DateTime.UtcNow);

			var abandoned = DueMessage(attempts: 9);
			await Outbox().ProcessDueAsync();
			abandoned.HelpdeskState.Should().Be((int)ImportHelpdeskState.Failed);
			abandoned.HelpdeskNextAttemptOn.Should().BeNull();
		}

		[Test]
		public async Task Nothing_is_mirrored_without_a_helpdesk()
		{
			_helpdesk.SetupGet(h => h.IsConfigured).Returns(false);
			DueMessage();

			(await Outbox().ProcessDueAsync()).Should().Be(0);

			_messages.Verify(m => m.GetHelpdeskDueAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- the daily digest of staff row views ---------------------------------------------------------------------------

		private ImportWorkspace Workspace(DateTime? digestSentOn)
		{
			var workspace = new ImportWorkspace { Id = WorkspaceId, DepartmentId = DepartmentId, Name = "Spring roster", Revision = 12, StaffViewDigestSentOn = digestSentOn };
			_workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(workspace);
			_workspaces.Setup(w => w.UpdateWithRevisionAsync(workspace, 12, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			return workspace;
		}

		private void Views(params (string Staff, double HoursAgo)[] views) =>
			_events.Setup(e => e.GetByActionSinceAsync(DepartmentImportMaintenanceService.StaffValuesViewed, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(views.Select(v => new ImportEvent
				{
					DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Action = DepartmentImportMaintenanceService.StaffValuesViewed,
					ActorKind = (int)ImportActorKind.ResgridStaff, ActorIdentity = v.Staff, CreatedOn = DateTime.UtcNow.AddHours(-v.HoursAgo)
				}).ToList());

		[Test]
		public async Task Every_administrator_gets_one_digest_of_the_views_since_the_last_one()
		{
			var workspace = Workspace(DateTime.UtcNow.AddHours(-30));
			Views(("support@example.test", 40), ("support@example.test", 5), ("SUPPORT@example.test", 4), ("operator@example.test", 1));

			(await Maintenance().SendStaffViewDigestsAsync(CancellationToken.None)).Should().Be(1);

			_notifications.Verify(n => n.NotifyAdministratorsAsync(DepartmentId, WorkspaceId, "staff-views;3;2", It.IsAny<CancellationToken>()), Times.Once);
			workspace.StaffViewDigestSentOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
		}

		[Test]
		public async Task A_second_digest_waits_a_day()
		{
			Workspace(DateTime.UtcNow.AddHours(-6));
			Views(("support@example.test", 1));

			(await Maintenance().SendStaffViewDigestsAsync(CancellationToken.None)).Should().Be(0);

			_notifications.Verify(n => n.NotifyAdministratorsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			_workspaces.Verify(w => w.UpdateWithRevisionAsync(It.IsAny<ImportWorkspace>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Views_already_summarized_do_not_trigger_another_digest()
		{
			Workspace(DateTime.UtcNow.AddHours(-26));
			Views(("support@example.test", 30), ("support@example.test", 28));

			(await Maintenance().SendStaffViewDigestsAsync(CancellationToken.None)).Should().Be(0);

			_notifications.Verify(n => n.NotifyAdministratorsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_digest_another_worker_claimed_first_is_not_sent_twice()
		{
			var workspace = Workspace(null);
			_workspaces.Setup(w => w.UpdateWithRevisionAsync(workspace, 12, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Views(("support@example.test", 2));

			(await Maintenance().SendStaffViewDigestsAsync(CancellationToken.None)).Should().Be(0);

			_notifications.Verify(n => n.NotifyAdministratorsAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void The_digest_message_counts_rows_and_staff_and_links_the_timeline()
		{
			var text = DepartmentImportNotificationService.BuildSupportMessage(new ImportWorkspace { Id = WorkspaceId, Name = "Spring roster" }, "staff-views;3;2");

			text.Should().Contain("opened 3 row(s)").And.Contain("2 staff member(s)").And.Contain("Spring roster").And.Contain("/DepartmentImport/Workspace/W1");
		}

		// ---- administrator messages ---------------------------------------------------------------------------------------

		[Test]
		public async Task An_administrator_message_threads_only_onto_a_request_of_its_own_workspace()
		{
			_workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportWorkspace { Id = WorkspaceId, DepartmentId = DepartmentId });
			var request = Request(ImportHelpdeskState.Pending);
			request.WorkspaceId = "W2";

			var thrown = await FluentActions.Awaiting(() => Outbox().PostMessageAsync(DepartmentId, "ADMIN", WorkspaceId, "Any news?", "R1"))
				.Should().ThrowAsync<DepartmentImportException>();
			thrown.Which.Code.Should().Be("import_request_not_found");
			_messages.Verify(m => m.InsertAsync(It.IsAny<ImportMessage>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);

			request.WorkspaceId = WorkspaceId;
			(await Outbox().PostMessageAsync(DepartmentId, "ADMIN", WorkspaceId, "Any news?", "R1")).SupportRequestId.Should().Be("R1");
			(await Outbox().PostMessageAsync(DepartmentId, "ADMIN", WorkspaceId, "Hello", null)).SupportRequestId.Should().BeNull();
		}
	}
}

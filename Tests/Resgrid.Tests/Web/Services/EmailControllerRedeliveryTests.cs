using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using PostmarkDotNet;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.CallEmailTemplates;
using Resgrid.Tests.Mocks;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Postmark redelivers an inbound email for about 10.5 hours after any answer but a 200 (a 403 stops it) and after waiting 2
	/// minutes for one. The call row commits before its dispatch rows and broadcast, so every retry after a late failure used to
	/// create another call. With the M0271 receipt a redelivery is acknowledged, held off, or finishes the call the earlier attempt
	/// saved; and the answers a retry cannot change are 403s.
	/// </summary>
	[TestFixture]
	public class EmailControllerRedeliveryTests
	{
		private const int DepartmentId = 10;
		private const string DispatchCode = "dept10";
		private const string MessageCode = "station2msg";

		private const string Active911Body = @"CALL: Structure Fire
ADDR1: 100 OAK ST
ID: 2026-000777-FIRE
NARR: SMOKE SHOWING";

		private Department _department;
		private List<Call> _saved;
		private List<Message> _messages;
		private int _broadcastFailures;
		private int _broadcastAttempts;
		private bool _failNextSaveAfterRow;
		private bool _unreadable;
		private int _sendFailures;
		private int _sendAttempts;
		private MockInboundEmailReceiptsRepository _receipts;
		private Mock<IQueueService> _queue;
		private EmailController _controller;

		[SetUp]
		public void SetUp()
		{
			_department = new Department { DepartmentId = DepartmentId, ManagingUserId = "manager", TimeZone = "Eastern Standard Time" };
			_saved = new List<Call>();
			_messages = new List<Message>();
			_broadcastFailures = 0;
			_broadcastAttempts = 0;
			_failNextSaveAfterRow = false;
			_unreadable = false;
			_sendFailures = 0;
			_sendAttempts = 0;
			_receipts = new MockInboundEmailReceiptsRepository();

			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetDepartmentIdForDispatchEmailAsync(DispatchCode)).ReturnsAsync(DepartmentId);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllUsersForDepartmentAsync(DepartmentId, It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = "member" } });
			departments.Setup(d => d.GetDepartmentEmailSettingsAsync(DepartmentId))
				.ReturnsAsync(new DepartmentCallEmail { DepartmentId = DepartmentId, FormatType = (int)CallEmailTypes.Active911 });
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(_department);

			var factory = new CallEmailFactory();
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetLatest10ActiveCallsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Call>());
			calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>());
			calls.Setup(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
					It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()))
				.Returns((int type, CallEmail email, string manager, List<IdentityUser> users, Department department, List<Call> active, List<Unit> units, int priority,
					List<DepartmentCallPriority> priorities, List<CallType> types) => _unreadable
					? Task.FromResult<Call>(null)
					: factory.GenerateCallFromEmailText((CallEmailTypes)type, email, manager, users, department, active, units, priority, priorities, types, Mock.Of<IGeoLocationProvider>()));
			calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Call call, CancellationToken ct) =>
				{
					if (call.CallId <= 0)
					{
						call.CallId = 900 + _saved.Count;
						_saved.Add(call);
					}

					if (_failNextSaveAfterRow)
					{
						// The call row is in; a dispatch row insert after it failed.
						_failNextSaveAfterRow = false;
						throw new InvalidOperationException("dispatch row insert failed");
					}

					return call;
				});
			calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool bypass) => _saved.SingleOrDefault(c => c.CallId == id));
			calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);

			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>());
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>());

			_queue = new Mock<IQueueService>();
			_queue.Setup(q => q.EnqueueCallBroadcastAsync(It.IsAny<CallQueueItem>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((CallQueueItem item, CancellationToken ct) =>
				{
					_broadcastAttempts++;
					if (_broadcastFailures > 0)
					{
						_broadcastFailures--;
						throw new TimeoutException("bus unavailable");
					}

					return true;
				});

			var group = new DepartmentGroup { DepartmentGroupId = 5, DepartmentId = DepartmentId, MessageEmail = MessageCode };
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupByMessageEmailCodeAsync(MessageCode)).ReturnsAsync(group);
			groups.Setup(g => g.GetAllMembersForGroupAndChildGroups(group)).Returns(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "member" } });

			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.SaveMessageAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Message message, CancellationToken ct) =>
				{
					if (message.MessageId <= 0)
					{
						message.MessageId = 500 + _messages.Count;
						_messages.Add(message);
					}

					return message;
				});
			messages.Setup(m => m.GetMessageByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => _messages.SingleOrDefault(m => m.MessageId == id));
			messages.Setup(m => m.SendMessageAsync(It.IsAny<Message>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((Message message, string sender, int department, bool single, CancellationToken ct) =>
				{
					_sendAttempts++;
					if (_sendFailures > 0)
					{
						_sendFailures--;
						throw new TimeoutException("bus unavailable");
					}

					return true;
				});

			_controller = new EmailController(settings.Object, Mock.Of<INumbersService>(), Mock.Of<ILimitsService>(), calls.Object, _queue.Object,
				departments.Object, profiles.Object, Mock.Of<ITextCommandService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUserStateService>(),
				Mock.Of<ICommunicationService>(), Mock.Of<IDistributionListsService>(), Mock.Of<IUsersService>(), Mock.Of<IEmailService>(), groups.Object,
				messages.Object, Mock.Of<IFileService>(), units.Object, Mock.Of<IGeoLocationProvider>(), Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchAdminService>(),
				new InboundEmailReceiptsService(_receipts, TimeProvider.System));
		}

		private static PostmarkInboundMessage Email(string messageId, string to = null)
		{
			return new PostmarkInboundMessage
			{
				MessageID = messageId,
				Subject = "ACTIVE 9-1-1",
				TextBody = Active911Body,
				FromFull = new FromFull { Email = "cad@county.example", Name = "County CAD" },
				ToFull = new List<ToFull> { new ToFull { Email = to ?? DispatchCode + "@" + Resgrid.Config.InboundEmailConfig.DispatchDomain, Name = "Dispatch" } },
				Attachments = new List<Attachment>()
			};
		}

		private Task<int?> ReceiveAsync(PostmarkInboundMessage email) => StatusOf(_controller.Receive(email, CancellationToken.None));

		private static async Task<int?> StatusOf(Task<ActionResult> result) => ((await result) as IStatusCodeActionResult)?.StatusCode;

		[Test]
		public async Task A_redelivered_email_creates_one_call_and_pages_once()
		{
			var id = Guid.NewGuid().ToString();

			(await ReceiveAsync(Email(id))).Should().Be(201);
			(await ReceiveAsync(Email(id))).Should().Be(200, "the email was already handled");

			_saved.Should().ContainSingle();
			_broadcastAttempts.Should().Be(1);
			var receipt = _receipts.Find(id);
			receipt.State.Should().Be((int)InboundEmailReceiptStates.Completed);
			receipt.Kind.Should().Be((int)InboundEmailKinds.Dispatch);
			receipt.EntityId.Should().Be(_saved[0].CallId);
			receipt.Deliveries.Should().Be(2);
		}

		[Test]
		public async Task A_retry_after_the_broadcast_failed_pages_the_saved_call_instead_of_creating_another()
		{
			var id = Guid.NewGuid().ToString();
			_broadcastFailures = 1;

			(await ReceiveAsync(Email(id))).Should().Be(400, "the page never went out, so Postmark must try again");
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Saved);
			_receipts.Find(id).ClaimedOn.Should().BeNull("a failed attempt gives its claim back for the next delivery");

			(await ReceiveAsync(Email(id))).Should().Be(201);

			_saved.Should().ContainSingle("the retry finished the first call");
			_broadcastAttempts.Should().Be(2);
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Completed);

			(await ReceiveAsync(Email(id))).Should().Be(200);
			_broadcastAttempts.Should().Be(2);
		}

		[Test]
		public async Task A_retry_after_the_save_failed_part_way_finishes_that_call()
		{
			var id = Guid.NewGuid().ToString();
			_failNextSaveAfterRow = true;

			(await ReceiveAsync(Email(id))).Should().Be(400);
			_receipts.Find(id).EntityId.Should().Be(900, "the call row was written before the failure");
			_broadcastAttempts.Should().Be(0);

			(await ReceiveAsync(Email(id))).Should().Be(201);

			_saved.Should().ContainSingle();
			_broadcastAttempts.Should().Be(1);
		}

		[Test]
		public async Task A_delivery_while_another_attempt_holds_the_email_is_asked_to_come_back()
		{
			var id = Guid.NewGuid().ToString();
			_receipts.Seed(new InboundEmailReceipt
			{
				InboundEmailReceiptId = Guid.NewGuid().ToString(),
				ProviderMessageId = id,
				Kind = (int)InboundEmailKinds.Dispatch,
				State = (int)InboundEmailReceiptStates.Processing,
				Deliveries = 1,
				ReceivedOn = DateTime.UtcNow.AddSeconds(-30),
				ClaimedOn = DateTime.UtcNow.AddSeconds(-30)
			});

			(await ReceiveAsync(Email(id))).Should().Be(409);

			_saved.Should().BeEmpty();
			_broadcastAttempts.Should().Be(0);
		}

		[Test]
		public async Task A_saved_call_closed_before_the_retry_is_not_paged()
		{
			var id = Guid.NewGuid().ToString();
			_broadcastFailures = 1;
			await ReceiveAsync(Email(id));
			_saved[0].State = (int)CallStates.Closed;

			(await ReceiveAsync(Email(id))).Should().Be(200);

			_broadcastAttempts.Should().Be(1, "only the failed first attempt");
			_receipts.Find(id).State.Should().Be((int)InboundEmailReceiptStates.Completed);
		}

		[Test]
		public async Task An_email_the_format_cannot_read_is_refused_so_postmark_stops()
		{
			var id = Guid.NewGuid().ToString();
			_unreadable = true;

			(await ReceiveAsync(Email(id))).Should().Be(403);
			(await ReceiveAsync(Email(id))).Should().Be(200);

			_saved.Should().BeEmpty();
		}

		[Test]
		public async Task Addresses_no_department_owns_are_refused_so_postmark_stops()
		{
			(await ReceiveAsync(Email(Guid.NewGuid().ToString(), "nobody@" + Resgrid.Config.InboundEmailConfig.DispatchDomain))).Should().Be(403);
			(await ReceiveAsync(Email(Guid.NewGuid().ToString(), "someone@example.com"))).Should().Be(403);

			_receipts.Receipts.Should().BeEmpty("nothing is claimed for an address nobody owns");
		}

		[Test]
		public async Task Without_a_provider_message_id_every_delivery_is_processed_as_before()
		{
			(await ReceiveAsync(Email(null))).Should().Be(201);
			(await ReceiveAsync(Email(null))).Should().Be(201);

			_saved.Should().HaveCount(2);
			_receipts.Receipts.Should().BeEmpty();
		}

		[Test]
		public async Task A_group_message_saved_but_not_sent_is_sent_once_by_the_retry()
		{
			var id = Guid.NewGuid().ToString();
			var to = MessageCode + "@" + Resgrid.Config.InboundEmailConfig.GroupMessageDomain;
			_sendFailures = 1;

			(await ReceiveAsync(Email(id, to))).Should().Be(400);
			(await ReceiveAsync(Email(id, to))).Should().Be(201);
			(await ReceiveAsync(Email(id, to))).Should().Be(200);

			_messages.Should().ContainSingle();
			_sendAttempts.Should().Be(2);
			_receipts.Find(id).Kind.Should().Be((int)InboundEmailKinds.GroupMessage);
		}
	}
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Intake;
using Resgrid.Tests.Mocks;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// D8 (cad-email-call-updates-plan.md): an email channel that accepts CAD updates makes its pending call inside the update engine's
	/// per-CAD-call lock, so two first emails for one new CAD call ID make one call, and the call gets its ledger baseline there.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class IntakeCadUpdateTests
	{
		private const int DepartmentId = 56;

		private Mock<IIntakeMessagesRepository> _messages;
		private Mock<ICallsService> _calls;
		private FakeCallEmailUpdatesRepository _ledger;
		private readonly List<Call> _saved = new List<Call>();
		private readonly List<(int State, int? CallId, string Reason)> _completions = new List<(int, int?, string)>();
		private TaskCompletionSource<bool> _firstSaveGate;
		private IntakeService _service;
		private bool _intakeEnabled;

		[SetUp]
		public void SetUp()
		{
			_intakeEnabled = IntakeConfig.Enabled;
			IntakeConfig.Enabled = true;
			_saved.Clear();
			_completions.Clear();
			_firstSaveGate = null;

			var channel = new IntakeChannel
			{
				IntakeChannelId = "ch-mail", DepartmentId = DepartmentId, Name = "CAD bridge", ChannelType = (int)IntakeChannelTypes.Email,
				Address = "cad", ProcessingMode = (int)IntakeProcessingModes.Template, TemplateFormat = (int)CallEmailTypes.Generic, IsEnabled = true, CadUpdates = true
			};
			var channels = new Mock<IIntakeChannelsRepository>();
			channels.Setup(c => c.GetByIdAsync("ch-mail")).ReturnsAsync(channel);

			_messages = new Mock<IIntakeMessagesRepository>();
			_messages.Setup(m => m.TryClaimAsync(It.IsAny<IntakeMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_messages.Setup(m => m.CompleteAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.Callback((string id, int state, int? callId, string reason, DateTime _, CancellationToken __) => { lock (_completions) _completions.Add((state, callId, reason)); })
				.ReturnsAsync(true);

			var department = new Department { DepartmentId = DepartmentId, Name = "Springfield Fire", ManagingUserId = "owner", TimeZone = "Eastern Standard Time" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(department);

			var config = new EmailCallUpdateConfig { Enabled = true, IdSource = (int)CadCallIdSources.Label, IdLabel = "Call ID:" };
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetEmailCallUpdateConfigAsync(DepartmentId, It.IsAny<int?>(), It.IsAny<bool>())).ReturnsAsync(config);

			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new List<DepartmentCallPriority>
			{
				new DepartmentCallPriority { DepartmentCallPriorityId = 21, Name = "High", Sort = 1, IsDefault = true }
			});
			_calls.Setup(c => c.GetCallTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CallType>());
			_calls.Setup(c => c.GenerateCallFromEmail(It.IsAny<int>(), It.IsAny<CallEmail>(), It.IsAny<string>(), It.IsAny<List<IdentityUser>>(), It.IsAny<Department>(),
					It.IsAny<List<Call>>(), It.IsAny<List<Unit>>(), It.IsAny<int>(), It.IsAny<List<DepartmentCallPriority>>(), It.IsAny<List<CallType>>()))
				.ReturnsAsync((int type, CallEmail email, string managing, List<IdentityUser> users, Department dept, List<Call> active, List<Unit> units, int priority,
					List<DepartmentCallPriority> priorities, List<CallType> types) => new Call { Name = email.Subject, NatureOfCall = email.Body, Priority = priority });
			_calls.Setup(c => c.SaveCallAsync(It.IsAny<Call>(), It.IsAny<CancellationToken>()))
				.Returns(async (Call call, CancellationToken ct) =>
				{
					var gate = _firstSaveGate;
					if (gate != null && call.CallId <= 0)
					{
						_firstSaveGate = null;
						await gate.Task;
					}

					lock (_saved)
					{
						if (call.CallId <= 0)
						{
							call.CallId = 700 + _saved.Count;
							_saved.Add(call);
						}
					}

					return call;
				});
			_calls.Setup(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool b) => { lock (_saved) return _saved.FirstOrDefault(c => c.CallId == id); });
			_calls.Setup(c => c.SaveCallNoteAsync(It.IsAny<CallNote>(), It.IsAny<CancellationToken>())).ReturnsAsync((CallNote note, CancellationToken ct) => note);
			_calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
					It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool a, bool b, bool c, bool d, bool e, bool f, bool g, bool h, bool i, bool j) => call);

			_ledger = new FakeCallEmailUpdatesRepository { Calls = () => { lock (_saved) return _saved.ToList(); } };
			var engine = EmailCallUpdateServices.Create(_calls.Object, _ledger, () => config, departments: departments.Object);

			_service = new IntakeService(channels.Object, _messages.Object, Mock.Of<IVoiceIntakeSessionsRepository>(), departments.Object, settings.Object, flags.Object,
				_calls.Object, Mock.Of<INumbersService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<IEventAggregator>(), Mock.Of<IQueueService>(),
				Mock.Of<ICommunicationService>(), Mock.Of<ICacheProvider>(), Mock.Of<IIntakeConversationService>(), Mock.Of<IIntakeConversationOperations>(),
				Mock.Of<IIntakeBlockedSendersRepository>(), TimeProvider.System, new Lazy<IDispatcherRoutingService>(() => Mock.Of<IDispatcherRoutingService>()),
				new Lazy<IEmailCallUpdateService>(() => engine));
		}

		[TearDown]
		public void TearDown()
		{
			IntakeConfig.Enabled = _intakeEnabled;
		}

		private static IntakeEnvelope CadEmail(string id, string body) => new IntakeEnvelope
		{
			ChannelType = IntakeChannelTypes.Email, IntakeChannelId = "ch-mail", ProviderMessageId = id, Sender = "cad@county.example",
			Subject = "Dispatch", Body = body, ReceivedOnUtc = DateTime.UtcNow
		};

		[Test]
		public async Task A_first_cad_email_makes_a_pending_call_that_carries_the_id_and_its_baseline()
		{
			var result = await _service.ReceiveAsync(CadEmail("m-1", "Call ID: 26-100\nStructure fire at 12 Oak St"));

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_saved.Should().ContainSingle();
			_saved[0].State.Should().Be((int)CallStates.Pending);
			_saved[0].ExternalIdentifier.Should().Be("26-100");
			_ledger.Rows.Should().ContainSingle(r => r.CallId == _saved[0].CallId && r.CadCallId == "26-100" && r.Outcome == (int)CallEmailUpdateOutcomes.Created,
				"the engine records the baseline once, while it still holds the lock");
		}

		[Test]
		public async Task A_follow_up_for_the_same_id_updates_the_pending_call()
		{
			await _service.ReceiveAsync(CadEmail("m-1", "Call ID: 26-101\nStructure fire at 12 Oak St"));

			var result = await _service.ReceiveAsync(CadEmail("m-2", "Call ID: 26-101\nStructure fire at 14 Oak St"));

			result.State.Should().Be(IntakeMessageStates.UpdatedCall);
			result.CallId.Should().Be(_saved[0].CallId);
			_saved.Should().ContainSingle();
		}

		[Test]
		public async Task Two_first_emails_for_one_new_id_arriving_together_make_one_call()
		{
			_firstSaveGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var gate = _firstSaveGate;

			var first = Task.Run(() => _service.ReceiveAsync(CadEmail("m-1", "Call ID: 26-102\nStructure fire at 12 Oak St")));
			// The first email is saving its call while it holds the CAD call ID's lock.
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (_firstSaveGate != null && DateTime.UtcNow < deadline)
				await Task.Delay(10);
			(_firstSaveGate == null).Should().BeTrue("the first email reached its save");

			var second = Task.Run(() => _service.ReceiveAsync(CadEmail("m-2", "Call ID: 26-102\nStructure fire at 12 Oak St, second page")));
			await Task.Delay(200);
			second.IsCompleted.Should().BeFalse("the second email waits for the lock instead of making its own call");

			gate.SetResult(true);
			var results = await Task.WhenAll(first, second);

			_saved.Should().ContainSingle("both emails are one incident");
			results.Select(r => r.State).Should().BeEquivalentTo(new[] { IntakeMessageStates.CallCreated, IntakeMessageStates.UpdatedCall });
			results.Single(r => r.State == IntakeMessageStates.UpdatedCall).CallId.Should().Be(_saved[0].CallId);
		}

		[Test]
		public async Task An_email_without_a_cad_id_still_makes_its_pending_call()
		{
			var result = await _service.ReceiveAsync(CadEmail("m-1", "Structure fire at 12 Oak St"));

			result.State.Should().Be(IntakeMessageStates.CallCreated);
			_saved.Should().ContainSingle();
			_ledger.Rows.Should().BeEmpty("no CAD call ID, no ledger row");
		}
	}
}

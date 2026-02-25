using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Tests.Helpers;
using Scriban.Runtime;
using Newtonsoft.Json;

namespace Resgrid.Tests.Services
{
	namespace WorkflowTemplateContextBuilderTests
	{
		public class with_the_context_builder : TestBase
		{
			protected WorkflowTemplateContextBuilder Sut;
			protected Mock<IDepartmentsService> DepartmentsServiceMock;
			protected Mock<IDepartmentSettingsService> DepartmentSettingsServiceMock;
			protected Mock<IUserProfileService> UserProfileServiceMock;

			protected Department TestDepartment;
			protected UserProfile TestProfile;

			protected with_the_context_builder()
			{
				TestDepartment = WorkflowHelpers.CreateTestDepartmentWithAddress();
				TestProfile    = WorkflowHelpers.CreateTestUserProfile();

				DepartmentsServiceMock = new Mock<IDepartmentsService>();
				// Only mock the two-param overload (bypassCache is bool) — Moq cannot mock optional params
				DepartmentsServiceMock
					.Setup(s => s.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync(TestDepartment);

				DepartmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();
				DepartmentSettingsServiceMock
					.Setup(s => s.GetTextToCallNumberForDepartmentAsync(It.IsAny<int>()))
					.ReturnsAsync((string)null);

				UserProfileServiceMock = new Mock<IUserProfileService>();
				// Only mock the two-param overload (bypassCache is bool)
				UserProfileServiceMock
					.Setup(s => s.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
					.ReturnsAsync(TestProfile);

				Sut = new WorkflowTemplateContextBuilder(
					DepartmentsServiceMock.Object,
					DepartmentSettingsServiceMock.Object,
					UserProfileServiceMock.Object);
			}

			protected async Task<ScriptObject> BuildContext(WorkflowTriggerEventType eventType, object payload)
			{
				var json   = JsonConvert.SerializeObject(payload);
				var result = await Sut.BuildContextAsync(1, eventType, json, CancellationToken.None);
				return (ScriptObject)result;
			}
		}

		[TestFixture]
		public class when_building_common_department_variables : with_the_context_builder
		{
			private ScriptObject _context;

			[SetUp]
			public async Task Setup()
			{
				_context = await BuildContext(WorkflowTriggerEventType.CallAdded,
					new CallAddedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
			}

			[Test]
			public void ShouldIncludeDepartmentObject()
				=> _context["department"].Should().NotBeNull();

			[Test]
			public void ShouldIncludeTimestampObject()
				=> _context["timestamp"].Should().NotBeNull();

			[Test]
			public void ShouldNotThrowForNullDepartmentAddress()
			{
				var dept = WorkflowHelpers.CreateTestDepartmentWithAddress();
				dept.Address = null;
				DepartmentsServiceMock
					.Setup(s => s.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync(dept);

				Func<Task> act = async () => await BuildContext(WorkflowTriggerEventType.CallAdded,
					new CallAddedEvent { DepartmentId = 1 });
				act.Should().NotThrowAsync();
			}
		}

		[TestFixture]
		public class when_building_user_variables : with_the_context_builder
		{
			[Test]
			public async Task ShouldIncludeUserObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.CallAdded,
					new CallAddedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
				ctx["user"].Should().NotBeNull();
			}

			[Test]
			public void ShouldHandleNullUserProfile()
			{
				UserProfileServiceMock
					.Setup(s => s.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>()))
					.ReturnsAsync((UserProfile)null);

				Func<Task> act = async () => await BuildContext(WorkflowTriggerEventType.CallAdded,
					new CallAddedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
				act.Should().NotThrowAsync();
			}
		}

		[TestFixture]
		public class when_building_call_event_variables : with_the_context_builder
		{
			[Test]
			public async Task CallAdded_ShouldIncludeCallObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.CallAdded,
					new CallAddedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
				ctx["call"].Should().NotBeNull();
			}

			[Test]
			public void CallClosed_ShouldNotThrow()
			{
				Func<Task> act = async () => await BuildContext(WorkflowTriggerEventType.CallClosed,
					new CallClosedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
				act.Should().NotThrowAsync();
			}

			[Test]
			public void CallUpdated_ShouldNotThrow()
			{
				Func<Task> act = async () => await BuildContext(WorkflowTriggerEventType.CallUpdated,
					new CallUpdatedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
				act.Should().NotThrowAsync();
			}
		}

		[TestFixture]
		public class when_building_note_variables : with_the_context_builder
		{
			[Test]
			public async Task NoteAdded_ShouldIncludeNoteObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.NoteAdded,
					new NoteAddedEvent { DepartmentId = 1, Note = WorkflowHelpers.CreateTestNote() });
				ctx["note"].Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_building_document_variables : with_the_context_builder
		{
			[Test]
			public async Task DocumentAdded_ShouldIncludeDocumentObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.DocumentAdded,
					new DocumentAddedEvent { DepartmentId = 1, Document = WorkflowHelpers.CreateTestDocument() });
				ctx["document"].Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_building_group_variables : with_the_context_builder
		{
			[Test]
			public async Task GroupAdded_ShouldIncludeGroupObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.GroupAdded,
					new GroupAddedEvent { DepartmentId = 1, Group = WorkflowHelpers.CreateTestDepartmentGroup() });
				ctx["group"].Should().NotBeNull();
			}

			[Test]
			public async Task GroupUpdated_ShouldIncludeGroupObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.GroupUpdated,
					new GroupUpdatedEvent { DepartmentId = 1, Group = WorkflowHelpers.CreateTestDepartmentGroup() });
				ctx["group"].Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_building_training_variables : with_the_context_builder
		{
			[Test]
			public async Task TrainingAdded_ShouldIncludeTrainingObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.TrainingAdded,
					new TrainingAddedEvent { DepartmentId = 1, Training = WorkflowHelpers.CreateTestTraining() });
				ctx["training"].Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_building_certification_variables : with_the_context_builder
		{
			[Test]
			public async Task CertificationExpiring_ShouldIncludeCertificationObject()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.CertificationExpiring,
					new CertificationExpiringEvent
					{
						DepartmentId   = 1,
						Certification  = WorkflowHelpers.CreateTestCertification(),
						DaysUntilExpiry = 14
					});
				ctx["certification"].Should().NotBeNull();
			}
		}

		[TestFixture]
		public class when_building_for_all_event_types : with_the_context_builder
		{
			[Test]
			public void AllEventTypes_ShouldBuildContextWithoutException()
			{
				foreach (WorkflowTriggerEventType eventType in Enum.GetValues(typeof(WorkflowTriggerEventType)))
				{
					var payload = new { DepartmentId = 1 };
					Func<Task> act = async () => await BuildContext(eventType, payload);
					act.Should().NotThrowAsync($"event type {eventType} should not throw");
				}
			}
		}
	}
}


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
			protected Mock<IDepartmentGroupsService> DepartmentGroupsServiceMock;
			protected Mock<IPersonnelRolesService> PersonnelRolesServiceMock;
			protected Mock<IUnitsService> UnitsServiceMock;
			protected Mock<IDepartmentMemberSensitiveDataService> MemberSensitiveDataServiceMock;
			protected Mock<IDepartmentProfileMediaService> DepartmentProfileMediaServiceMock;

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
				UserProfileServiceMock
					.Setup(s => s.GetSelectedUserProfilesAsync(It.IsAny<System.Collections.Generic.List<string>>()))
					.ReturnsAsync(new System.Collections.Generic.List<UserProfile>());

				DepartmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				DepartmentGroupsServiceMock
					.Setup(s => s.GetGroupForUserAsync(It.IsAny<string>(), It.IsAny<int>()))
					.ReturnsAsync((DepartmentGroup)null);
				DepartmentGroupsServiceMock
					.Setup(s => s.GetGroupByIdAsync(It.IsAny<int>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentGroup)null);

				PersonnelRolesServiceMock = new Mock<IPersonnelRolesService>();
				PersonnelRolesServiceMock
					.Setup(s => s.GetRolesForDepartmentAsync(It.IsAny<int>()))
					.ReturnsAsync(new System.Collections.Generic.List<PersonnelRole>());
				PersonnelRolesServiceMock
					.Setup(s => s.GetRolesForUserAsync(It.IsAny<string>(), It.IsAny<int>()))
					.ReturnsAsync(new System.Collections.Generic.List<PersonnelRole>());
				PersonnelRolesServiceMock
					.Setup(s => s.GetRoleByIdAsync(It.IsAny<int>()))
					.ReturnsAsync((PersonnelRole)null);

				UnitsServiceMock = new Mock<IUnitsService>();
				UnitsServiceMock
					.Setup(s => s.GetUnitByIdAsync(It.IsAny<int>()))
					.ReturnsAsync((Unit)null);

				// Department-scoped member data: no rows by default, so identification numbers render
				// empty rather than falling back to the global profile column.
				MemberSensitiveDataServiceMock = new Mock<IDepartmentMemberSensitiveDataService>();
				MemberSensitiveDataServiceMock
					.Setup(s => s.GetByDepartmentAndUserAsync(It.IsAny<int>(), It.IsAny<string>()))
					.ReturnsAsync((DepartmentMemberSensitiveData)null);
				MemberSensitiveDataServiceMock
					.Setup(s => s.GetResolvedForDepartmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
					.ReturnsAsync(new System.Collections.Generic.Dictionary<string, DepartmentMemberSensitiveData>());

				// No Department Profile branding by default: display_name falls back to the department name,
				// logo_url and website render empty.
				DepartmentProfileMediaServiceMock = new Mock<IDepartmentProfileMediaService>();
				DepartmentProfileMediaServiceMock
					.Setup(s => s.GetEmailBrandingAsync(It.IsAny<int>()))
					.ReturnsAsync((int id) => DepartmentEmailBranding.Disabled(id));

				Sut = new WorkflowTemplateContextBuilder(
					DepartmentsServiceMock.Object,
					DepartmentSettingsServiceMock.Object,
					UserProfileServiceMock.Object,
					DepartmentGroupsServiceMock.Object,
					PersonnelRolesServiceMock.Object,
					UnitsServiceMock.Object,
					MemberSensitiveDataServiceMock.Object,
					DepartmentProfileMediaServiceMock.Object);
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

		/// <summary>
		/// Department Profile identity for workflow email bodies (RMS plan section 4.10.1): the masthead URL obeys
		/// the same opt-in as the system emails, the name and website are plain identity.
		/// </summary>
		[TestFixture]
		public class when_building_department_branding_variables : with_the_context_builder
		{
			private async Task<ScriptObject> DepartmentVariables()
			{
				var ctx = await BuildContext(WorkflowTriggerEventType.CallAdded,
					new CallAddedEvent { DepartmentId = 1, Call = WorkflowHelpers.CreateTestCall() });
				return (ScriptObject)ctx["department"];
			}

			[Test]
			public async Task ShouldFallBackToTheDepartmentNameWithoutProfileBranding()
			{
				// One fixture instance serves every test here, so the branding mock is pinned rather than inherited.
				DepartmentProfileMediaServiceMock
					.Setup(s => s.GetEmailBrandingAsync(It.IsAny<int>()))
					.ReturnsAsync((int id) => DepartmentEmailBranding.Disabled(id));

				var dept = await DepartmentVariables();

				dept["display_name"].Should().Be(dept["name"], "no profile name means the Department row name");
				dept["logo_url"].Should().Be(string.Empty);
				dept["website"].Should().Be(string.Empty);
			}

			[Test]
			public async Task ShouldExposeTheMastheadUrlWhenEmailBrandingIsOn()
			{
				DepartmentProfileMediaServiceMock
					.Setup(s => s.GetEmailBrandingAsync(1))
					.ReturnsAsync(new DepartmentEmailBranding
					{
						DepartmentId = 1, Enabled = true, DisplayName = "Springfield Fire",
						LogoUrl = "https://app.example/User/Department/PublicMasthead?key=abc", Website = "https://www.springfieldfire.example/"
					});

				var dept = await DepartmentVariables();

				dept["display_name"].Should().Be("Springfield Fire");
				dept["logo_url"].Should().Be("https://app.example/User/Department/PublicMasthead?key=abc");
				dept["website"].Should().Be("https://www.springfieldfire.example/");
			}

			[Test]
			public async Task ShouldKeepIdentityButHideTheLogoWhenTheOptInIsOff()
			{
				DepartmentProfileMediaServiceMock
					.Setup(s => s.GetEmailBrandingAsync(1))
					.ReturnsAsync(new DepartmentEmailBranding
					{
						DepartmentId = 1, Enabled = false, DisplayName = "Springfield Fire", Website = "https://www.springfieldfire.example/"
					});

				var dept = await DepartmentVariables();

				dept["display_name"].Should().Be("Springfield Fire");
				dept["logo_url"].Should().Be(string.Empty, "a workflow author cannot leak a masthead the department never enabled");
				dept["website"].Should().Be("https://www.springfieldfire.example/");
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
		public class when_building_incident_command_variables : with_the_context_builder
		{
			[Test]
			public async Task PublicIncidentNoteAdded_ShouldExposeShareableStatusFields()
			{
				// Arrange / Act
				var ctx = await BuildContext(WorkflowTriggerEventType.PublicIncidentNoteAdded, new IncidentNoteAddedEvent
				{
					DepartmentId = 1,
					CallId = 1001,
					IncidentCommandId = "ic-1",
					IncidentNoteId = "note-1",
					Visibility = (int)IncidentContentVisibility.Public,
					NoteType = (int)IncidentNoteType.Containment,
					Title = "Containment update",
					Body = "Forward progress stopped.",
					ContainmentPercent = 40,
					CreatedByUserId = "user-1"
				});

				// Assert
				var incident = (ScriptObject)ctx["incident"];
				incident["command_id"].Should().Be("ic-1");
				incident["note_id"].Should().Be("note-1");
				incident["body"].Should().Be("Forward progress stopped.");
				incident["containment_percent"].Should().Be(40m);
				incident["visibility"].Should().Be((int)IncidentContentVisibility.Public);
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


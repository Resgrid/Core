using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Core;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Protocols
{
	/// <summary>
	/// Round 5F integration pass: translations in Unified Search (same row, same audience), the compliance matrix in the report catalog,
	/// import role changes queueing ContentAudienceChanges, v4 reading a version through the call that cites it, and the seams' registrations.
	/// </summary>
	[TestFixture]
	public class Round5FIntegrationTests
	{
		private const int Dept = 1;

		#region Translations in Unified Search

		private static ProtocolVersionTranslation Translation(string locale, string html, ProtocolTranslationStatuses status = ProtocolTranslationStatuses.Approved) =>
			new ProtocolVersionTranslation
			{
				DepartmentId = Dept, DispatchProtocolId = 5, ProtocolVersionId = 50, Locale = locale, Status = (int)status, ContentHtml = html, ContentHash = ProtocolContentHash.Compute(html)
			};

		[Test]
		public async Task An_approved_translation_joins_the_protocols_own_row_and_is_filtered_by_the_same_audience()
		{
			var translations = new Mock<IProtocolVersionTranslationsRepository>();
			var tampered = Translation("it", "<p>Sicurezza della scena</p>");
			tampered.ContentHtml = "<p>QUIETLY-EDITED</p>";
			translations.Setup(x => x.GetForVersionAsync(Dept, 50, true)).ReturnsAsync(new List<ProtocolVersionTranslation>
			{
				Translation("fr", "<p>Appel de détresse : annoncer MAYDAY trois fois</p>"),
				Translation("de", "<p>ENTWURF Notruf</p>", ProtocolTranslationStatuses.Draft),
				Translation("es", "<p>RETIRADO socorro</p>", ProtocolTranslationStatuses.Withdrawn),
				tampered
			});
			var audiences = new Mock<IProtocolAudiencesRepository>();
			audiences.Setup(x => x.GetForProtocolAsync(Dept, 5)).ReturnsAsync(new List<ProtocolAudience>
			{
				new ProtocolAudience { DepartmentId = Dept, DispatchProtocolId = 5, Kind = (int)ContentAudienceKinds.Group, RefId = "6" }
			});
			var service = new SearchProjectionService(Mock.Of<ISearchProjectionsRepository>(), Mock.Of<IDepartmentDataProtectionService>(), translations.Object,
				protocolAudiences: audiences.Object);
			var protocol = new DispatchProtocol
			{
				DispatchProtocolId = 5, DepartmentId = Dept, Name = "Mayday", Code = "SOG-204", Kind = (int)ProtocolKinds.Sog, Status = (int)ProtocolStatuses.Published,
				CurrentVersionId = 50, ProtocolText = "<p>Call MAYDAY three times</p>"
			};

			var projection = await service.BuildProtocolAsync(protocol);

			projection.EntityType.Should().Be(SearchEntityTypes.Protocol);
			projection.EntityId.Should().Be("5", "a translation never gets a row (or an audience) of its own");
			projection.SearchText.Should().Contain("Call MAYDAY three times").And.Contain("annoncer MAYDAY trois fois");
			projection.SearchText.Should().NotContain("ENTWURF").And.NotContain("RETIRADO").And.NotContain("QUIETLY-EDITED").And.NotContain("Sicurezza");
			var metadata = JsonConvert.DeserializeObject<Dictionary<string, string>>(projection.MetadataJson);
			metadata["Audience"].Should().Be("1:6", "hits on translated text are filtered by the source's audience");
			metadata["Locales"].Should().Be("fr");

			// A draft protocol (nothing in force) is never projected, translations or not.
			protocol.Status = (int)ProtocolStatuses.Draft;
			(await service.BuildProtocolAsync(protocol)).Should().BeNull();
		}

		[Test]
		public void Learning_requirement_rows_are_never_a_search_family()
		{
			// Round 5F decision: requirement status rows are personal records; they are found on the matrix and the member's page, not in search.
			SearchEntityTypes.Indexed.Should().NotContain(t => t.IndexOf("Requirement", StringComparison.OrdinalIgnoreCase) >= 0 ||
				t.IndexOf("Lms", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		#endregion Translations in Unified Search

		#region Compliance matrix report

		private static LmsComplianceMatrix Matrix()
		{
			var evoc = new LmsRequirement { LmsRequirementId = 1, DepartmentId = Dept, Name = "EVOC", Kind = (int)LmsRequirementKinds.Course };
			var hours = new LmsRequirement { LmsRequirementId = 2, DepartmentId = Dept, Name = "Hazmat hours", Kind = (int)LmsRequirementKinds.CategoryHours, RequiredHours = 8 };
			var matrix = new LmsComplianceMatrix { Requirements = { evoc, hours }, AllRequirements = { evoc, hours }, TargetNames = { [1] = "Driver course" }, AsOfUtc = DateTime.UtcNow };
			var alice = new LmsMatrixRow { UserId = "alice", Name = "Alice Able" };
			alice.Cells[1] = new LmsMatrixCell { LmsRequirementId = 1, State = LmsRequirementStates.Compliant, LastCompletedUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc) };
			alice.Cells[2] = new LmsMatrixCell { LmsRequirementId = 2, State = LmsRequirementStates.AtRisk, Counted = true, Progress = 3, Required = 8 };
			var bob = new LmsMatrixRow { UserId = "bob", Name = "Bob Baker" };
			bob.Cells[1] = new LmsMatrixCell { LmsRequirementId = 1, State = LmsRequirementStates.Overdue, DueUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc) };
			matrix.Rows.AddRange(new[] { alice, bob });
			matrix.Totals[LmsRequirementStates.Compliant] = 1;
			matrix.Totals[LmsRequirementStates.AtRisk] = 1;
			matrix.Totals[LmsRequirementStates.Overdue] = 1;
			return matrix;
		}

		[Test]
		public async Task The_compliance_matrix_is_an_lms_report_of_the_catalog_by_member_or_by_requirement()
		{
			var requirements = new Mock<ILmsRequirementService>();
			requirements.Setup(x => x.GetDepartmentMatrixAsync(Dept, It.IsAny<LmsMatrixFilter>(), It.IsAny<DateTime>())).ReturnsAsync(Matrix);
			var source = new LmsComplianceMatrixReportSource(requirements.Object);
			source.Descriptor.Key.Should().Be("compliance-matrix");
			source.Descriptor.Tier.Should().Be(LmsReportTiers.Lms);

			var catalog = new LmsReportCatalogService(new ILmsReportSource[] { source }, Mock.Of<ILmsReportFoldersRepository>(), Mock.Of<ILmsReportDefinitionsRepository>(),
				Mock.Of<ILmsReportRunsRepository>(), Mock.Of<IDepartmentsService>(), Mock.Of<IAuthorizationService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<IEmailService>(), Mock.Of<IPdfProvider>(), Mock.Of<ILmsAccessService>(), Mock.Of<IEventAggregator>());
			catalog.Find("compliance-matrix").Should().BeSameAs(source.Descriptor, "saved and scheduled reports find it by its key");

			var culture = CultureInfo.GetCultureInfo("en");
			var byMember = await source.BuildAsync(new LmsReportRequest { DepartmentId = Dept, Culture = culture, UtcNow = DateTime.UtcNow, Parameters = new LmsReportParameters { GroupBy = 0 } }, k => k);
			byMember.Columns.Should().HaveCount(6);
			byMember.Rows.Should().HaveCount(3);
			byMember.Rows.Should().Contain(r => r[0] == "Bob Baker" && r[1] == "EVOC (Driver course)" && r[3] == new DateTime(2026, 10, 1).ToString("d", culture));
			byMember.Rows.Single(r => r[1] == "Hazmat hours")[5].Should().Be("3 / 8");
			byMember.Rows.Single(r => r[0] == "Bob Baker")[2].Should().Be(LmsComplianceMatrixReportSource.StateName(LmsRequirementStates.Overdue, culture));

			var byRequirement = await source.BuildAsync(new LmsReportRequest { DepartmentId = Dept, Culture = culture, UtcNow = DateTime.UtcNow, Parameters = new LmsReportParameters { GroupBy = 1 } }, k => k);
			byRequirement.Rows.Should().HaveCount(2);
			var evoc = byRequirement.Rows.Single(r => r[0].StartsWith("EVOC"));
			evoc[1].Should().Be("2");
			evoc[2 + 0].Should().Be("1", "one compliant");
			evoc[2 + 3].Should().Be("1", "one overdue");
			requirements.Verify(x => x.GetDepartmentMatrixAsync(Dept, It.IsAny<LmsMatrixFilter>(), It.IsAny<DateTime>()), Times.Exactly(2));
		}

		[Test]
		public void The_matrix_state_names_are_translated()
		{
			LmsComplianceMatrixReportSource.StateName(LmsRequirementStates.Overdue, CultureInfo.GetCultureInfo("de"))
				.Should().NotBe(LmsComplianceMatrixReportSource.StateName(LmsRequirementStates.Overdue, CultureInfo.GetCultureInfo("en")));
		}

		#endregion Compliance matrix report

		#region Import role changes

		[Test]
		public async Task An_import_role_save_queues_the_members_it_moved_for_worker_83()
		{
			var roles = new Mock<IPersonnelRolesRepository>();
			roles.Setup(r => r.SaveOrUpdateAsync(It.IsAny<PersonnelRole>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((PersonnelRole r, CancellationToken _, bool __) => r);
			var roleUsers = new Mock<IPersonnelRoleUsersRepository>();
			roleUsers.Setup(r => r.GetAllMembersOfRoleAsync(3)).ReturnsAsync(new List<PersonnelRoleUser>
			{
				new PersonnelRoleUser { PersonnelRoleId = 3, DepartmentId = Dept, UserId = "u2" }, new PersonnelRoleUser { PersonnelRoleId = 3, DepartmentId = Dept, UserId = "u3" }
			});
			var members = new Mock<IDepartmentMembersRepository>();
			members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(Dept)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = Dept, UserId = "u1" }, new DepartmentMember { DepartmentId = Dept, UserId = "u2" }
			});
			var queue = new Mock<IContentAudienceChangeQueue>();
			var search = new Mock<ISearchProjectionService>();
			var service = new PersonnelRolesService(roles.Object, roleUsers.Object, Mock.Of<ISubscriptionsService>(), members.Object, Mock.Of<IEventAggregator>(),
				Mock.Of<IUnitOfWork>(), null, new Lazy<ISearchProjectionService>(() => search.Object), null, queue.Object);
			var role = new PersonnelRole
			{
				PersonnelRoleId = 3, DepartmentId = Dept, Name = "Engineer",
				Users = new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "u1" }, new PersonnelRoleUser { UserId = "U2" } }
			};

			await service.SaveRoleAsync(role, Resgrid.Model.Onboarding.ImportEffectPolicy.Import, CancellationToken.None);

			// u1 joined, u3 left; u2 (whatever the case of the id) stayed. The search projection waits for the import's one rebuild.
			queue.Verify(q => q.EnqueueManyAsync(Dept, It.Is<IEnumerable<string>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { "u1", "u3" })), ContentAudienceChangeSources.Role,
				It.IsAny<CancellationToken>()), Times.Once);
			search.Verify(s => s.RefreshAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			PersonnelRolesService.MovedMembers(new[] { "a", "B" }, new[] { "b", "c" }).Should().BeEquivalentTo("c", "a");
		}

		#endregion Import role changes

		#region v4 GetVersion through a call

		[Test]
		public void A_version_is_read_through_a_call_only_when_the_call_cites_exactly_it()
		{
			var legacy = new ProtocolVersion { ProtocolVersionId = 77, DispatchProtocolId = 5, DepartmentId = Dept, Status = (int)ProtocolVersionStatuses.Legacy };
			ProtocolAccessRules.IsCitedByCall(legacy, new[] { new CallProtocol { CallId = 40, DispatchProtocolId = 5, ProtocolVersionId = 77 } }).Should().BeTrue();
			ProtocolAccessRules.IsCitedByCall(legacy, new[] { new CallProtocol { CallId = 40, DispatchProtocolId = 5, ProtocolVersionId = 77, RemovedOn = DateTime.UtcNow } }).Should().BeFalse("a removed row cites nothing");
			ProtocolAccessRules.IsCitedByCall(legacy, new[] { new CallProtocol { CallId = 40, DispatchProtocolId = 5, ProtocolVersionId = 78 } }).Should().BeFalse("another version");
			ProtocolAccessRules.IsCitedByCall(legacy, new[] { new CallProtocol { CallId = 40, DispatchProtocolId = 6, ProtocolVersionId = 77 } }).Should().BeFalse("another protocol");
			ProtocolAccessRules.IsCitedByCall(legacy, null).Should().BeFalse();

			// Without the call, a member never reads a legacy version from the library.
			var reader = new ProtocolReader { DepartmentId = Dept, UserId = "m", IsCurrentMember = true };
			var protocol = new DispatchProtocol { DispatchProtocolId = 5, DepartmentId = Dept, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 80 };
			ProtocolAccessRules.CanReadVersion(reader, protocol, new List<IContentAudienceRule>(), legacy).Should().BeFalse();
		}

		#endregion v4 GetVersion through a call

		#region Registrations

		[Test]
		public void The_round_5F_seams_are_registered_in_the_container()
		{
			var registry = Bootstrapper.GetKernel().ComponentRegistry;
			IEnumerable<Type> Implementations(Type service) =>
				registry.RegistrationsFor(new TypedService(service)).Select(r => r.Activator.LimitType);

			Implementations(typeof(IProtocolLearningExtensions)).Should().Contain(typeof(LmsProtocolLearningService));
			Implementations(typeof(ILmsReportSource)).Should().Contain(typeof(LmsComplianceMatrixReportSource));
			Implementations(typeof(IGdprExportSection)).Should().Contain(typeof(LmsGdprExportSection));
			foreach (var repository in new[] { typeof(ILmsProtocolLearningsRepository), typeof(ILmsProtocolQuizAttemptsRepository), typeof(ILmsMicroLearningRepository) })
				registry.IsRegistered(new TypedService(repository)).Should().BeTrue(repository.Name);
		}

		#endregion Registrations
	}
}

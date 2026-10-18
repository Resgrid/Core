using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Identity;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Services.Onboarding;
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// The commit engine's building blocks (wizard plan sections 11 and 12): writer composition, the shared writer
	/// planning and journaling rules, the personnel account rules, plan capacity and kept custom fields.
	/// </summary>
	[TestFixture]
	public class ImportEngineTests : TestBase
	{
		// ---- Composition ----------------------------------------------------------------------------------------------

		/// <summary>
		/// The shared test container plus what only the hosts register: the Identity-backed account service (next to
		/// UserManager). The member-deletion and emergency-contact graphs need repositories only DataModule registers (the test
		/// container uses TestingDataModule), so those services are mocks; this test is about the import graph.
		/// </summary>
		private static ILifetimeScope EngineScope() => Bootstrapper.GetKernel().BeginLifetimeScope(builder =>
		{
			builder.RegisterInstance(Mock.Of<IMemberAccountService>()).As<IMemberAccountService>();
			builder.RegisterInstance(Mock.Of<IDeleteService>()).As<IDeleteService>();
			builder.RegisterInstance(Mock.Of<IDepartmentMemberEmergencyContactService>()).As<IDepartmentMemberEmergencyContactService>();
		});

		[Test]
		public void Engine_services_and_one_writer_per_contract_resolve_from_the_container()
		{
			using var scope = EngineScope();

			scope.Resolve<IImportRunService>().Should().NotBeNull();
			scope.Resolve<IImportApprovalService>().Should().BeOfType<ImportApprovalService>();
			scope.Resolve<IImportPlanService>().Should().BeOfType<ImportPlanService>();
			scope.Resolve<IEnumerable<IImportRunHandler>>().Select(h => h.Kind).Should()
				.Contain(new[] { ImportRunKind.Analysis, ImportRunKind.Commit, ImportRunKind.Rollback });

			var writers = scope.Resolve<IEnumerable<IImportTargetWriter>>().Select(w => w.EntityKey).ToList();
			writers.Should().OnlyHaveUniqueItems();
			writers.Should().BeEquivalentTo(ImportCatalog.Current.Contracts.Select(c => c.Key));
		}

		[Test]
		public void Writers_that_accept_create_new_decisions_are_the_name_keyed_catalogs()
		{
			using var scope = EngineScope();
			scope.Resolve<IEnumerable<IImportTargetWriter>>().OfType<IImportImplicitCreator>().Select(w => w.EntityKey).Should()
				.BeEquivalentTo("roles", "groups", "certification-types", "unit-types", "call-types", "poi-types");
		}

		// ---- Shared writer rules (ImportRecordWriter) -------------------------------------------------------------------

		private sealed class Thing
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public string Description { get; set; }
			public bool Used { get; set; }
		}

		private sealed class ThingWriter : ImportRecordWriter<Thing>
		{
			public readonly List<Thing> Store = new List<Thing>();
			public readonly List<int> Removed = new List<int>();
			public bool Seats;

			private static readonly IReadOnlyList<ImportFieldBinding<Thing>> Fields = new[]
			{
				new ImportFieldBinding<Thing>("name", r => r.Name, (r, v) => r.Name = v),
				new ImportFieldBinding<Thing>("description", r => r.Description, (r, v) => r.Description = v)
			};

			public override string EntityKey => "things";
			public override ImportEffects AffectedEffects => ImportEffects.None;
			protected override IReadOnlyList<ImportFieldBinding<Thing>> Bindings => Fields;
			protected override bool UsesUnitSeat => Seats;
			protected override Task<List<Thing>> LoadAsync(int departmentId, CancellationToken cancellationToken) => Task.FromResult(Store.ToList());
			protected override string KeyOf(Thing record) => record.Id.ToString();
			protected override string NaturalKeyOf(Thing record) => record.Name;
			protected override Thing New(ImportWriteContext context, ImportCanonicalRow row) => new Thing();

			protected override Task<Thing> SaveAsync(ImportWriteContext context, Thing record, CancellationToken cancellationToken)
			{
				if (record.Id == 0)
				{
					record.Id = Store.Count == 0 ? 1 : Store.Max(s => s.Id) + 1;
					Store.Add(record);
				}
				return Task.FromResult(record);
			}

			protected override Task<bool> HasOutsideActivityAsync(ImportWriteContext context, Thing record, CancellationToken cancellationToken) => Task.FromResult(record.Used);

			protected override Task<ImportRollbackOutcome> RemoveAsync(ImportWriteContext context, Thing record, CancellationToken cancellationToken)
			{
				Store.Remove(record);
				Removed.Add(record.Id);
				return Task.FromResult(ImportRollbackOutcome.Deleted);
			}
		}

		private static ImportCanonicalRow Row(string name, string description = null, ImportRowDecision decision = ImportRowDecision.Undecided, string target = null)
		{
			var row = new ImportCanonicalRow { StagedRowId = Guid.NewGuid().ToString(), RowNumber = 2, Decision = decision, TargetKey = target };
			row.Values["name"] = name;
			if (description != null)
				row.Values["description"] = description;
			return row;
		}

		private static ImportWriteContext Context(ImportWriteMode mode = ImportWriteMode.CreateOnly) => new ImportWriteContext
		{
			DepartmentId = 7,
			WriteMode = mode,
			Authority = ImportAuthority.ForAdministrator(7, "admin")
		};

		private static (ImportJournalWriter Writer, List<ImportChangeJournalEntry> Entries) Journal()
		{
			var entries = new List<ImportChangeJournalEntry>();
			return (new ImportJournalWriter((entry, _) =>
			{
				entry.Id = Guid.NewGuid().ToString();
				entries.Add(entry);
				return Task.CompletedTask;
			}), entries);
		}

		[Test]
		public async Task Planning_creates_new_names_links_exact_unique_matches_and_refuses_ambiguous_ones()
		{
			var writer = new ThingWriter();
			writer.Store.AddRange(new[]
			{
				new Thing { Id = 1, Name = "Engine Company" },
				new Thing { Id = 2, Name = "Duplicate" },
				new Thing { Id = 3, Name = "duplicate " }
			});

			var rows = new[] { Row("Brand New"), Row("ENGINE   company"), Row("Duplicate"), Row("Skipped", decision: ImportRowDecision.Skip) };
			var plan = await writer.PlanAsync(Context(), rows, CancellationToken.None);

			plan.Rows[0].Decision.Should().Be(ImportRowDecision.Create);
			plan.Rows[1].Decision.Should().Be(ImportRowDecision.Link);
			plan.Rows[1].TargetKey.Should().Be("1");
			plan.Rows[1].Issues.Should().ContainSingle(i => i.Code == "IMP-MATCH-EXISTING" && i.Severity == (int)ImportIssueSeverity.Info);
			plan.Rows[2].Decision.Should().Be(ImportRowDecision.Skip);
			plan.Rows[2].Issues.Should().ContainSingle(i => i.Code == "IMP-MATCH-AMBIGUOUS" && i.Severity == (int)ImportIssueSeverity.Blocking);
			plan.Rows[3].Decision.Should().Be(ImportRowDecision.Skip);
			(await writer.FindExistingAsync(7, CancellationToken.None)).Keys.Should().BeEquivalentTo(ImportFieldConverter.Normalize("Engine Company"));
		}

		[Test]
		public async Task Reviewed_update_changes_only_differing_fields_and_rollback_restores_them()
		{
			var writer = new ThingWriter();
			writer.Store.Add(new Thing { Id = 1, Name = "Rescue", Description = "Old" });
			var context = Context(ImportWriteMode.ReviewedUpdate);

			var row = Row("Rescue", "New");
			var plan = await writer.PlanAsync(context, new[] { row }, CancellationToken.None);
			plan.Rows[0].Decision.Should().Be(ImportRowDecision.Update);
			plan.Rows[0].Changes.Keys.Should().BeEquivalentTo("description");

			row.Decision = ImportRowDecision.Update;
			row.TargetKey = "1";
			var (journal, entries) = Journal();
			var result = await writer.WriteRowAsync(context, row, journal, CancellationToken.None);
			result.Outcome.Should().Be(ImportRowOutcome.Updated);
			writer.Store[0].Description.Should().Be("New");
			entries.Should().ContainSingle(e => e.Operation == (int)ImportJournalOperation.Update);
			JsonConvert.DeserializeObject<Dictionary<string, string>>(entries[0].BeforeValues).Should().ContainKey("description").WhoseValue.Should().Be("Old");

			var undo = await writer.RollbackAsync(context, entries[0], CancellationToken.None);
			undo.Outcome.Should().Be(ImportRollbackOutcome.Restored);
			writer.Store[0].Description.Should().Be("Old");
		}

		[Test]
		public async Task Created_records_are_journaled_and_removed_by_rollback_unless_used_since()
		{
			var writer = new ThingWriter();
			var context = Context();
			var (journal, entries) = Journal();

			var first = await writer.WriteRowAsync(context, Row("One", decision: ImportRowDecision.Create), journal, CancellationToken.None);
			var second = await writer.WriteRowAsync(context, Row("Two", decision: ImportRowDecision.Create), journal, CancellationToken.None);
			first.Outcome.Should().Be(ImportRowOutcome.Created);
			entries.Select(e => e.Operation).Should().AllBeEquivalentTo((int)ImportJournalOperation.Create);

			writer.Store.Single(s => s.Name == "Two").Used = true;
			(await writer.RollbackAsync(context, entries[1], CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);
			(await writer.RollbackAsync(context, entries[0], CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.Deleted);
			writer.Removed.Should().Equal(int.Parse(first.TargetKey));
			second.TargetKey.Should().NotBe(first.TargetKey);
		}

		[Test]
		public async Task Records_that_use_a_plan_seat_are_never_written_when_capacity_is_unknown_or_spent()
		{
			var writer = new ThingWriter { Seats = true };
			var (journal, _) = Journal();

			var unknown = Context();
			(await writer.WriteRowAsync(unknown, Row("A", decision: ImportRowDecision.Create), journal, CancellationToken.None)).Code.Should().Be("IMP-PLAN-CAPACITY");

			var one = Context();
			one.UnitCapacity = 1;
			(await writer.WriteRowAsync(one, Row("B", decision: ImportRowDecision.Create), journal, CancellationToken.None)).Outcome.Should().Be(ImportRowOutcome.Created);
			(await writer.WriteRowAsync(one, Row("C", decision: ImportRowDecision.Create), journal, CancellationToken.None)).Code.Should().Be("IMP-PLAN-CAPACITY");
			writer.Store.Select(s => s.Name).Should().Equal("B");
		}

		[Test]
		public async Task Implicit_creation_finds_an_existing_name_before_creating()
		{
			var writer = new ThingWriter();
			writer.Store.Add(new Thing { Id = 4, Name = "Captain" });

			(await writer.EnsureAsync(Context(), " captain", CancellationToken.None)).Should().Be(("4", false));
			var created = await writer.EnsureAsync(Context(), "Lieutenant", CancellationToken.None);
			created.Created.Should().BeTrue();
			writer.Store.Should().Contain(s => s.Name == "Lieutenant" && s.Id.ToString() == created.TargetKey);
		}

		// ---- Capacity -------------------------------------------------------------------------------------------------

		[Test]
		public void Capacity_is_unknown_without_billing_data_and_shared_on_entity_plans()
		{
			new ImportCapacity().Covers(0, 0).Should().BeFalse();
			new ImportCapacity { Personnel = 5, Units = 2 }.Covers(5, 2).Should().BeTrue();
			new ImportCapacity { Personnel = 5, Units = 2 }.Covers(5, 3).Should().BeFalse();
			new ImportCapacity { Personnel = 6, Units = 6, Shared = true }.Covers(4, 3).Should().BeFalse();

			var context = new ImportWriteContext { PersonnelCapacity = 2, UnitCapacity = 2, SharedCapacity = true };
			context.UseUnitSeat();
			context.UsePersonnelSeat();
			context.HasPersonnelSeat.Should().BeFalse();
			context.HasUnitSeat.Should().BeFalse();
		}

		// ---- Personnel account rules (section 12.3) ---------------------------------------------------------------------

		private sealed class PersonnelFixture
		{
			public readonly Mock<IMemberProvisioningService> Provisioning = new Mock<IMemberProvisioningService>();
			public readonly Mock<IMemberAccountService> Accounts = new Mock<IMemberAccountService>();
			public readonly Mock<IDepartmentsService> Departments = new Mock<IDepartmentsService>();
			public readonly Mock<IDeleteService> Delete = new Mock<IDeleteService>();
			public readonly List<DepartmentMember> Members = new List<DepartmentMember>();

			public PersonnelFixture()
			{
				Departments.Setup(d => d.GetAllMembersForDepartmentIncludingDeletedAsync(7)).ReturnsAsync(() => Members);
				Departments.Setup(d => d.GetAllUsersForDepartmentUnlimitedAsync(7, true)).ReturnsAsync(new List<IdentityUser>());
			}

			public PersonnelImportWriter Writer() => new PersonnelImportWriter(Provisioning.Object, Accounts.Object, Departments.Object,
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(),
				Mock.Of<IUserDefinedFieldsService>(), Delete.Object);
		}

		private static ImportCanonicalRow Person(string email, ImportRowDecision decision = ImportRowDecision.Undecided, bool admin = false)
		{
			var row = new ImportCanonicalRow { StagedRowId = Guid.NewGuid().ToString(), RowNumber = 2, Decision = decision };
			row.Values["email"] = email;
			row.Values["first_name"] = "Pat";
			row.Values["last_name"] = "Doe";
			if (admin)
				row.Values["will_be_admin"] = "true";
			return row;
		}

		[Test]
		public async Task Personnel_planning_follows_the_global_account_rules()
		{
			var fixture = new PersonnelFixture();
			fixture.Accounts.Setup(a => a.FindByEmailAsync("member@example.org")).ReturnsAsync(new IdentityUser { Id = "U1" });
			fixture.Accounts.Setup(a => a.FindByEmailAsync("removed@example.org")).ReturnsAsync(new IdentityUser { Id = "U2" });
			fixture.Accounts.Setup(a => a.FindByEmailAsync("elsewhere@example.org")).ReturnsAsync(new IdentityUser { Id = "U3" });
			fixture.Members.Add(new DepartmentMember { DepartmentId = 7, UserId = "U1" });
			fixture.Members.Add(new DepartmentMember { DepartmentId = 7, UserId = "U2", IsDeleted = true });

			var rows = new[] { Person("new@example.org", admin: true), Person("member@example.org"), Person("removed@example.org"), Person("elsewhere@example.org") };
			var plan = await fixture.Writer().PlanAsync(Context(), rows, CancellationToken.None);

			plan.Rows.Select(r => r.Decision).Should().Equal(ImportRowDecision.Create, ImportRowDecision.Link, ImportRowDecision.Reactivate, ImportRowDecision.InviteToJoin);
			// The new person and the returning one each take a seat; the returning one is acknowledged at approval.
			plan.PersonnelSeats.Should().Be(2);
			plan.Consequences.Should().Contain(PersonnelImportWriter.ReactivatesConsequence, 1);
			plan.Rows[0].Issues.Should().Contain(i => i.Code == "IMP-PERSON-WILL-BE-ADMIN");
			plan.Rows[1].TargetKey.Should().Be("U1");
			plan.Rows[2].Issues.Should().Contain(i => i.Code == "IMP-PERSON-DELETED-MEMBER");
			// Nothing about the other department's account is revealed or linked.
			plan.Rows[3].TargetKey.Should().BeNull();
			plan.Rows[3].Issues.Should().ContainSingle(i => i.Code == "IMP-PERSON-EXISTING-ACCOUNT");
		}

		[Test]
		public async Task Imported_people_are_dormant_without_a_password_and_never_administrators_at_commit()
		{
			var fixture = new PersonnelFixture();
			MemberProvisioningRequest seen = null;
			fixture.Provisioning.Setup(p => p.ProvisionAsync(It.IsAny<MemberProvisioningRequest>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.Callback<MemberProvisioningRequest, ImportEffectPolicy, CancellationToken>((r, _, _) => seen = r)
				.ReturnsAsync(new MemberProvisioningResult { Succeeded = true, UserId = "PRE", CreatedAccount = true });

			var context = Context();
			context.PersonnelCapacity = 1;
			context.ResolveReference = (_, _, _) => Task.FromResult<string>(null);
			var row = Person("new@example.org", ImportRowDecision.Create, admin: true);
			row.TargetKey = "PRE";
			var (journal, entries) = Journal();

			var result = await fixture.Writer().WriteRowAsync(context, row, journal, CancellationToken.None);

			result.Outcome.Should().Be(ImportRowOutcome.Created);
			seen.UserId.Should().Be("PRE");
			seen.UserIdReserved.Should().BeTrue("the key is the engine's committed account claim");
			seen.Password.Should().BeNull();
			seen.EmailConfirmed.Should().BeFalse();
			seen.IsAdmin.Should().BeFalse();
			seen.ImportActivationState.Should().Be((int)ImportActivationState.Dormant);
			context.PersonnelCapacity.Should().Be(0);
			entries.Should().ContainSingle(e => e.CreatedAccount && e.TargetKey == "PRE");
		}

		[Test]
		public async Task Personnel_rollback_deactivates_only_accounts_the_import_created_for_this_department_alone()
		{
			var fixture = new PersonnelFixture();
			fixture.Departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), 7, true))
				.ReturnsAsync((string id, int _, bool _) => new DepartmentMember { DepartmentId = 7, UserId = id, ImportActivationState = id == "ACT" ? (int)ImportActivationState.Activated : (int)ImportActivationState.Dormant });
			fixture.Departments.Setup(d => d.GetAllDepartmentsForUserAsync("ALONE")).ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = 7 } });
			fixture.Departments.Setup(d => d.GetAllDepartmentsForUserAsync("SHARED")).ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = 7 }, new DepartmentMember { DepartmentId = 9 } });
			fixture.Delete.Setup(d => d.DeleteUserAsync(7, "admin", "ALONE", It.IsAny<CancellationToken>())).ReturnsAsync(DeleteUserResults.NoFailure);
			var writer = fixture.Writer();
			var context = Context();

			ImportChangeJournalEntry Created(string id) => new ImportChangeJournalEntry { Id = id, TargetKey = id, Operation = (int)ImportJournalOperation.Create, CreatedAccount = true };

			(await writer.RollbackAsync(context, Created("ALONE"), CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.Deleted);
			(await writer.RollbackAsync(context, Created("SHARED"), CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.MembershipRemoved);
			(await writer.RollbackAsync(context, Created("ACT"), CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);

			fixture.Delete.Verify(d => d.RevokeDepartmentAccessAsync("SHARED", 7, "admin", It.IsAny<CancellationToken>()), Times.Once);
			fixture.Delete.Verify(d => d.DeleteUserAsync(7, It.IsAny<string>(), "ACT", It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_replayed_row_journals_the_account_its_first_attempt_created_so_rollback_deletes_it()
		{
			// The first attempt created the account outside the chunk transaction; the chunk then rolled back (or was replayed row
			// by row), so the replay finds the account under the claimed key with no journal entry for it.
			var fixture = new PersonnelFixture();
			fixture.Accounts.Setup(a => a.FindByIdAsync("PRE")).ReturnsAsync(new IdentityUser { Id = "PRE", UserName = "new", Email = "new@example.org" });
			var users = new Mock<IUsersService>();
			users.Setup(u => u.HasUserExtInfoAsync("PRE")).ReturnsAsync(true);
			var provisioning = new Resgrid.Services.MemberProvisioningService(fixture.Accounts.Object, users.Object, Mock.Of<IUserProfileService>(),
				fixture.Departments.Object, Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IUserDefinedFieldsService>());
			var writer = new PersonnelImportWriter(provisioning, fixture.Accounts.Object, fixture.Departments.Object, Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IUserDefinedFieldsService>(), fixture.Delete.Object);
			var context = Context();
			context.PersonnelCapacity = 1;
			context.ResolveReference = (_, _, _) => Task.FromResult<string>(null);
			var row = Person("new@example.org", ImportRowDecision.Create);
			row.TargetKey = "PRE";
			var (journal, entries) = Journal();

			(await writer.WriteRowAsync(context, row, journal, CancellationToken.None)).Outcome.Should().Be(ImportRowOutcome.Created);

			var entry = entries.Should().ContainSingle(e => e.TargetKey == "PRE").Subject;
			entry.CreatedAccount.Should().BeTrue();
			fixture.Accounts.Verify(a => a.CreateAsync(It.IsAny<IdentityUser>(), It.IsAny<string>()), Times.Never);

			fixture.Departments.Setup(d => d.GetDepartmentMemberAsync("PRE", 7, true))
				.ReturnsAsync(new DepartmentMember { DepartmentId = 7, UserId = "PRE", ImportActivationState = (int)ImportActivationState.Dormant });
			fixture.Departments.Setup(d => d.GetAllDepartmentsForUserAsync("PRE")).ReturnsAsync(new List<DepartmentMember> { new DepartmentMember { DepartmentId = 7 } });
			fixture.Delete.Setup(d => d.DeleteUserAsync(7, "admin", "PRE", It.IsAny<CancellationToken>())).ReturnsAsync(DeleteUserResults.NoFailure);

			(await writer.RollbackAsync(context, entry, CancellationToken.None)).Outcome.Should().Be(ImportRollbackOutcome.Deleted);
			fixture.Delete.Verify(d => d.RevokeDepartmentAccessAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- Kept custom fields ----------------------------------------------------------------------------------------

		[Test]
		public async Task Kept_columns_publish_a_first_definition_but_never_replace_an_existing_one()
		{
			var udf = new Mock<IUserDefinedFieldsService>();
			List<UdfField> published = null;
			udf.Setup(u => u.SaveDefinitionAsync(7, (int)UdfEntityType.Unit, It.IsAny<List<UdfField>>(), "admin", It.IsAny<CancellationToken>()))
				.Callback<int, int, List<UdfField>, string, CancellationToken>((_, _, f, _, _) => published = f)
				.ReturnsAsync(new UdfDefinition());

			(await ImportCustomFields.PublishFirstDefinitionAsync(udf.Object, 7, UdfEntityType.Unit, new[] { "Radio ID", "Radio-ID!", "Пожарный" }, "admin", CancellationToken.None))
				.Should().BeTrue();
			published.Select(f => f.Name).Should().OnlyHaveUniqueItems();
			published.Should().OnlyContain(f => Resgrid.Model.Helpers.UdfValidationHelper.IsValidMachineName(f.Name));
			published.Should().OnlyContain(f => f.Visibility == (int)UdfFieldVisibility.DepartmentAdminsOnly);

			udf.Setup(u => u.GetActiveDefinitionAsync(8, (int)UdfEntityType.Personnel)).ReturnsAsync(new UdfDefinition());
			udf.Setup(u => u.GetFieldsForActiveDefinitionAsync(8, (int)UdfEntityType.Personnel)).ReturnsAsync(new List<UdfField> { new UdfField { Label = "Shirt size" } });
			(await ImportCustomFields.PublishFirstDefinitionAsync(udf.Object, 8, UdfEntityType.Personnel, new[] { "shirt size" }, "admin", CancellationToken.None)).Should().BeTrue();
			(await ImportCustomFields.PublishFirstDefinitionAsync(udf.Object, 8, UdfEntityType.Personnel, new[] { "Boot size" }, "admin", CancellationToken.None)).Should().BeFalse();
			udf.Verify(u => u.SaveDefinitionAsync(8, It.IsAny<int>(), It.IsAny<List<UdfField>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public void Transient_errors_are_recognized_through_wrappers()
		{
			ImportCommitHandler.IsTransient(new TimeoutException()).Should().BeTrue();
			ImportCommitHandler.IsTransient(new InvalidOperationException("x", new TimeoutException())).Should().BeTrue();
			ImportCommitHandler.IsTransient(new InvalidOperationException("certifications_type_scope")).Should().BeFalse();
		}
	}
}

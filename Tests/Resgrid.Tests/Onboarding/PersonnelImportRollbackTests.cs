using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding.Writers;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// A removed member comes back as a dormant member (wizard plan section 12.3), and rollback takes back exactly what a
	/// reviewed update or a reactivation added to a member, unless it changed since (section 11.8).
	/// </summary>
	[TestFixture]
	public class PersonnelImportRollbackTests
	{
		private const int DepartmentId = 7;
		private const string UserId = "U1";

		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IDepartmentMemberSensitiveDataService> _sensitive;
		private DepartmentMember _member;
		private DepartmentGroupMember _groupMember;
		private List<PersonnelRoleUser> _roleUsers;
		private DepartmentMemberSensitiveData _data;
		private List<ImportChangeJournalEntry> _journal;

		[SetUp]
		public void SetUp()
		{
			_departments = new Mock<IDepartmentsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_sensitive = new Mock<IDepartmentMemberSensitiveDataService>();
			_journal = new List<ImportChangeJournalEntry>();
			_roleUsers = new List<PersonnelRoleUser>();
			_groupMember = null;
			_data = null;
			_member = new DepartmentMember { DepartmentId = DepartmentId, UserId = UserId, IsDeleted = true, IsHidden = true, IsDisabled = false, IsAdmin = true };

			_departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, true)).ReturnsAsync(() => _member);
			_departments.Setup(d => d.SaveDepartmentMemberAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentMember m, CancellationToken _) => m);
			_groups.Setup(g => g.GetGroupMemberForUserAsync(UserId, DepartmentId)).ReturnsAsync(() => _groupMember);
			_groups.Setup(g => g.SaveGroupMemberAsync(It.IsAny<DepartmentGroupMember>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.Callback((DepartmentGroupMember m, ImportEffectPolicy _, CancellationToken _) => _groupMember = m)
				.ReturnsAsync((DepartmentGroupMember m, ImportEffectPolicy _, CancellationToken _) => m);
			_groups.Setup(g => g.DeleteUserFromGroupsAsync(UserId, DepartmentId, It.IsAny<CancellationToken>()))
				.Callback(() => _groupMember = null).ReturnsAsync(true);
			_roles.Setup(r => r.GetRolesForUserAsync(UserId, DepartmentId)).ReturnsAsync(() => _roleUsers.Select(u => new PersonnelRole { PersonnelRoleId = u.PersonnelRoleId }).ToList());
			_roles.Setup(r => r.GetRoleByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new PersonnelRole { PersonnelRoleId = id, DepartmentId = DepartmentId });
			_roles.Setup(r => r.SaveRoleAsync(It.IsAny<PersonnelRole>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>(), It.IsAny<string>()))
				.Callback((PersonnelRole role, ImportEffectPolicy _, CancellationToken _, string _) => _roleUsers.AddRange(role.Users))
				.ReturnsAsync((PersonnelRole role, ImportEffectPolicy _, CancellationToken _, string _) => role);
			_roles.Setup(r => r.GetAllMembersOfRoleAsync(It.IsAny<int>())).ReturnsAsync((int id) => _roleUsers.Where(u => u.PersonnelRoleId == id).ToList());
			_roles.Setup(r => r.DeleteRoleUsersAsync(It.IsAny<List<PersonnelRoleUser>>(), It.IsAny<CancellationToken>()))
				.Callback((List<PersonnelRoleUser> users, CancellationToken _) => _roleUsers.RemoveAll(users.Contains)).ReturnsAsync(true);
			_sensitive.Setup(s => s.GetByDepartmentAndUserAsync(DepartmentId, UserId)).ReturnsAsync(() => _data);
			_sensitive.Setup(s => s.SaveAsync(It.IsAny<DepartmentMemberSensitiveData>(), It.IsAny<ImportEffectPolicy>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentMemberSensitiveData d, ImportEffectPolicy _, CancellationToken _) =>
				{
					d.CreatedOn = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);
					return _data = d;
				});
			_sensitive.Setup(s => s.DeleteForMemberAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).Callback(() => _data = null).ReturnsAsync(true);
		}

		private PersonnelImportWriter Writer() => new(Mock.Of<IMemberProvisioningService>(), Mock.Of<IMemberAccountService>(), _departments.Object, _groups.Object,
			_roles.Object, _sensitive.Object, Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IDeleteService>());

		private static ImportWriteContext Context(int seats = 5) => new()
		{
			DepartmentId = DepartmentId,
			WriteMode = ImportWriteMode.ReviewedUpdate,
			Authority = ImportAuthority.ForAdministrator(DepartmentId, "ADMIN"),
			PersonnelCapacity = seats,
			ResolveReference = (entity, token, _) => Task.FromResult(entity == "groups" ? "12" : token == "src:R1" ? "31" : "32")
		};

		private ImportJournalWriter Journal() => new((entry, _) =>
		{
			entry.Id = Guid.NewGuid().ToString();
			_journal.Add(entry);
			return Task.CompletedTask;
		});

		private static ImportCanonicalRow Row(ImportRowDecision decision)
		{
			var row = new ImportCanonicalRow { StagedRowId = "S1", RowNumber = 2, Decision = decision, TargetKey = UserId };
			row.Values["email"] = "pat@example.org";
			row.Values["group"] = "src:G1";
			row.Values["roles"] = new List<string> { "src:R1", "src:R2" };
			row.Values["notes"] = "Prefers nights";
			return row;
		}

		[Test]
		public async Task A_removed_member_comes_back_dormant_never_admin_with_the_rows_department_data()
		{
			var context = Context();

			var result = await Writer().WriteRowAsync(context, Row(ImportRowDecision.Reactivate), Journal(), CancellationToken.None);

			result.Outcome.Should().Be(ImportRowOutcome.Updated);
			_member.IsDeleted.Should().BeFalse();
			_member.IsAdmin.Should().BeFalse();
			_member.IsHidden.Should().BeFalse();
			_member.ImportActivationState.Should().Be((int)ImportActivationState.Dormant);
			_member.IsImportDormant.Should().BeTrue("nothing reaches them but the invitation");
			_departments.Verify(d => d.SaveDepartmentMemberAsync(It.IsAny<DepartmentMember>(), It.IsAny<CancellationToken>()), Times.Once);
			_groupMember.DepartmentGroupId.Should().Be(12);
			_roleUsers.Select(r => r.PersonnelRoleId).Should().BeEquivalentTo(new[] { 31, 32 });
			context.PersonnelCapacity.Should().Be(4);

			var entry = _journal.Single();
			entry.Operation.Should().Be((int)ImportJournalOperation.Reactivate);
			var before = JsonConvert.DeserializeObject<PersonnelImportWriter.ReactivatedMember>(entry.BeforeValues);
			before.IsAdmin.Should().BeTrue();
			before.IsHidden.Should().BeTrue();
			before.Added.GroupId.Should().Be(12);
			before.Added.SensitiveDataVersion.Should().NotBeNull();
			entry.BeforeValues.Should().NotContain("Prefers nights", "the journal records what was added, never the values");
		}

		[Test]
		public async Task A_reactivation_needs_a_seat()
		{
			var result = await Writer().WriteRowAsync(Context(seats: 0), Row(ImportRowDecision.Reactivate), Journal(), CancellationToken.None);

			result.Code.Should().Be("IMP-PLAN-CAPACITY");
			_member.IsDeleted.Should().BeTrue();
		}

		[Test]
		public async Task Rolling_back_a_reactivation_removes_the_member_again_as_they_were()
		{
			var writer = Writer();
			await writer.WriteRowAsync(Context(), Row(ImportRowDecision.Reactivate), Journal(), CancellationToken.None);

			var rolledBack = await writer.RollbackAsync(Context(), _journal.Single(), CancellationToken.None);

			rolledBack.Outcome.Should().Be(ImportRollbackOutcome.MembershipRemoved);
			_member.IsDeleted.Should().BeTrue();
			_member.IsAdmin.Should().BeTrue();
			_member.IsHidden.Should().BeTrue();
			_member.ImportActivationState.Should().BeNull();
			_groupMember.Should().BeNull();
			_roleUsers.Should().BeEmpty();
			_data.Should().BeNull();
		}

		[Test]
		public async Task A_reactivated_member_who_accepted_their_invitation_is_kept()
		{
			var writer = Writer();
			await writer.WriteRowAsync(Context(), Row(ImportRowDecision.Reactivate), Journal(), CancellationToken.None);
			_member.ImportActivationState = (int)ImportActivationState.Activated;

			var rolledBack = await writer.RollbackAsync(Context(), _journal.Single(), CancellationToken.None);

			rolledBack.Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);
			rolledBack.Code.Should().Be("IMP-ROLLBACK-HAS-ACTIVITY");
			_member.IsDeleted.Should().BeFalse();
		}

		[Test]
		public async Task Rolling_back_a_reviewed_update_takes_back_exactly_what_it_added()
		{
			_member.IsDeleted = false;
			_roleUsers.Add(new PersonnelRoleUser { PersonnelRoleId = 30, DepartmentId = DepartmentId, UserId = UserId });
			var writer = Writer();
			await writer.WriteRowAsync(Context(), Row(ImportRowDecision.Update), Journal(), CancellationToken.None);
			_roleUsers.Select(r => r.PersonnelRoleId).Should().BeEquivalentTo(new[] { 30, 31, 32 });

			var rolledBack = await writer.RollbackAsync(Context(), _journal.Single(), CancellationToken.None);

			rolledBack.Outcome.Should().Be(ImportRollbackOutcome.Restored);
			_roleUsers.Select(r => r.PersonnelRoleId).Should().Equal(30);
			_groupMember.Should().BeNull();
			_data.Should().BeNull();
		}

		[Test]
		public async Task What_changed_after_the_update_is_left_alone_and_reported()
		{
			_member.IsDeleted = false;
			var writer = Writer();
			await writer.WriteRowAsync(Context(), Row(ImportRowDecision.Update), Journal(), CancellationToken.None);
			// Someone moved them to another station and edited their details since.
			_groupMember = new DepartmentGroupMember { DepartmentGroupId = 99, DepartmentId = DepartmentId, UserId = UserId };
			_data.UpdatedOn = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);

			var rolledBack = await writer.RollbackAsync(Context(), _journal.Single(), CancellationToken.None);

			rolledBack.Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);
			rolledBack.Code.Should().Be("IMP-ROLLBACK-MEMBER-UPDATE");
			_groupMember.DepartmentGroupId.Should().Be(99);
			_data.Should().NotBeNull();
			_roleUsers.Should().BeEmpty("the roles it added were still only the import's");
		}

		[Test]
		public async Task An_update_recorded_before_additions_were_tracked_stays_a_manual_check()
		{
			_member.IsDeleted = false;
			var entry = new ImportChangeJournalEntry
			{
				Id = "J1", TargetKey = UserId, Operation = (int)ImportJournalOperation.Update, BeforeValues = "{\"group\":null,\"roles_added\":\"31\"}"
			};

			var rolledBack = await Writer().RollbackAsync(Context(), entry, CancellationToken.None);

			rolledBack.Code.Should().Be("IMP-ROLLBACK-MEMBER-UPDATE");
			_roles.Verify(r => r.DeleteRoleUsersAsync(It.IsAny<List<PersonnelRoleUser>>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}

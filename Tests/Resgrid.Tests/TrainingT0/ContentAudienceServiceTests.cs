using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.TrainingT0
{
	/// <summary>
	/// The shared audience resolver (Training T0, Protocols P1): each kind resolves to the department's active members only,
	/// foreign groups / roles / types / shifts are ignored, and picked members who cannot be addressed come back with a reason.
	/// </summary>
	[TestFixture]
	public class ContentAudienceServiceTests
	{
		private const int DepartmentId = 10;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<ICertificationService> _certifications;
		private Mock<IUnitsService> _units;
		private Mock<IShiftsService> _shifts;
		private ContentAudienceService _service;

		[SetUp]
		public void SetUp()
		{
			_departments = new Mock<IDepartmentsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_certifications = new Mock<ICertificationService>();
			_units = new Mock<IUnitsService>();
			_shifts = new Mock<IShiftsService>();
			_service = new ContentAudienceService(_departments.Object, _groups.Object, _roles.Object, _certifications.Object, _units.Object, _shifts.Object);

			_departments.Setup(x => x.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(new HashSet<string>(new[] { "a", "b", "c", "d" }, StringComparer.OrdinalIgnoreCase));
		}

		private static ContentAudienceRule Rule(ContentAudienceKinds kind, object refId = null) => new ContentAudienceRule(kind, refId?.ToString());

		private async Task<HashSet<string>> Resolve(params ContentAudienceRule[] rules) => (await _service.ResolveAsync(DepartmentId, rules)).MemberUserIds;

		[Test]
		public async Task Everyone_is_every_active_member()
		{
			(await Resolve(Rule(ContentAudienceKinds.Everyone))).Should().BeEquivalentTo(new[] { "a", "b", "c", "d" });
		}

		[Test]
		public async Task A_group_resolves_to_its_active_members_and_a_foreign_group_to_nobody()
		{
			_groups.Setup(x => x.GetGroupByIdAsync(3, false)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 3, DepartmentId = DepartmentId });
			_groups.Setup(x => x.GetAllMembersForGroupAsync(3)).ReturnsAsync(new List<DepartmentGroupMember>
			{
				new DepartmentGroupMember { UserId = "A" }, new DepartmentGroupMember { UserId = "gone" }
			});
			_groups.Setup(x => x.GetGroupByIdAsync(4, false)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = 99 });
			_groups.Setup(x => x.GetAllMembersForGroupAsync(4)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "b" } });

			(await Resolve(Rule(ContentAudienceKinds.Group, 3))).Should().BeEquivalentTo(new[] { "A" });
			(await Resolve(Rule(ContentAudienceKinds.Group, 4))).Should().BeEmpty();
		}

		[Test]
		public async Task A_role_resolves_to_its_members_in_the_department()
		{
			_roles.Setup(x => x.GetRoleByIdAsync(5)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 5, DepartmentId = DepartmentId });
			_roles.Setup(x => x.GetAllMembersOfRoleAsync(5)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "b" }, new PersonnelRoleUser { UserId = "c" } });
			_roles.Setup(x => x.GetRoleByIdAsync(6)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 6, DepartmentId = 99 });

			(await Resolve(Rule(ContentAudienceKinds.Role, 5))).Should().BeEquivalentTo(new[] { "b", "c" });
			(await Resolve(Rule(ContentAudienceKinds.Role, 6))).Should().BeEmpty();
		}

		[Test]
		public async Task A_certification_type_resolves_to_holders_of_a_current_record_only()
		{
			var now = DateTime.UtcNow;
			_certifications.Setup(x => x.GetCertificationsForDepartmentAsync(DepartmentId, null)).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "a", DepartmentCertificationTypeId = 7, Status = (int)PersonnelCertificationStatuses.Active },
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "b", DepartmentCertificationTypeId = 7, Status = (int)PersonnelCertificationStatuses.Trainee, ExpiresOn = now.AddDays(30) },
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "c", DepartmentCertificationTypeId = 7, Status = (int)PersonnelCertificationStatuses.Active, ExpiresOn = now.AddDays(-1) },
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "d", DepartmentCertificationTypeId = 7, Status = (int)PersonnelCertificationStatuses.Revoked },
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "d", DepartmentCertificationTypeId = 7, Status = (int)PersonnelCertificationStatuses.Active, IsDeleted = true },
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "d", DepartmentCertificationTypeId = 8, Status = (int)PersonnelCertificationStatuses.Active }
			});

			(await Resolve(Rule(ContentAudienceKinds.CertificationType, 7))).Should().BeEquivalentTo(new[] { "a", "b" });
		}

		[Test]
		public async Task A_unit_type_resolves_to_members_riding_units_of_that_type()
		{
			_units.Setup(x => x.GetUnitTypeByIdAsync(2)).ReturnsAsync(new UnitType { UnitTypeId = 2, DepartmentId = DepartmentId, Type = "Engine" });
			_units.Setup(x => x.GetUnitTypeByIdAsync(3)).ReturnsAsync(new UnitType { UnitTypeId = 3, DepartmentId = 99, Type = "Engine" });
			_units.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 1, DepartmentId = DepartmentId, Type = "engine" },
				new Unit { UnitId = 2, DepartmentId = DepartmentId, Type = "Ladder" }
			});
			_units.Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole>
			{
				new UnitActiveRole { UnitId = 1, UserId = "a" }, new UnitActiveRole { UnitId = 2, UserId = "b" }, new UnitActiveRole { UnitId = 1, UserId = "outsider" }
			});

			(await Resolve(Rule(ContentAudienceKinds.UnitType, 2))).Should().BeEquivalentTo(new[] { "a" });
			(await Resolve(Rule(ContentAudienceKinds.UnitType, 3))).Should().BeEmpty();
		}

		[Test]
		public async Task A_shift_resolves_to_its_standing_members()
		{
			var shift = new Shift { ShiftId = 4, DepartmentId = DepartmentId };
			_shifts.Setup(x => x.GetShiftByIdAsync(4)).ReturnsAsync(shift);
			_shifts.Setup(x => x.PopulateShiftData(shift, false, true, false, false, false)).ReturnsAsync(new Shift
			{
				ShiftId = 4, DepartmentId = DepartmentId, Personnel = new List<ShiftPerson> { new ShiftPerson { UserId = "c" }, new ShiftPerson { UserId = "d" } }
			});
			_shifts.Setup(x => x.GetShiftByIdAsync(5)).ReturnsAsync(new Shift { ShiftId = 5, DepartmentId = 99 });

			(await Resolve(Rule(ContentAudienceKinds.Shift, 4))).Should().BeEquivalentTo(new[] { "c", "d" });
			(await Resolve(Rule(ContentAudienceKinds.Shift, 5))).Should().BeEmpty();
		}

		[Test]
		public async Task Picked_users_resolve_when_active_and_are_explained_otherwise()
		{
			_departments.Setup(x => x.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "deleted", IsDeleted = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "disabled", IsDisabled = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "hidden", IsHidden = true }
			});

			var result = await _service.ResolveAsync(DepartmentId, new[]
			{
				Rule(ContentAudienceKinds.User, "A"), Rule(ContentAudienceKinds.User, "deleted"), Rule(ContentAudienceKinds.User, "disabled"),
				Rule(ContentAudienceKinds.User, "hidden"), Rule(ContentAudienceKinds.User, "stranger")
			});

			result.MemberUserIds.Should().BeEquivalentTo(new[] { "A" });
			result.Skipped.Select(s => (s.UserId, s.Reason)).Should().BeEquivalentTo(new[]
			{
				("deleted", ContentAudienceSkipReasons.Deleted), ("disabled", ContentAudienceSkipReasons.Disabled),
				("hidden", ContentAudienceSkipReasons.Hidden), ("stranger", ContentAudienceSkipReasons.NotInDepartment)
			});
		}

		[Test]
		public async Task A_member_who_left_and_came_back_is_explained_by_their_live_row()
		{
			_departments.Setup(x => x.GetAllMembersForDepartmentIncludingDeletedAsync(DepartmentId)).ReturnsAsync(new List<DepartmentMember>
			{
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "returner", IsDeleted = true },
				new DepartmentMember { DepartmentId = DepartmentId, UserId = "RETURNER", IsHidden = true }
			});

			var skipped = await _service.ExplainSkippedAsync(DepartmentId, new[] { "returner", "", null, "returner" });

			skipped.Should().ContainSingle().Which.Reason.Should().Be(ContentAudienceSkipReasons.Hidden);
		}

		[Test]
		public async Task No_rules_resolve_to_nobody_without_reading_anything()
		{
			(await _service.ResolveAsync(DepartmentId, null)).MemberUserIds.Should().BeEmpty();
			(await _service.ResolveAsync(DepartmentId, new[] { Rule(ContentAudienceKinds.Group, "abc") })).MemberUserIds.Should().BeEmpty();
			_departments.Verify(x => x.GetActiveMemberUserIdsAsync(It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task Member_facts_collect_what_one_member_belongs_to_and_match_rules_without_more_queries()
		{
			_departments.Setup(x => x.GetDepartmentMemberAsync("a", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "a" });
			_groups.Setup(x => x.GetGroupForUserAsync("a", DepartmentId)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 3, DepartmentId = DepartmentId });
			_roles.Setup(x => x.GetRolesForUserAsync("a", DepartmentId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 5, DepartmentId = DepartmentId } });
			_certifications.Setup(x => x.GetCertificationsByUserIdAsync("a")).ReturnsAsync(new List<PersonnelCertification>
			{
				new PersonnelCertification { DepartmentId = DepartmentId, UserId = "a", DepartmentCertificationTypeId = 7, Status = (int)PersonnelCertificationStatuses.Active },
				new PersonnelCertification { DepartmentId = 99, UserId = "a", DepartmentCertificationTypeId = 70, Status = (int)PersonnelCertificationStatuses.Active }
			});
			_units.Setup(x => x.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = 1, UserId = "A" } });
			_units.Setup(x => x.GetUnitsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 1, Type = "Engine" } });
			_units.Setup(x => x.GetUnitTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<UnitType> { new UnitType { UnitTypeId = 2, Type = "engine" }, new UnitType { UnitTypeId = 9, Type = "Ladder" } });
			_shifts.Setup(x => x.GetShiftPersonsForUserAsync("a")).ReturnsAsync(new List<ShiftPerson> { new ShiftPerson { ShiftId = 4 }, new ShiftPerson { ShiftId = 44 } });
			_shifts.Setup(x => x.GetAllShiftsByDepartmentAsync(DepartmentId)).ReturnsAsync(new List<Shift> { new Shift { ShiftId = 4 } });

			var facts = await _service.GetMemberFactsAsync(DepartmentId, "a");

			facts.IsActive.Should().BeTrue();
			facts.GroupIds.Should().Equal(3);
			facts.RoleIds.Should().Equal(5);
			facts.CertificationTypeIds.Should().Equal(7);
			facts.UnitTypeIds.Should().Equal(2);
			facts.ShiftIds.Should().Equal(4);

			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.Group, 3) }).Should().BeTrue();
			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.Role, 5) }).Should().BeTrue();
			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.CertificationType, 70) }).Should().BeFalse();
			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.UnitType, 9), Rule(ContentAudienceKinds.Shift, 4) }).Should().BeTrue();
			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.User, "A") }).Should().BeTrue();
			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.Shift, 44) }).Should().BeFalse();
			(await _service.IsUserInAudienceAsync(DepartmentId, "a", new[] { Rule(ContentAudienceKinds.Everyone) })).Should().BeTrue();
		}

		[Test]
		public async Task An_inactive_member_matches_no_audience_not_even_everyone()
		{
			_departments.Setup(x => x.GetDepartmentMemberAsync("h", DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentMember { DepartmentId = DepartmentId, UserId = "h", IsHidden = true });

			var facts = await _service.GetMemberFactsAsync(DepartmentId, "h");

			facts.IsActive.Should().BeFalse();
			ContentAudienceMatcher.Matches(facts, new[] { Rule(ContentAudienceKinds.Everyone), Rule(ContentAudienceKinds.User, "h") }).Should().BeFalse();
			_groups.Verify(x => x.GetGroupForUserAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public void Normalize_drops_bad_rows_deduplicates_and_lets_everyone_absorb_the_rest()
		{
			ContentAudienceMatcher.Normalize(new IContentAudienceRule[]
			{
				Rule(ContentAudienceKinds.Group, 3), Rule(ContentAudienceKinds.Group, " 3 "), Rule(ContentAudienceKinds.Group, "x"), Rule(ContentAudienceKinds.Group, -1),
				Rule(ContentAudienceKinds.User, "Bob"), Rule(ContentAudienceKinds.User, "bob"), new ContentAudienceRule { Kind = 42, RefId = "1" }, null
			}).Select(r => (r.Kind, r.RefId)).Should().BeEquivalentTo(new[] { ((int)ContentAudienceKinds.Group, "3"), ((int)ContentAudienceKinds.User, "Bob") });

			ContentAudienceMatcher.Normalize(new[] { Rule(ContentAudienceKinds.Group, 3), Rule(ContentAudienceKinds.Everyone) })
				.Should().ContainSingle().Which.Kind.Should().Be((int)ContentAudienceKinds.Everyone);
		}

		[Test]
		public async Task The_change_queue_never_throws_and_skips_blank_members()
		{
			var repository = new Mock<IContentAudienceChangesRepository>();
			repository.Setup(x => x.InsertAsync(It.IsAny<ContentAudienceChange>(), It.IsAny<CancellationToken>(), true)).ThrowsAsync(new InvalidOperationException("db down"));
			var queue = new ContentAudienceChangeQueue(repository.Object);

			await queue.Invoking(q => q.EnqueueAsync(DepartmentId, "a", ContentAudienceChangeSources.Group)).Should().NotThrowAsync();

			repository.Reset();
			await queue.EnqueueManyAsync(DepartmentId, new[] { "a", "A", "", null, "b" }, ContentAudienceChangeSources.Role);
			await queue.EnqueueAsync(0, "a", ContentAudienceChangeSources.Role);
			await queue.EnqueueAsync(DepartmentId, " ", ContentAudienceChangeSources.Shift);

			repository.Verify(x => x.InsertAsync(It.Is<ContentAudienceChange>(c => c.UserId == "a" && c.Source == (int)ContentAudienceChangeSources.Role), It.IsAny<CancellationToken>(), true), Times.Once);
			repository.Verify(x => x.InsertAsync(It.Is<ContentAudienceChange>(c => c.UserId == "b"), It.IsAny<CancellationToken>(), true), Times.Once);
			repository.Verify(x => x.InsertAsync(It.Is<ContentAudienceChange>(c => c.UserId == null && c.DepartmentId == DepartmentId), It.IsAny<CancellationToken>(), true), Times.Once);
			repository.Verify(x => x.InsertAsync(It.IsAny<ContentAudienceChange>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(3));
		}
	}
}

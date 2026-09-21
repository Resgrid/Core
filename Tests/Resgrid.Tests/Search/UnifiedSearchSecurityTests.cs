using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Search
{
	public partial class UnifiedSearchServiceTests
	{
		private Task<UnifiedSearchResult> Search(string resource = "Call", bool prefix = false) =>
			_service.SearchAsync(new UnifiedSearchRequest { Text = "secret", Prefix = prefix }, Principal(resource + ":View"));

		[TestCase("Call", "Call")]
		[TestCase("Unit", "Unit")]
		[TestCase("Personnel", "Personnel")]
		[TestCase("Message", "Messages")]
		[TestCase("Contact", "Contacts")]
		[TestCase("Document", "Documents")]
		[TestCase("Note", "Notes")]
		public async Task Foreign_index_hits_are_rejected_for_every_family(string type, string resource)
		{
			var hit = Hit(type, "9");
			hit.DepartmentId = 8;
			Answer(hit);
			var result = await Search(resource);
			result.Hits.Should().BeEmpty();
			result.Total.Should().BeNull();
		}

		[TestCase("Call", "Call")]
		[TestCase("Unit", "Unit")]
		[TestCase("Personnel", "Personnel")]
		[TestCase("Message", "Messages")]
		[TestCase("Contact", "Contacts")]
		[TestCase("Document", "Documents")]
		[TestCase("Note", "Notes")]
		public async Task Live_ownership_is_rechecked_even_when_the_index_claims_our_department(string type, string resource)
		{
			Answer(Hit(type, "9"));
			SeedEntity(type, 8);
			var result = await Search(resource);
			result.Hits.Should().BeEmpty();
			result.Total.Should().BeNull();
			SeedEntity(type, 7);
			(await Search(resource)).Hits.Should().ContainSingle("the same valid entity is visible in the caller's department");
		}

		private void SeedEntity(string type, int departmentId)
		{
			switch (type)
			{
				case "Call": _calls.Setup(c => c.GetCallByIdAsync(9, true)).ReturnsAsync(new Call { CallId = 9, DepartmentId = departmentId }); break;
				case "Unit": _units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { UnitId = 9, DepartmentId = departmentId }); break;
				case "Personnel": _departments.Setup(d => d.GetDepartmentMemberAsync("9", 7, true)).ReturnsAsync(new DepartmentMember { UserId = "9", DepartmentId = departmentId }); break;
				case "Message": _messages.Setup(m => m.GetMessageByIdAsync(9)).ReturnsAsync(new Message { MessageId = 9, DepartmentId = departmentId, SendingUserId = "u1" }); break;
				case "Contact": _contacts.Setup(c => c.GetContactByIdAsync("9")).ReturnsAsync(new Contact { ContactId = "9", DepartmentId = departmentId }); break;
				case "Document": _documents.Setup(d => d.GetDocumentByIdAsync(9)).ReturnsAsync(new Document { DocumentId = 9, DepartmentId = departmentId }); break;
				case "Note": _notes.Setup(n => n.GetNoteByIdAsync(9)).ReturnsAsync(new Note { NoteId = 9, DepartmentId = departmentId }); break;
			}
		}

		[TestCase("missing")]
		[TestCase("deleted")]
		[TestCase("disabled")]
		[TestCase("foreign")]
		public async Task Revoked_or_foreign_members_cannot_search_even_with_admin_claims(string state)
		{
			_viewer.IsAdmin = true;
			if (state == "missing") _viewer = null;
			if (state == "deleted") _viewer.IsDeleted = true;
			if (state == "disabled") _viewer.IsDisabled = true;
			if (state == "foreign") _viewer.DepartmentId = 8;
			var principal = Principal("Call:View");
			principal.IsDepartmentAdmin = true;
			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "secret" }, principal);
			result.Available.Should().BeFalse();
			result.Hits.Should().BeEmpty();
			result.Actions.Should().BeEmpty();
			_global.Verify(g => g.SearchAsync(It.IsAny<int>(), It.IsAny<GlobalSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Search_and_typeahead_use_live_group_permissions_without_a_matrix(bool prefix)
		{
			Answer(Hit("Unit", "9"), Hit("Unit", "10"), Hit("Personnel", "9"), Hit("Personnel", "10"));
			foreach (var id in new[] { 9, 10 })
			{
				_units.Setup(u => u.GetUnitByIdAsync(id)).ReturnsAsync(new Unit { UnitId = id, DepartmentId = 7, StationGroupId = id });
				_departments.Setup(d => d.GetDepartmentMemberAsync(id.ToString(), 7, true)).ReturnsAsync(new DepartmentMember { UserId = id.ToString(), DepartmentId = 7 });
				_groups.Setup(g => g.GetGroupMemberForUserAsync(id.ToString(), 7)).ReturnsAsync(new DepartmentGroupMember { UserId = id.ToString(), DepartmentId = 7, DepartmentGroupId = id });
			}
			_groups.Setup(g => g.GetGroupMemberForUserAsync("u1", 7)).ReturnsAsync(new DepartmentGroupMember { UserId = "u1", DepartmentId = 7, DepartmentGroupId = 9 });
			var permission = new Permission { DepartmentId = 7, Action = (int)PermissionActions.Everyone, LockToGroup = true };
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(7, It.IsAny<int>())).ReturnsAsync(permission);
			var request = new UnifiedSearchRequest { Text = "secret", Prefix = prefix };
			var principal = Principal("Unit:View", "Personnel:View");
			var result = await _service.SearchAsync(request, principal);
			result.Hits.Should().HaveCount(2).And.OnlyContain(h => h.EntityId == "9");
			result.Total.Should().BeNull();
			_auth.Verify(a => a.CanUserViewUnitViaMatrixAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
			_auth.Verify(a => a.CanUserViewPersonViaMatrixAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
			permission.Action = (int)PermissionActions.DepartmentAdminsOnly;
			(await _service.SearchAsync(request, principal)).Hits.Should().BeEmpty("permission revocation takes effect on the next request");
		}

		[TestCase(PermissionActions.DepartmentAdminsOnly, false, false, false)]
		[TestCase(PermissionActions.DepartmentAndGroupAdmins, true, false, true)]
		[TestCase(PermissionActions.DepartmentAndGroupAdmins, false, true, false)]
		[TestCase(PermissionActions.DepartmentAdminsAndSelectRoles, false, true, true)]
		[TestCase(PermissionActions.DepartmentAdminsAndSelectRoles, true, false, false)]
		[TestCase(PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, true, false, true)]
		[TestCase(PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, false, true, true)]
		[TestCase((PermissionActions)99, true, true, false)]
		public async Task Group_admin_and_selected_role_rules_are_enforced(PermissionActions action, bool groupAdmin, bool hasRole, bool allowed)
		{
			Answer(Hit("Unit", "9"));
			_units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { DepartmentId = 7, UnitId = 9, StationGroupId = 4 });
			_groups.Setup(g => g.GetGroupMemberForUserAsync("u1", 7)).ReturnsAsync(new DepartmentGroupMember { DepartmentId = 7, UserId = "u1", DepartmentGroupId = 4, IsAdmin = groupAdmin });
			_roles.Setup(r => r.GetRolesForUserAsync("u1", 7)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { DepartmentId = 7, PersonnelRoleId = hasRole ? 12 : 13 } });
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(7, (int)PermissionTypes.ViewGroupUnits)).ReturnsAsync(new Permission { DepartmentId = 7, Action = (int)action, LockToGroup = true, Data = "12" });
			(await Search("Unit")).Hits.Count.Should().Be(allowed ? 1 : 0);
		}

		[TestCase("hidden")]
		[TestCase("deleted")]
		[TestCase("disabled")]
		public async Task Personnel_visibility_is_rechecked_against_current_membership(string state)
		{
			Answer(Hit("Personnel", "9"));
			_departments.Setup(d => d.GetDepartmentMemberAsync("9", 7, true)).ReturnsAsync(new DepartmentMember
			{
				UserId = "9", DepartmentId = 7, IsHidden = state == "hidden", IsDisabled = state == "disabled", IsDeleted = state == "deleted"
			});
			(await Search("Personnel")).Hits.Should().BeEmpty();
		}

		[TestCase("Document", "Documents")]
		[TestCase("Note", "Notes")]
		public async Task Newly_admin_only_entities_are_hidden_and_revoked_admin_claims_do_not_bypass_checks(string type, string resource)
		{
			Answer(Hit(type, "9"));
			_documents.Setup(d => d.GetDocumentByIdAsync(9)).ReturnsAsync(new Document { DepartmentId = 7, DocumentId = 9, AdminsOnly = true });
			_notes.Setup(n => n.GetNoteByIdAsync(9)).ReturnsAsync(new Note { DepartmentId = 7, NoteId = 9, IsAdminOnly = true });
			var principal = Principal(resource + ":View");
			principal.IsDepartmentAdmin = true;
			(await _service.SearchAsync(new UnifiedSearchRequest { Text = "secret" }, principal)).Hits.Should().BeEmpty();
			_viewer.IsAdmin = true;
			(await _service.SearchAsync(new UnifiedSearchRequest { Text = "secret" }, principal)).Hits.Should().ContainSingle();
		}

		[Test]
		public async Task Removed_message_recipients_are_not_authorized_by_stale_index_participants()
		{
			Answer(Hit("Message", "9"));
			var recipient = new MessageRecipient { UserId = "u1", DepartmentId = 7, IsDeleted = true };
			_messages.Setup(m => m.GetMessageByIdAsync(9)).ReturnsAsync(new Message { DepartmentId = 7, MessageId = 9, MessageRecipients = new List<MessageRecipient> { recipient } });
			(await Search("Messages")).Hits.Should().BeEmpty();
			recipient.IsDeleted = false;
			(await Search("Messages")).Hits.Should().ContainSingle();
		}

		[TestCase("department")]
		[TestCase("deleted")]
		[TestCase("version")]
		[TestCase("identity")]
		[TestCase("generation")]
		[TestCase("missing")]
		public async Task Stale_or_mismatched_projections_are_not_returned(string mismatch)
		{
			var hit = Hit("Call", "1");
			Answer(hit);
			if (mismatch == "department") _rows[0].DepartmentId = 8;
			if (mismatch == "deleted") _rows[0].DeletedOn = DateTime.UtcNow;
			if (mismatch == "version") _rows[0].RowVersion++;
			if (mismatch == "identity") _rows[0].EntityId = "2";
			if (mismatch == "generation") hit.Generation = "1.0.0";
			if (mismatch == "missing") _rows.Clear();
			var result = await Search();
			result.Hits.Should().BeEmpty();
			result.Total.Should().BeNull();
		}

		[Test]
		public async Task Current_projection_supplies_display_values_instead_of_index_payload()
		{
			Answer(Hit("Call", "1"));
			(await Search()).Hits.Single().Title.Should().Be("Current title");
		}

		[TestCase(DepartmentDataProtectionState.EnrollmentQueued)]
		[TestCase(DepartmentDataProtectionState.Enabled)]
		[TestCase(DepartmentDataProtectionState.Decrypting)]
		public async Task Protected_text_is_suppressed_during_protection_transitions(DepartmentDataProtectionState state)
		{
			Answer(Hit("Call", "1"));
			_rows[0].IncludesProtectedText = true;
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(7, true)).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = 7, State = (int)state });
			(await Search()).Hits.Should().BeEmpty("even when an old index shares the policy epoch");
			_rows[0].IncludesProtectedText = false;
			(await Search()).Hits.Should().ContainSingle("safe metadata remains searchable");
		}

		[Test]
		public async Task Moving_a_member_or_revoking_a_role_takes_effect_on_the_next_search()
		{
			Answer(Hit("Unit", "9"));
			_units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { DepartmentId = 7, UnitId = 9, StationGroupId = 4 });
			var group = new DepartmentGroupMember { DepartmentId = 7, UserId = "u1", DepartmentGroupId = 4 };
			_groups.Setup(g => g.GetGroupMemberForUserAsync("u1", 7)).ReturnsAsync(group);
			var roles = new List<PersonnelRole> { new PersonnelRole { DepartmentId = 7, PersonnelRoleId = 12 } };
			_roles.Setup(r => r.GetRolesForUserAsync("u1", 7)).ReturnsAsync(roles);
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(7, (int)PermissionTypes.ViewGroupUnits))
				.ReturnsAsync(new Permission { DepartmentId = 7, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, LockToGroup = true, Data = "12" });
			(await Search("Unit")).Hits.Should().ContainSingle();
			group.DepartmentGroupId = 5;
			(await Search("Unit")).Hits.Should().BeEmpty();
			group.DepartmentGroupId = 4;
			roles.Clear();
			(await Search("Unit")).Hits.Should().BeEmpty();
		}

		[Test]
		public async Task Permission_or_entity_lookup_errors_never_allow_access()
		{
			Answer(Hit("Unit", "9"));
			_units.Setup(u => u.GetUnitByIdAsync(9)).ThrowsAsync(new InvalidOperationException());
			(await Search("Unit")).Hits.Should().BeEmpty();
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(7, (int)PermissionTypes.ViewGroupUnits)).ThrowsAsync(new InvalidOperationException());
			(await Search("Unit")).Available.Should().BeFalse();
		}

		[Test]
		public async Task Both_indexes_receive_the_current_department_policy_generation()
		{
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(7, true)).ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = 7, CatalogVersion = 25, PolicyEpoch = 41 });
			RecordsAnswer();
			await _service.SearchAsync(new UnifiedSearchRequest { Text = "secret" }, Principal("Call:View", "Record:View"));
			_lastQuery.Generation.Should().Be(GlobalSearchGeneration.Compute(25, 41));
			_recordsSearch.Verify(r => r.SearchAsync(7, It.Is<RecordsSearchRequest>(q => q.Generation == RecordsSearchGeneration.Compute(25, 41)), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Unknown_policy_or_module_state_fails_closed()
		{
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(7, true)).ThrowsAsync(new InvalidOperationException());
			(await Search()).Available.Should().BeFalse();
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(7, true)).ReturnsAsync((DepartmentDataProtectionPolicy)null);
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(7, true)).ThrowsAsync(new InvalidOperationException());
			(await Search()).Available.Should().BeFalse();
		}

		[Test]
		public async Task Current_disabled_module_overrides_old_principal_settings()
		{
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(7, true)).ReturnsAsync(new DepartmentModuleSettings { DocumentsDisabled = true });
			var result = await Search("Documents");
			result.Hits.Should().BeEmpty();
			_lastQuery.EntityTypes.Should().NotContain(SearchEntityTypes.Document, "a disabled module's family never reaches the index");
		}

		[Test]
		public async Task Unchecked_candidates_do_not_leak_through_totals_or_truncation()
		{
			_global.Setup(g => g.SearchAsync(7, It.IsAny<GlobalSearchQuery>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new GlobalSearchResult { Hits = new List<GlobalSearchHit> { Hit("Call", "1") }, Total = 900, Truncated = true });
			var result = await Search();
			result.Hits.Should().ContainSingle();
			result.Total.Should().BeNull();
			result.Truncated.Should().BeFalse();
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Foreign_record_hits_and_projections_are_rejected(bool foreignProjection)
		{
			var hit = new RecordsSearchHit { SourceType = ((int)RmsSearchSourceType.Record).ToString(), SourceId = "r1" };
			RecordsAnswer(hit);
			if (foreignProjection)
				_records.Setup(r => r.GetProjectionsByIdsAsync(7, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsRecordSearchProjection> { new RmsRecordSearchProjection { DepartmentId = 8, RmsRecordSearchProjectionId = "r1", SourceId = "r1", SourceType = (int)RmsSearchSourceType.Record } });
			else hit.DepartmentId = 8;
			var result = await Search("Record");
			result.Hits.Should().BeEmpty();
			result.Total.Should().BeNull();
		}
	}
}

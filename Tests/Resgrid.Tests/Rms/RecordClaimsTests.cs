using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Claims;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Permission-chain tests for PermissionTypes 50-67 (RMS plan section 7, "Permission-chain tests").
	/// The sharp edge is the no-row fall-through: a department with zero Permission rows must get exactly
	/// the access it has under Logs today, per the no-row default pinned in RecordPermissionCatalog.
	/// </summary>
	[TestFixture]
	public class RecordClaimsTests
	{
		private static readonly List<PersonnelRole> NoRoles = new List<PersonnelRole>();

		private static ClaimsIdentity Derive(bool isAdmin, bool isGroupAdmin, List<Permission> permissions = null, List<PersonnelRole> roles = null)
		{
			var identity = new ClaimsIdentity();
			ClaimsLogic.AddRecordClaims(identity, isAdmin, permissions, isGroupAdmin, roles ?? NoRoles);
			return identity;
		}

		private static bool Has(ClaimsIdentity identity, string resource, string action)
		{
			return identity.HasClaim(resource, action);
		}

		private static Permission Row(PermissionTypes type, PermissionActions action, string data = null, bool lockToGroup = false)
		{
			return new Permission { PermissionType = (int)type, Action = (int)action, Data = data, LockToGroup = lockToGroup };
		}

		#region No-row fall-through equals today's Logs behavior

		[Test]
		public void No_rows_member_gets_view_create_finalize_export_void_and_legacy_view_only()
		{
			var id = Derive(isAdmin: false, isGroupAdmin: false, permissions: null);

			// Everyone-defaults: matches AddLogClaims / AddDeleteLogClaims fall-through today.
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View).Should().BeTrue();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create).Should().BeTrue();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Finalize).Should().BeTrue();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Export).Should().BeTrue();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Void).Should().BeTrue(
				"AddDeleteLogClaims grants Delete to everyone when no DeleteLog row exists; parity keeps that");
			Has(id, ResgridClaimTypes.Resources.RecordLegacy, ResgridClaimTypes.Actions.View).Should().BeTrue();

			// Admin-or-group-admin defaults.
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Amend).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Reassign).Should().BeFalse();

			// Department-admin-only defaults.
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Approve).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Submit).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Share).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordDefinition, ResgridClaimTypes.Actions.Update).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordDefinition, ResgridClaimTypes.Actions.Publish).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordReport, ResgridClaimTypes.Actions.Update).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordDisclosure, ResgridClaimTypes.Actions.Update).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordLegalHold, ResgridClaimTypes.Actions.Update).Should().BeFalse();
		}

		[Test]
		public void No_rows_group_admin_additionally_gets_review_amend_and_reassign()
		{
			var id = Derive(isAdmin: false, isGroupAdmin: true);

			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeTrue();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Amend).Should().BeTrue();
			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Reassign).Should().BeTrue();

			Has(id, ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Approve).Should().BeFalse();
			Has(id, ResgridClaimTypes.Resources.RecordRestricted, ResgridClaimTypes.Actions.View).Should().BeFalse();
		}

		[Test]
		public void No_rows_department_admin_gets_every_claim()
		{
			var id = Derive(isAdmin: true, isGroupAdmin: false);

			foreach (var descriptor in RecordPermissionCatalog.All)
			{
				foreach (var grant in ClaimsLogic.RecordClaimGrants(descriptor.Type))
					Has(id, grant.Resource, grant.Action).Should().BeTrue($"{descriptor.Type} must be granted to a department admin");
			}
		}

		[Test]
		public void Empty_permission_list_behaves_exactly_like_null()
		{
			var fromNull = Derive(false, true, null).Claims.Select(c => c.Type + ":" + c.Value).OrderBy(x => x).ToList();
			var fromEmpty = Derive(false, true, new List<Permission>()).Claims.Select(c => c.Type + ":" + c.Value).OrderBy(x => x).ToList();

			fromEmpty.Should().Equal(fromNull);
		}

		#endregion

		#region Configured rows

		[Test]
		public void Row_department_admins_only_withholds_from_group_admins_and_members()
		{
			var rows = new List<Permission> { Row(PermissionTypes.CreateRecord, PermissionActions.DepartmentAdminsOnly) };

			Has(Derive(false, false, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			Has(Derive(false, true, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			Has(Derive(true, false, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create).Should().BeTrue();

			// View is issued regardless, like Log:View today.
			Has(Derive(false, false, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.View).Should().BeTrue();
		}

		[Test]
		public void Row_select_roles_uses_the_role_id_csv()
		{
			var rows = new List<Permission> { Row(PermissionTypes.ReviewRecords, PermissionActions.DepartmentAdminsAndSelectRoles, "4, 9") };
			var inRole = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 9 } };
			var otherRole = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 2 } };

			Has(Derive(false, false, rows, inRole), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeTrue();
			Has(Derive(false, false, rows, otherRole), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeFalse();
			// Value 2 deliberately excludes group admins (registry section 4.4).
			Has(Derive(false, true, rows, otherRole), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeFalse();
		}

		[Test]
		public void Row_department_and_group_admins_and_select_roles_includes_group_admins()
		{
			var rows = new List<Permission> { Row(PermissionTypes.ReviewRecords, PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, "9") };
			var inRole = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 9 } };

			Has(Derive(false, true, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeTrue();
			Has(Derive(false, false, rows, inRole), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeTrue();
			Has(Derive(false, false, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeFalse();
		}

		[Test]
		public void Malformed_role_csv_is_treated_as_empty_not_thrown()
		{
			var rows = new List<Permission> { Row(PermissionTypes.ReviewRecords, PermissionActions.DepartmentAdminsAndSelectRoles, "abc,,7x") };
			var roles = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 7 } };

			var act = () => Derive(false, false, rows, roles);
			act.Should().NotThrow();
			Has(act(), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Review).Should().BeFalse();
		}

		[Test]
		public void Unknown_future_action_value_denies_non_admins_and_still_allows_admins()
		{
			var rows = new List<Permission> { new Permission { PermissionType = (int)PermissionTypes.CreateRecord, Action = 99 } };

			Has(Derive(false, true, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create).Should().BeFalse();
			Has(Derive(true, false, rows), ResgridClaimTypes.Resources.Record, ResgridClaimTypes.Actions.Create).Should().BeTrue();
		}

		[Test]
		public void View_group_records_issues_no_claim_because_it_is_evaluated_per_record()
		{
			ClaimsLogic.RecordClaimGrants(PermissionTypes.ViewGroupRecords).Should().BeEmpty();

			var rows = new List<Permission> { Row(PermissionTypes.ViewGroupRecords, PermissionActions.DepartmentAdminsOnly, lockToGroup: true) };
			var before = Derive(false, false).Claims.Count();
			var after = Derive(false, false, rows).Claims.Count();
			after.Should().Be(before, "a ViewGroupRecords row must not change the claim set");
		}

		[Test]
		public void Claims_are_not_duplicated_when_derived_twice()
		{
			var identity = new ClaimsIdentity();
			ClaimsLogic.AddRecordClaims(identity, true, null, false, NoRoles);
			var once = identity.Claims.Count();
			ClaimsLogic.AddRecordClaims(identity, true, null, false, NoRoles);
			identity.Claims.Count().Should().Be(once);
		}

		#endregion

		#region Catalog integrity

		[Test]
		public void Catalog_covers_exactly_values_50_to_67_once_each()
		{
			var values = RecordPermissionCatalog.All.Select(d => (int)d.Type).OrderBy(v => v).ToList();
			values.Should().Equal(Enumerable.Range(RecordPermissionCatalog.FirstValue, 18));
		}

		[Test]
		public void Every_catalog_entry_except_view_group_records_issues_at_least_one_claim()
		{
			foreach (var descriptor in RecordPermissionCatalog.All)
			{
				var grants = ClaimsLogic.RecordClaimGrants(descriptor.Type);
				if (descriptor.Type == PermissionTypes.ViewGroupRecords)
					grants.Should().BeEmpty();
				else
					grants.Should().NotBeEmpty($"{descriptor.Type} needs a claim or the policy can never be satisfied");
			}
		}

		[TestCase(PermissionTypes.CreateRecord, PermissionActions.Everyone)]
		[TestCase(PermissionTypes.DeleteRecord, PermissionActions.Everyone)]
		[TestCase(PermissionTypes.FinalizeRecords, PermissionActions.Everyone)]
		[TestCase(PermissionTypes.ExportRecords, PermissionActions.Everyone)]
		[TestCase(PermissionTypes.ViewLegacyRecords, PermissionActions.Everyone)]
		[TestCase(PermissionTypes.ViewGroupRecords, PermissionActions.Everyone)]
		[TestCase(PermissionTypes.ReviewRecords, PermissionActions.DepartmentAndGroupAdmins)]
		[TestCase(PermissionTypes.AmendRecords, PermissionActions.DepartmentAndGroupAdmins)]
		[TestCase(PermissionTypes.ReassignRecordDrafts, PermissionActions.DepartmentAndGroupAdmins)]
		[TestCase(PermissionTypes.ApproveRecords, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.SubmitRecords, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.ShareRecordsExternally, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.ViewRestrictedRecords, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.ManageRecordDefinitions, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.PublishRecordDefinitions, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.ManageRecordReports, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.ManageRecordDisclosures, PermissionActions.DepartmentAdminsOnly)]
		[TestCase(PermissionTypes.ManageRecordLegalHold, PermissionActions.DepartmentAdminsOnly)]
		public void No_row_defaults_are_pinned(PermissionTypes type, PermissionActions expected)
		{
			RecordPermissionCatalog.Get(type).NoRowDefault.Should().Be(expected);
		}

		[Test]
		public void Activation_row_mapping_copies_create_log_to_create_and_finalize_and_delete_log_to_delete()
		{
			var map = RecordPermissionCatalog.ActivationRowMapping.ToDictionary(k => k.Key, k => k.Value);

			map[PermissionTypes.CreateLog].Should().BeEquivalentTo(new[] { PermissionTypes.CreateRecord, PermissionTypes.FinalizeRecords });
			map[PermissionTypes.DeleteLog].Should().BeEquivalentTo(new[] { PermissionTypes.DeleteRecord });
			map.Should().NotContainKey(PermissionTypes.ViewGroupUsers, "ViewGroupUsers is read as a suggestion, never applied silently");
		}

		#endregion
	}
}

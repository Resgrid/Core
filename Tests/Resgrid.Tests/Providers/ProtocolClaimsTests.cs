using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Providers.Claims;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// The P1 protocol claims (ClaimsLogic.AddProtocolClaims): every member holds Protocols:View; Author Protocols (128) gives
	/// Create and Update, Approve Protocols (129) gives Approve and Delete, Manage Protocol Acknowledgments (130) gives Assign,
	/// each following its permission row (admins always, then group admins, selected roles or everyone). With no row only
	/// department admins hold them, and the old two-argument overload behaves exactly like "no rows".
	/// </summary>
	[TestFixture]
	public class ProtocolClaimsTests
	{
		private const int AuthorRoleId = 7;
		private const int ApproverRoleId = 8;
		private const int AcknowledgerRoleId = 9;
		private const int OtherRoleId = 11;

		private static readonly string[] AllActions =
		{
			ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Create, ResgridClaimTypes.Actions.Update,
			ResgridClaimTypes.Actions.Approve, ResgridClaimTypes.Actions.Delete, ResgridClaimTypes.Actions.Assign
		};

		private static Permission Row(PermissionTypes type, PermissionActions action, string data = null) =>
			new Permission { PermissionType = (int)type, Action = (int)action, Data = data };

		private static List<PersonnelRole> InRoles(params int[] roleIds) =>
			roleIds.Select(id => new PersonnelRole { PersonnelRoleId = id, Name = "Role " + id }).ToList();

		private static ClaimsIdentity Claims(bool isAdmin, bool isGroupAdmin, List<PersonnelRole> roles, params Permission[] permissions)
		{
			var identity = new ClaimsIdentity();
			ClaimsLogic.AddProtocolClaims(identity, isAdmin, permissions.ToList(), isGroupAdmin, roles ?? new List<PersonnelRole>());
			return identity;
		}

		private static string[] ProtocolActions(ClaimsIdentity identity) =>
			identity.FindAll(ResgridClaimTypes.Resources.Protocols).Select(c => c.Value).ToArray();

		/// <summary>The three P1 rows, each for its own selected role.</summary>
		private static Permission[] SelectRoleRows() => new[]
		{
			Row(PermissionTypes.AuthorProtocols, PermissionActions.DepartmentAdminsAndSelectRoles, AuthorRoleId.ToString()),
			Row(PermissionTypes.ApproveProtocols, PermissionActions.DepartmentAdminsAndSelectRoles, ApproverRoleId.ToString()),
			Row(PermissionTypes.ManageProtocolAcknowledgments, PermissionActions.DepartmentAdminsAndSelectRoles, AcknowledgerRoleId.ToString())
		};

		[Test]
		public void A_department_admin_holds_every_protocol_claim_without_rows()
		{
			ProtocolActions(Claims(true, false, null)).Should().BeEquivalentTo(AllActions);
		}

		[Test]
		public void A_department_admin_holds_every_protocol_claim_even_when_the_rows_select_other_roles()
		{
			ProtocolActions(Claims(true, false, InRoles(OtherRoleId), SelectRoleRows())).Should().BeEquivalentTo(AllActions);
		}

		[Test]
		public void Without_rows_a_non_admin_gets_only_view()
		{
			ProtocolActions(Claims(false, false, InRoles(AuthorRoleId, ApproverRoleId))).Should().Equal(ResgridClaimTypes.Actions.View);
			ProtocolActions(Claims(false, true, null)).Should().Equal(new[] { ResgridClaimTypes.Actions.View }, "a group admin is not an author without a row that says so");
		}

		[Test]
		public void The_old_two_argument_overload_behaves_like_no_rows()
		{
			var member = new ClaimsIdentity();
			var admin = new ClaimsIdentity();

			ClaimsLogic.AddProtocolClaims(member, false);
			ClaimsLogic.AddProtocolClaims(admin, true);

			ProtocolActions(member).Should().Equal(ResgridClaimTypes.Actions.View);
			ProtocolActions(admin).Should().BeEquivalentTo(AllActions);
		}

		[Test]
		public void Author_protocols_row_gives_create_and_update_to_members_of_the_selected_role_only()
		{
			var author = Claims(false, false, InRoles(AuthorRoleId), SelectRoleRows());
			var other = Claims(false, false, InRoles(OtherRoleId), SelectRoleRows());

			ProtocolActions(author).Should().BeEquivalentTo(new[] { ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Create, ResgridClaimTypes.Actions.Update });
			ProtocolActions(other).Should().Equal(ResgridClaimTypes.Actions.View);
		}

		[Test]
		public void Approve_protocols_row_gives_approve_and_delete_but_not_authoring()
		{
			var approver = Claims(false, false, InRoles(ApproverRoleId), SelectRoleRows());

			ProtocolActions(approver).Should().BeEquivalentTo(new[] { ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Approve, ResgridClaimTypes.Actions.Delete });
		}

		[Test]
		public void Manage_protocol_acknowledgments_row_gives_assign_only()
		{
			var acknowledger = Claims(false, false, InRoles(AcknowledgerRoleId), SelectRoleRows());

			ProtocolActions(acknowledger).Should().BeEquivalentTo(new[] { ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Assign });
		}

		[Test]
		public void A_member_in_several_selected_roles_holds_each_rows_claims()
		{
			var both = Claims(false, false, InRoles(AuthorRoleId, ApproverRoleId), SelectRoleRows());

			ProtocolActions(both).Should().BeEquivalentTo(new[]
			{
				ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Create, ResgridClaimTypes.Actions.Update,
				ResgridClaimTypes.Actions.Approve, ResgridClaimTypes.Actions.Delete
			});
		}

		[Test]
		public void A_select_roles_row_lists_several_roles_separated_by_commas()
		{
			var row = Row(PermissionTypes.AuthorProtocols, PermissionActions.DepartmentAdminsAndSelectRoles, $"{OtherRoleId}, {AuthorRoleId}");

			ProtocolActions(Claims(false, false, InRoles(AuthorRoleId), row)).Should().Contain(ResgridClaimTypes.Actions.Create);
			ProtocolActions(Claims(false, false, InRoles(ApproverRoleId), row)).Should().NotContain(ResgridClaimTypes.Actions.Create);
		}

		[Test]
		public void A_group_admins_row_gives_group_admins_the_right_and_not_plain_members()
		{
			var row = Row(PermissionTypes.ApproveProtocols, PermissionActions.DepartmentAndGroupAdmins);

			ProtocolActions(Claims(false, true, null, row)).Should().Contain(new[] { ResgridClaimTypes.Actions.Approve, ResgridClaimTypes.Actions.Delete });
			ProtocolActions(Claims(false, false, null, row)).Should().Equal(ResgridClaimTypes.Actions.View);
		}

		[Test]
		public void An_everyone_row_gives_every_member_the_right()
		{
			var row = Row(PermissionTypes.AuthorProtocols, PermissionActions.Everyone);

			ProtocolActions(Claims(false, false, null, row)).Should().BeEquivalentTo(new[] { ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Create, ResgridClaimTypes.Actions.Update });
		}

		[Test]
		public void A_department_admins_only_row_gives_non_admins_nothing_more_than_view()
		{
			var rows = new[]
			{
				Row(PermissionTypes.AuthorProtocols, PermissionActions.DepartmentAdminsOnly),
				Row(PermissionTypes.ApproveProtocols, PermissionActions.DepartmentAdminsOnly),
				Row(PermissionTypes.ManageProtocolAcknowledgments, PermissionActions.DepartmentAdminsOnly)
			};

			ProtocolActions(Claims(false, true, InRoles(AuthorRoleId), rows)).Should().Equal(ResgridClaimTypes.Actions.View);
			ProtocolActions(Claims(true, false, null, rows)).Should().BeEquivalentTo(AllActions);
		}

		[Test]
		public void A_select_roles_row_with_no_roles_listed_gives_non_admins_nothing()
		{
			var row = Row(PermissionTypes.AuthorProtocols, PermissionActions.DepartmentAdminsAndSelectRoles, "");

			ProtocolActions(Claims(false, false, InRoles(AuthorRoleId), row)).Should().Equal(ResgridClaimTypes.Actions.View);
		}

		[Test]
		public void Rows_of_other_permission_types_do_not_grant_protocol_rights()
		{
			var unrelated = Row(PermissionTypes.CreateTraining, PermissionActions.Everyone);

			ProtocolActions(Claims(false, false, null, unrelated)).Should().Equal(ResgridClaimTypes.Actions.View);
		}

		[Test]
		public void Null_entries_in_the_rows_and_roles_are_ignored()
		{
			var identity = new ClaimsIdentity();
			var rows = new List<Permission> { null, Row(PermissionTypes.AuthorProtocols, PermissionActions.DepartmentAdminsAndSelectRoles, AuthorRoleId.ToString()) };
			var roles = new List<PersonnelRole> { null, new PersonnelRole { PersonnelRoleId = AuthorRoleId } };

			ClaimsLogic.AddProtocolClaims(identity, false, rows, false, roles);

			ProtocolActions(identity).Should().BeEquivalentTo(new[] { ResgridClaimTypes.Actions.View, ResgridClaimTypes.Actions.Create, ResgridClaimTypes.Actions.Update });
		}
	}
}

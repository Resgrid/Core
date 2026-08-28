using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Pins the ADP no-row permission defaults. Resgrid's convention is "missing row = allowed";
	/// protected data deliberately inverts that, and enforcement + the Security &amp; Permissions
	/// admin page both read this map — a change here changes who can reach protected data in every
	/// department that never touched the settings.
	/// </summary>
	[TestFixture]
	public class AdpPermissionDefaultsTests
	{
		[TestCase(PermissionTypes.ViewProtectedCallData, PermissionActions.Everyone,
			TestName = "View_call_defaults_to_everyone_so_responders_can_read_a_dispatch")]
		[TestCase(PermissionTypes.EditProtectedCallData, PermissionActions.Everyone,
			TestName = "Edit_call_defaults_to_everyone_to_match_call_workflow")]
		[TestCase(PermissionTypes.ViewProtectedOperationalData, PermissionActions.DepartmentAndGroupAdmins,
			TestName = "Operational_data_defaults_to_department_and_group_admins")]
		[TestCase(PermissionTypes.ManageDepartmentDataProtection, PermissionActions.DepartmentAdminsOnly,
			TestName = "Settings_management_defaults_to_department_admins")]
		[TestCase(PermissionTypes.ViewProtectedPersonnelData, PermissionActions.DepartmentAdminsOnly,
			TestName = "Personnel_PII_defaults_to_department_admins")]
		[TestCase(PermissionTypes.ViewProtectedContactData, PermissionActions.DepartmentAdminsOnly,
			TestName = "Contact_PII_defaults_to_department_admins")]
		[TestCase(PermissionTypes.ExportProtectedData, PermissionActions.DepartmentAdminsOnly,
			TestName = "Export_defaults_to_department_admins")]
		[TestCase(PermissionTypes.ConfigureProtectedDataEgress, PermissionActions.DepartmentAdminsOnly,
			TestName = "Egress_configuration_defaults_to_department_admins")]
		[TestCase(PermissionTypes.BreakGlassProtectedData, PermissionActions.DepartmentAdminsOnly,
			TestName = "Break_glass_defaults_to_department_admins")]
		public void Adp_no_row_defaults_are_pinned(PermissionTypes type, PermissionActions expected)
		{
			AdpPermissionDefaults.For(type).Should().Be(expected);
		}

		[Test]
		public void Every_adp_permission_value_has_a_default_and_nothing_else_does()
		{
			// 31-39 are the ADP block per the identifier allocation registry.
			for (var value = 31; value <= 39; value++)
			{
				var act = () => AdpPermissionDefaults.For((PermissionTypes)value);
				act.Should().NotThrow($"PermissionTypes value {value} is an ADP permission and needs a default");
			}

			var nonAdp = () => AdpPermissionDefaults.For(PermissionTypes.CreateCall);
			nonAdp.Should().Throw<ArgumentOutOfRangeException>(
				"non-ADP permissions must use the standard wide-open evaluation, never this map");
		}
	}
}

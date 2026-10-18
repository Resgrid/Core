using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Onboarding;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// Pins the Department Import identifiers to the Identifier Allocation Registry section 4A (wizard plan section 4.3).
	/// They are persisted values: a failure here means a number moved, which must be fixed in the registry first.
	/// </summary>
	[TestFixture]
	public class DepartmentImportIdentifierTests
	{
		[Test]
		public void Permissions_72_and_73_are_the_registry_names()
		{
			((int)PermissionTypes.ManageDepartmentImport).Should().Be(72);
			((int)PermissionTypes.ManageRecordImports).Should().Be(73);
		}

		[Test]
		public void Department_setting_83_is_the_import_config_and_84_stays_reserved()
		{
			((int)DepartmentSettingTypes.DepartmentImportConfig).Should().Be(83);
			Enum.IsDefined(typeof(DepartmentSettingTypes), 84).Should().BeFalse("84 is reserved for Phase 4 connector configuration");
		}

		[Test]
		public void Notification_event_types_50_and_51_are_targeted_and_not_offered_in_settings()
		{
			Enum.GetName(typeof(EventTypes), 50).Should().Be("DepartmentImportRunFinished");
			Enum.GetName(typeof(EventTypes), 51).Should().Be("DepartmentImportSupportMessage");

			foreach (var name in new[] { "DepartmentImportRunFinished", "DepartmentImportSupportMessage" })
				typeof(EventTypes).GetField(name).GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), false)
					.Should().NotBeEmpty($"{name} is targeted at the workspace owner, not a department broadcast");
		}

		[Test]
		public void Persisted_values_are_the_registry_values()
		{
			((int)DepartmentOperationLockType.DepartmentImport).Should().Be(2);
			((int)CallSources.Imported).Should().Be(5);
			((int)ImportActivationState.Dormant).Should().Be(1);
			((int)ImportActivationState.Invited).Should().Be(2);
			((int)ImportActivationState.Activated).Should().Be(3);

			var member = typeof(DepartmentMember).GetProperty(nameof(DepartmentMember.ImportActivationState));
			member.GetCustomAttributes(typeof(ProtoBuf.ProtoMemberAttribute), false).Cast<ProtoBuf.ProtoMemberAttribute>().Single().Tag.Should().Be(14);
			member.GetCustomAttributes(typeof(System.ComponentModel.DefaultValueAttribute), false).Should().NotBeEmpty();
		}

		[Test]
		public void Worker_commands_47_and_48_are_registered()
		{
			var path = FindRepositoryFile(Path.Combine("Workers", "Resgrid.Workers.Console", "Program.cs"));
			if (path == null)
				Assert.Inconclusive("Worker host Program.cs not found relative to the test assembly.");
			var program = System.IO.File.ReadAllText(path);

			program.Should().Contain("new Commands.DepartmentImportCommand(47)");
			program.Should().Contain("new Commands.DepartmentImportMaintenanceCommand(48)");
		}

		[Test]
		public void Feature_flags_are_the_registry_keys()
		{
			FeatureFlagKeys.OnboardingMigrationWizard.Should().Be("Onboarding.MigrationWizard");
			FeatureFlagKeys.OnboardingOperationsData.Should().Be("Onboarding.OperationsData");
			FeatureFlagKeys.OnboardingReadinessData.Should().Be("Onboarding.ReadinessData");
			FeatureFlagKeys.OnboardingOperationalHistory.Should().Be("Onboarding.OperationalHistory");
			FeatureFlagKeys.OnboardingHistoricalRecords.Should().Be("Onboarding.HistoricalRecords");
			FeatureFlagKeys.OnboardingVendorPacks.Should().Be("Onboarding.VendorPacks");
			FeatureFlagKeys.OnboardingDepartmentPackage.Should().Be("Onboarding.DepartmentPackage");
			FeatureFlagKeys.OnboardingManagedMigration.Should().Be("Onboarding.ManagedMigration");
		}

		[Test]
		public void Department_import_permissions_are_admin_only_and_never_offered_on_the_security_screen()
		{
			DepartmentImportPermissionCatalog.All.Select(d => d.Type)
				.Should().BeEquivalentTo(new[] { PermissionTypes.ManageDepartmentImport, PermissionTypes.ManageRecordImports });

			foreach (var descriptor in DepartmentImportPermissionCatalog.All)
			{
				descriptor.NoRowDefault.Should().Be(PermissionActions.DepartmentAdminsOnly);
				descriptor.ShownOnSecurityScreen.Should().BeFalse();
				descriptor.EveryoneOffered.Should().BeFalse();
			}
		}

		[Test]
		public void Every_import_table_is_in_the_department_purge()
		{
			var cleanupTables = typeof(Resgrid.Repositories.DataRepository.DepartmentImportDepartmentCleanup)
				.GetField("Tables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
				.GetValue(null) as string[];

			cleanupTables.Should().BeEquivalentTo(DepartmentImportTables.All.Values);
		}

		private static string FindRepositoryFile(string relativePath)
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = Path.Combine(directory.FullName, relativePath);
				if (System.IO.File.Exists(candidate))
					return candidate;
				directory = directory.Parent;
			}

			return null;
		}
	}
}

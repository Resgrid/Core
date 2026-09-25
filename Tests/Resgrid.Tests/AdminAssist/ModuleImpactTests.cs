using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class ModuleImpactTests
	{
		private readonly AdminAssistActor _actor = new(7, "admin");
		private sealed class Fixture
		{
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Repository = new();
			public readonly Mock<IDepartmentSettingsRepository> Settings = new();
			public readonly Mock<IModuleImpactStore> Counts = new();
			public ModuleImpactService Service => new(Access.Object, Repository.Object, new ConfigurationCatalog(), Settings.Object, Counts.Object, TimeProvider.System);
			public Fixture()
			{
				Access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<DepartmentSetting>());
				Counts.Setup(c => c.ReadModuleImpactCountsAsync(7, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ModuleImpactCounts(8, 3));
			}
		}
		[Test]
		public async Task Hiding_navigation_preserves_stored_data_and_counts_current_member_impact()
		{
			var f = new Fixture(); var report = await f.Service.PreviewAsync(_actor, new("0", "Documents", true));
			var menus = report.Metrics.Single(m => m.LabelKey == "Impact.ModuleMenus");
			Assert.That(menus.Before, Is.EqualTo(8)); Assert.That(menus.After, Is.Zero);
			var stored = report.Metrics.Single(m => m.LabelKey == "Impact.ModuleDataRows");
			Assert.That(stored.Before, Is.EqualTo(3)); Assert.That(stored.After, Is.EqualTo(3));
			Assert.That(report.LimitKeys, Does.Contain("Impact.ModuleTiming"));
			f.Settings.Verify(s => s.GetAllByDepartmentIdAsync(7), Times.Exactly(2)); f.Settings.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Enabling_a_hidden_module_restores_its_entry_without_claiming_to_create_data()
		{
			var f = new Fixture();
			f.Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentSetting { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.ModuleSettings,
				Setting = ObjectSerialization.Serialize(new DepartmentModuleSettings { DocumentsDisabled = true }) } });
			var report = await f.Service.PreviewAsync(_actor, new("0", "Documents", false));
			var menus = report.Metrics.Single(m => m.LabelKey == "Impact.ModuleMenus");
			Assert.That(menus.Before, Is.Zero); Assert.That(menus.After, Is.EqualTo(8));
		}
		[Test]
		public async Task Uncounted_derived_module_data_stays_unknown_while_verified_menu_effect_is_shown()
		{
			var f = new Fixture(); f.Counts.Setup(c => c.ReadModuleImpactCountsAsync(7, "Mapping", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ModuleImpactCounts(8, null));
			var report = await f.Service.PreviewAsync(_actor, new("0", "Mapping", true));
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.ModuleDataRows").State, Is.EqualTo(EvidenceState.Unknown));
			Assert.That(report.Metrics.Single(m => m.LabelKey == "Impact.ModuleMenus").State, Is.EqualTo(EvidenceState.Known));
		}
		[Test]
		public async Task Missing_source_is_unknown_and_never_becomes_an_empty_department()
		{
			var f = new Fixture(); f.Counts.Setup(c => c.ReadModuleImpactCountsAsync(7, "Documents", It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
			var report = await f.Service.PreviewAsync(_actor, new("0", "Documents", true));
			Assert.That(report.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown)); Assert.That(report.Metrics.Single().After, Is.Null);
		}
		[Test]
		public void Concurrent_data_or_access_change_invalidates_the_preview()
		{
			var f = new Fixture(); f.Counts.SetupSequence(c => c.ReadModuleImpactCountsAsync(7, "Documents", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ModuleImpactCounts(8, 3)).ReturnsAsync(new ModuleImpactCounts(8, 4));
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(_actor, new("0", "Documents", true)));
			f = new Fixture(); f.Access.SetupSequence(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(_actor, new("0", "Documents", true)));
		}
		[TestCase("BusinessOperations")][TestCase("Maintenance")][TestCase("Checklists")][TestCase("Notes; DELETE")]
		public void Unreviewed_and_arbitrary_modules_are_rejected_before_reading_sources(string module)
		{
			var f = new Fixture(); Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(_actor, new("0", module, true)));
			f.Settings.VerifyNoOtherCalls(); f.Counts.VerifyNoOtherCalls();
		}
	}
}

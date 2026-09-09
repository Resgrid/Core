using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public partial class ChecklistWorkflowTests
	{
		[Test, NonParallelizable]
		public async Task Managed_flag_and_module_commands_persist_pauses_and_resume_in_the_same_transaction()
		{
			var oldEnabled = FeatureFlagsConfig.FeatureFlagsEnabled; var oldCache = SystemBehaviorConfig.CacheEnabled;
			FeatureFlagsConfig.FeatureFlagsEnabled = true; SystemBehaviorConfig.CacheEnabled = true;
			try
			{
				var setup = await Scheduled(); await _service.SweepSchedulesAsync(setup.Clock.Now.UtcDateTime);
				var flag = new FeatureFlag { FeatureFlagId = 19, FlagKey = FeatureFlagKeys.ChecklistsSystem, IsEnabledGlobally = true };
				var flags = new Mock<IFeatureFlagRepository>();
				flags.Setup(r => r.GetAllAsync()).ReturnsAsync(() => new[] { JsonConvert.DeserializeObject<FeatureFlag>(JsonConvert.SerializeObject(flag)) });
				flags.Setup(r => r.SaveOrUpdateAsync(It.IsAny<FeatureFlag>(), It.IsAny<CancellationToken>(), false)).ReturnsAsync((FeatureFlag row, CancellationToken _, bool first) => flag = row);
				DepartmentSetting modules = null; var settings = new Mock<IDepartmentSettingsRepository>();
				settings.Setup(r => r.GetDepartmentSettingByIdTypeAsync(77, DepartmentSettingTypes.ModuleSettings)).ReturnsAsync(() => modules);
				settings.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), false)).ReturnsAsync((DepartmentSetting row, CancellationToken _, bool first) => modules = row);
				settings.Setup(r => r.DeleteAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => { modules = null; return true; });
				var observer = new ChecklistAccessMutationObserver(_store, settings.Object, setup.Clock); var cache = new Mock<ICacheProvider>();
				var service = new FeatureToggleService(flags.Object, Mock.Of<IFeatureFlagOverrideRepository>(), Mock.Of<IFeatureFlagTargetingRuleRepository>(), Mock.Of<IFeatureFlagPrerequisiteRepository>(), Mock.Of<IFeatureFlagUsageRepository>(), cache.Object, Mock.Of<IEventAggregator>(), Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentsService>(), _uow.Object, observer);
				await service.SetGlobalEnabledAsync(flag.FlagKey, false, "author");
				(await _store.GetAsync<Resgrid.Model.Checklists.ChecklistSchedule>(77, setup.Input.Id)).IsSuspended.Should().BeTrue();
				setup.Clock.Now = setup.Clock.Now.AddDays(3); await service.SetGlobalEnabledAsync(flag.FlagKey, true, "author");
				var resumed = await _store.GetAsync<Resgrid.Model.Checklists.ChecklistSchedule>(77, setup.Input.Id); resumed.ActiveFromUtc.Should().Be(setup.Clock.Now.UtcDateTime); resumed.IsSuspended.Should().BeFalse();
				var moduleService = new DepartmentSettingsService(settings.Object, Mock.Of<IAddressService>(), Mock.Of<IGeoLocationProvider>(), cache.Object, _uow.Object, observer, new Lazy<IFeatureToggleService>(() => service));
				await moduleService.SaveOrUpdateSettingAsync(77, Resgrid.Framework.ObjectSerialization.Serialize(new DepartmentModuleSettings { ChecklistsDisabled = true }), DepartmentSettingTypes.ModuleSettings);
				(await _store.GetAsync<Resgrid.Model.Checklists.ChecklistSchedule>(77, setup.Input.Id)).IsSuspended.Should().BeTrue();
				setup.Clock.Now = setup.Clock.Now.AddHours(1); await moduleService.DeleteSettingAsync(77, DepartmentSettingTypes.ModuleSettings);
				(await _store.GetAsync<Resgrid.Model.Checklists.ChecklistSchedule>(77, setup.Input.Id)).ActiveFromUtc.Should().Be(setup.Clock.Now.UtcDateTime);
				cache.Invocations.Should().NotContain(c => c.Method.Name.StartsWith("Retrieve"), "policy evaluation during a mutation must bypass every cached layer");
				_uow.Verify(u => u.CommitChanges(), Times.AtLeast(4));
			}
			finally { FeatureFlagsConfig.FeatureFlagsEnabled = oldEnabled; SystemBehaviorConfig.CacheEnabled = oldCache; }
		}
		[Test]
		public async Task Mutation_observer_failure_aborts_flag_write_without_cache_invalidation_or_audit_publication()
		{
			var observer = new Mock<IFeatureFlagMutationObserver>(); observer.Setup(o => o.AfterChangeAsync(It.IsAny<Func<string, int, Task<bool>>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("Storage unavailable"));
			var flags = new Mock<IFeatureFlagRepository>(); var cache = new Mock<ICacheProvider>(); var events = new Mock<IEventAggregator>();
			var service = new FeatureToggleService(flags.Object, Mock.Of<IFeatureFlagOverrideRepository>(), Mock.Of<IFeatureFlagTargetingRuleRepository>(), Mock.Of<IFeatureFlagPrerequisiteRepository>(), Mock.Of<IFeatureFlagUsageRepository>(), cache.Object, events.Object, Mock.Of<ISubscriptionsService>(), Mock.Of<IDepartmentsService>(), _uow.Object, observer.Object);
			Func<Task> command = () => service.SetGlobalEnabledAsync(FeatureFlagKeys.ChecklistsSystem, false, "author"); await command.Should().ThrowAsync<InvalidOperationException>();
			_uow.Verify(u => u.DiscardChanges(), Times.Once); _uow.Verify(u => u.CommitChanges(), Times.Never); cache.Invocations.Should().BeEmpty(); events.Invocations.Should().BeEmpty(); flags.Invocations.Should().BeEmpty();
		}
	}
}

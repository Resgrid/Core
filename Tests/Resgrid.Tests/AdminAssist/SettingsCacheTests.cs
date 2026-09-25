using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture, NonParallelizable]
	public class SettingsCacheTests
	{
		[Test]
		public async Task Shared_save_and_delete_invalidate_the_records_cached_getter()
		{
			var previous = Resgrid.Config.SystemBehaviorConfig.CacheEnabled;
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = true;
			try
			{
				DepartmentSetting persisted = new() { DepartmentId = 7, SettingType = (int)DepartmentSettingTypes.RecordsReviewDueHours, Setting = "72" };
				var repository = new Mock<IDepartmentSettingsRepository>();
				repository.Setup(r => r.GetDepartmentSettingByIdTypeAsync(7, DepartmentSettingTypes.RecordsReviewDueHours)).ReturnsAsync(() => persisted);
				repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((DepartmentSetting row, CancellationToken _, bool firstLevelOnly) => persisted = row);
				repository.Setup(r => r.DeleteAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => { persisted = null; return true; });
				var values = new Dictionary<string, string>(); var cache = new Mock<ICacheProvider>();
				cache.Setup(c => c.RetrieveAsync(It.IsAny<string>(), It.IsAny<Func<Task<string>>>(), It.IsAny<TimeSpan>()))
					.Returns(async (string key, Func<Task<string>> read, TimeSpan _) => values.TryGetValue(key, out var value) ? value : values[key] = await read());
				cache.Setup(c => c.RemoveAsync(It.IsAny<string>())).ReturnsAsync((string key) => values.Remove(key));
				var service = new DepartmentSettingsService(repository.Object, Mock.Of<IAddressService>(), Mock.Of<IGeoLocationProvider>(), cache.Object);
				Assert.That(await service.GetRecordsReviewDueHoursAsync(7), Is.EqualTo(72));
				await service.SaveOrUpdateSettingAsync(7, "24", DepartmentSettingTypes.RecordsReviewDueHours);
				Assert.That(await service.GetRecordsReviewDueHoursAsync(7), Is.EqualTo(24));
				await service.DeleteSettingAsync(7, DepartmentSettingTypes.RecordsReviewDueHours);
				Assert.That(await service.GetRecordsReviewDueHoursAsync(7), Is.EqualTo(DepartmentSettingsService.DefaultRecordsReviewDueHours));
			}
			finally { Resgrid.Config.SystemBehaviorConfig.CacheEnabled = previous; }
		}
	}
}

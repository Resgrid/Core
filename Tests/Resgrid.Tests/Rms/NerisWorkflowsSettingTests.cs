using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Neris;
using Resgrid.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>Department setting 111: the Records Settings switch that turns a department's NERIS workflows off.</summary>
	[TestFixture, NonParallelizable]
	public class NerisWorkflowsSettingTests
	{
		private const int Dept = 7;
		private DepartmentSetting _row;
		private Mock<IDepartmentSettingsRepository> _repository;
		private DepartmentSettingsService _settings;
		private bool _previousCache;
		private bool _previousNeris;

		[SetUp]
		public void SetUp()
		{
			_previousCache = SystemBehaviorConfig.CacheEnabled;
			_previousNeris = NerisConfig.Enabled;
			SystemBehaviorConfig.CacheEnabled = false;
			NerisConfig.Enabled = true;

			_row = null;
			_repository = new Mock<IDepartmentSettingsRepository>();
			_repository.Setup(r => r.GetDepartmentSettingByIdTypeAsync(Dept, DepartmentSettingTypes.RecordsNerisWorkflowsEnabled)).ReturnsAsync(() => _row);
			_repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentSetting row, CancellationToken _, bool firstLevelOnly) => _row = row);
			_settings = new DepartmentSettingsService(_repository.Object, Mock.Of<IAddressService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<ICacheProvider>());
		}

		[TearDown]
		public void TearDown()
		{
			SystemBehaviorConfig.CacheEnabled = _previousCache;
			NerisConfig.Enabled = _previousNeris;
		}

		[TestCase(null, true)]
		[TestCase("", true)]
		[TestCase("not-a-bool", true)]
		[TestCase("True", true)]
		[TestCase("false", false)]
		[TestCase("False", false)]
		public async Task Only_an_explicit_false_turns_NERIS_workflows_off(string stored, bool expected)
		{
			_row = stored == null ? null : new DepartmentSetting { DepartmentId = Dept, SettingType = (int)DepartmentSettingTypes.RecordsNerisWorkflowsEnabled, Setting = stored };

			(await _settings.GetRecordsNerisWorkflowsEnabledAsync(Dept)).Should().Be(expected);
		}

		[Test]
		public async Task Saving_the_switch_round_trips()
		{
			await _settings.SetRecordsNerisWorkflowsEnabledAsync(Dept, false);
			(await _settings.GetRecordsNerisWorkflowsEnabledAsync(Dept)).Should().BeFalse();
			_row.SettingType.Should().Be((int)DepartmentSettingTypes.RecordsNerisWorkflowsEnabled);

			await _settings.SetRecordsNerisWorkflowsEnabledAsync(Dept, true);
			(await _settings.GetRecordsNerisWorkflowsEnabledAsync(Dept)).Should().BeTrue();
		}

		[Test]
		public async Task A_fully_configured_profile_cannot_submit_while_the_department_has_NERIS_workflows_off()
		{
			var profiles = new Mock<IRmsNerisProfilesRepository>();
			profiles.Setup(p => p.GetByDepartmentIdAsync(Dept)).ReturnsAsync(new RmsNerisProfile
			{
				DepartmentId = Dept, NerisEntityId = "FD24027000", IsEnabled = true, EncryptedCredentialJson = "sealed"
			});
			var service = new NerisProfileService(profiles.Object, Mock.Of<IRmsNerisValueSetsRepository>(), Mock.Of<IRmsNerisCrosswalksRepository>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IEncryptionService>(), _settings);

			(await service.IsWorkflowEnabledAsync(Dept)).Should().BeTrue("a department that never saved the setting keeps NERIS");
			(await service.IsSubmissionEnabledAsync(Dept)).Should().BeTrue();

			await _settings.SetRecordsNerisWorkflowsEnabledAsync(Dept, false);

			(await service.IsWorkflowEnabledAsync(Dept)).Should().BeFalse();
			(await service.IsSubmissionEnabledAsync(Dept)).Should().BeFalse("the department switch gates every submission path, the worker included");
			profiles.Verify(p => p.UpdateAsync(It.IsAny<RmsNerisProfile>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never, "the profile is kept as it was");
		}
	}
}

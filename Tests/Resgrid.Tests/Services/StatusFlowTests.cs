using System.Collections.Generic;
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
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Status flow for the Unit and Responder apps: the next statuses a status option offers (CustomStateDetail
	/// NextStateDetailIds, v4 StatusResultData.NextIds) and department setting 114, hold to set status.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class StatusFlowTests
	{
		private const int Dept = 9;

		[Test]
		public void Next_statuses_parse_without_blanks_duplicates_or_the_option_itself()
		{
			var detail = new CustomStateDetail { CustomStateDetailId = 5, NextStateDetailIds = " 7, ,8,7,5,x,-3,9 " };

			detail.GetNextStateDetailIds().Should().Equal(7, 8, 9);
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  ")]
		public void An_option_without_next_statuses_has_no_restriction(string stored)
		{
			new CustomStateDetail { CustomStateDetailId = 5, NextStateDetailIds = stored }.GetNextStateDetailIds().Should().BeEmpty();
		}

		[Test]
		public void Saving_next_statuses_stores_a_clean_list_and_an_empty_list_clears_it()
		{
			var detail = new CustomStateDetail { CustomStateDetailId = 5 };

			detail.SetNextStateDetailIds(new[] { 8, 8, 5, 0, 12 });
			detail.NextStateDetailIds.Should().Be("8,12");

			detail.SetNextStateDetailIds(new List<int>());
			detail.NextStateDetailIds.Should().BeNull();

			detail.SetNextStateDetailIds(null);
			detail.NextStateDetailIds.Should().BeNull();
		}

		[Test]
		public void The_apps_only_get_next_statuses_that_are_still_in_the_list()
		{
			// "Departed" offers On Scene (11) and an option that has since been deleted (99).
			var departed = new CustomStateDetail { CustomStateDetailId = 10, CustomStateId = 3, ButtonText = "Vertrokken", ButtonColor = "#f0ad4e", NextStateDetailIds = "11,99" };

			var data = StatusesController.ConvertCustomStatusData((int)CustomStateTypes.Unit, departed, new HashSet<int> { 10, 11, 12 });

			data.Id.Should().Be(10);
			data.NextIds.Should().Equal(11);
		}

		[Test]
		public void A_status_without_next_statuses_sends_an_empty_list()
		{
			var available = new CustomStateDetail { CustomStateDetailId = 12, CustomStateId = 3, ButtonText = "Standplaats", ButtonColor = "#5cb85c" };

			StatusesController.ConvertCustomStatusData((int)CustomStateTypes.Unit, available, new HashSet<int> { 10, 11, 12 }).NextIds.Should().BeEmpty();
			StatusesController.ConvertCustomStatusData((int)CustomStateTypes.Unit, available).NextIds.Should().BeEmpty();
		}

		[TestFixture, NonParallelizable]
		public class HoldToConfirmSetting
		{
			private DepartmentSetting _row;
			private DepartmentSettingsService _settings;
			private bool _previousCache;

			[SetUp]
			public void SetUp()
			{
				_previousCache = SystemBehaviorConfig.CacheEnabled;
				SystemBehaviorConfig.CacheEnabled = false;

				_row = null;
				var repository = new Mock<IDepartmentSettingsRepository>();
				repository.Setup(r => r.GetDepartmentSettingByIdTypeAsync(Dept, DepartmentSettingTypes.StatusHoldToConfirm)).ReturnsAsync(() => _row);
				repository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DepartmentSetting>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentSetting row, CancellationToken _, bool firstLevelOnly) => _row = row);
				_settings = new DepartmentSettingsService(repository.Object, Mock.Of<IAddressService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<ICacheProvider>());
			}

			[TearDown]
			public void TearDown()
			{
				SystemBehaviorConfig.CacheEnabled = _previousCache;
			}

			[TestCase(null, false)]
			[TestCase("", false)]
			[TestCase("not-a-bool", false)]
			[TestCase("False", false)]
			[TestCase("True", true)]
			[TestCase("true", true)]
			public async Task Only_an_explicit_true_switches_the_apps_to_hold(string stored, bool expected)
			{
				_row = stored == null ? null : new DepartmentSetting { DepartmentId = Dept, SettingType = (int)DepartmentSettingTypes.StatusHoldToConfirm, Setting = stored };

				(await _settings.GetStatusHoldToConfirmAsync(Dept)).Should().Be(expected);
			}

			[Test]
			public async Task The_dispatch_settings_save_round_trips()
			{
				// DepartmentController.DispatchSettings saves the posted bool with ToString().
				await _settings.SaveOrUpdateSettingAsync(Dept, true.ToString(), DepartmentSettingTypes.StatusHoldToConfirm);
				(await _settings.GetStatusHoldToConfirmAsync(Dept)).Should().BeTrue();

				await _settings.SaveOrUpdateSettingAsync(Dept, false.ToString(), DepartmentSettingTypes.StatusHoldToConfirm);
				(await _settings.GetStatusHoldToConfirmAsync(Dept)).Should().BeFalse();
			}
		}
	}
}

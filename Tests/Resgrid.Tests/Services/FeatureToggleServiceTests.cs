using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace FeatureToggleServiceTests
	{
		public class with_the_feature_toggle_service : TestBase
		{
			protected Mock<IFeatureFlagRepository> _featureFlagRepositoryMock;
			protected IFeatureToggleService _featureToggleService;

			protected with_the_feature_toggle_service()
			{
				BuildService();
			}

			protected override void Before_all_tests()
			{
				BuildService();
			}

			private void BuildService()
			{
				_featureFlagRepositoryMock = new Mock<IFeatureFlagRepository>();

				_featureToggleService = new FeatureToggleService(
					_featureFlagRepositoryMock.Object,
					new Mock<IFeatureFlagOverrideRepository>().Object,
					new Mock<IFeatureFlagTargetingRuleRepository>().Object,
					new Mock<IFeatureFlagPrerequisiteRepository>().Object,
					new Mock<IFeatureFlagUsageRepository>().Object,
					new Mock<ICacheProvider>().Object,
					new Mock<IEventAggregator>().Object,
					new Mock<ISubscriptionsService>().Object,
					new Mock<IDepartmentsService>().Object);
			}
		}

		[TestFixture]
		public class when_the_flag_store_is_unavailable : with_the_feature_toggle_service
		{
			[Test]
			public async Task is_enabled_should_fail_shut_and_not_throw()
			{
				_featureFlagRepositoryMock.Setup(x => x.GetAllAsync()).ThrowsAsync(new InvalidOperationException("flag store down"));

				var result = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, 1);

				result.Should().BeFalse();
			}

			[Test]
			public async Task is_enabled_should_fail_shut_even_when_default_value_is_true()
			{
				_featureFlagRepositoryMock.Setup(x => x.GetAllAsync()).ThrowsAsync(new InvalidOperationException("flag store down"));

				// defaultValue answers "flag not defined", not "flag store down" — outages always read disabled.
				var result = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.ChatSystem, 1, defaultValue: true);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_a_flag_is_not_defined : with_the_feature_toggle_service
		{
			[Test]
			public async Task is_enabled_should_return_the_caller_default()
			{
				_featureFlagRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<FeatureFlag>());

				var result = await _featureToggleService.IsEnabledAsync("Tests.NoSuchFlag", 1, defaultValue: true);

				result.Should().BeTrue();
			}
		}
	}
}

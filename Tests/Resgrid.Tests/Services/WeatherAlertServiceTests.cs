using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace WeatherAlertServiceTests
	{
		public class with_the_weather_alert_service : TestBase
		{
			protected IWeatherAlertService _weatherAlertService;

			protected readonly Mock<IWeatherAlertRepository> _weatherAlertRepoMock;
			protected readonly Mock<IWeatherAlertSourceRepository> _weatherAlertSourceRepoMock;
			protected readonly Mock<IWeatherAlertZoneRepository> _weatherAlertZoneRepoMock;
			protected readonly Mock<IWeatherAlertProviderFactory> _providerFactoryMock;
			protected readonly Mock<IDepartmentSettingsRepository> _departmentSettingsRepoMock;
			protected readonly Mock<IDepartmentsService> _departmentsServiceMock;
			protected readonly Mock<IMessageService> _messageServiceMock;
			protected readonly Mock<ICacheProvider> _cacheProviderMock;
			protected readonly Mock<IEventAggregator> _eventAggregatorMock;

			protected const int TestDepartmentId = 500;
			protected readonly Guid TestSourceId = Guid.NewGuid();
			protected readonly Guid TestZoneId = Guid.NewGuid();

			protected with_the_weather_alert_service()
			{
				_weatherAlertRepoMock = new Mock<IWeatherAlertRepository>();
				_weatherAlertSourceRepoMock = new Mock<IWeatherAlertSourceRepository>();
				_weatherAlertZoneRepoMock = new Mock<IWeatherAlertZoneRepository>();
				_providerFactoryMock = new Mock<IWeatherAlertProviderFactory>();
				_departmentSettingsRepoMock = new Mock<IDepartmentSettingsRepository>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_messageServiceMock = new Mock<IMessageService>();
				_cacheProviderMock = new Mock<ICacheProvider>();
				_eventAggregatorMock = new Mock<IEventAggregator>();

				_weatherAlertService = new WeatherAlertService(
					_weatherAlertRepoMock.Object,
					_weatherAlertSourceRepoMock.Object,
					_weatherAlertZoneRepoMock.Object,
					_providerFactoryMock.Object,
					_departmentSettingsRepoMock.Object,
					_departmentsServiceMock.Object,
					_messageServiceMock.Object,
					_cacheProviderMock.Object,
					_eventAggregatorMock.Object);
			}
		}

		// ── Source CRUD ──────────────────────────────────────────────────────────

		[TestFixture]
		public class when_saving_a_new_source : with_the_weather_alert_service
		{
			[Test]
			public async Task should_assign_new_guid_and_created_date_for_new_source()
			{
				var source = new WeatherAlertSource
				{
					DepartmentId = TestDepartmentId,
					Name = "NWS Pacific Northwest",
					SourceType = (int)WeatherAlertSourceType.NationalWeatherService,
					PollIntervalMinutes = 15,
					Active = true
				};

				_weatherAlertSourceRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WeatherAlertSource s, CancellationToken _, bool __) => s);

				var result = await _weatherAlertService.SaveSourceAsync(source);

				result.Should().NotBeNull();
				result.WeatherAlertSourceId.Should().NotBe(Guid.Empty);
				result.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
			}

			[Test]
			public async Task should_not_overwrite_existing_guid_on_update()
			{
				var existingId = Guid.NewGuid();
				var source = new WeatherAlertSource
				{
					WeatherAlertSourceId = existingId,
					DepartmentId = TestDepartmentId,
					Name = "Updated Source",
					SourceType = (int)WeatherAlertSourceType.NationalWeatherService,
					PollIntervalMinutes = 30,
					Active = true,
					CreatedOn = DateTime.UtcNow.AddDays(-10)
				};

				_weatherAlertSourceRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WeatherAlertSource s, CancellationToken _, bool __) => s);

				var result = await _weatherAlertService.SaveSourceAsync(source);

				result.WeatherAlertSourceId.Should().Be(existingId);
			}
		}

		[TestFixture]
		public class when_getting_source_by_id : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_source_when_found()
			{
				var expected = new WeatherAlertSource
				{
					WeatherAlertSourceId = TestSourceId,
					DepartmentId = TestDepartmentId,
					Name = "Test Source"
				};

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(TestSourceId.ToString()))
					.ReturnsAsync(expected);

				var result = await _weatherAlertService.GetSourceByIdAsync(TestSourceId);

				result.Should().NotBeNull();
				result.WeatherAlertSourceId.Should().Be(TestSourceId);
				result.Name.Should().Be("Test Source");
			}

			[Test]
			public async Task should_return_null_when_source_not_found()
			{
				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(It.IsAny<string>()))
					.ReturnsAsync((WeatherAlertSource)null);

				var result = await _weatherAlertService.GetSourceByIdAsync(Guid.NewGuid());

				result.Should().BeNull();
			}
		}

		[TestFixture]
		public class when_getting_sources_by_department : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_sources_for_department()
			{
				var sources = new List<WeatherAlertSource>
				{
					new WeatherAlertSource { WeatherAlertSourceId = Guid.NewGuid(), DepartmentId = TestDepartmentId, Name = "Source A" },
					new WeatherAlertSource { WeatherAlertSourceId = Guid.NewGuid(), DepartmentId = TestDepartmentId, Name = "Source B" }
				};

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetSourcesByDepartmentIdAsync(TestDepartmentId))
					.ReturnsAsync(sources);

				var result = await _weatherAlertService.GetSourcesByDepartmentIdAsync(TestDepartmentId);

				result.Should().NotBeNull();
				result.Count.Should().Be(2);
			}

			[Test]
			public async Task should_return_empty_list_when_no_sources()
			{
				_weatherAlertSourceRepoMock
					.Setup(x => x.GetSourcesByDepartmentIdAsync(TestDepartmentId))
					.ReturnsAsync((IEnumerable<WeatherAlertSource>)null);

				var result = await _weatherAlertService.GetSourcesByDepartmentIdAsync(TestDepartmentId);

				result.Should().NotBeNull();
				result.Should().BeEmpty();
			}
		}

		[TestFixture]
		public class when_deleting_a_source : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_true_when_source_exists()
			{
				var source = new WeatherAlertSource
				{
					WeatherAlertSourceId = TestSourceId,
					DepartmentId = TestDepartmentId
				};

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(TestSourceId.ToString()))
					.ReturnsAsync(source);
				_weatherAlertSourceRepoMock
					.Setup(x => x.DeleteAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(true);

				var result = await _weatherAlertService.DeleteSourceAsync(TestSourceId);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_return_false_when_source_not_found()
			{
				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(It.IsAny<string>()))
					.ReturnsAsync((WeatherAlertSource)null);

				var result = await _weatherAlertService.DeleteSourceAsync(Guid.NewGuid());

				result.Should().BeFalse();
			}
		}

		// ── Alert Deduplication ──────────────────────────────────────────────────

		[TestFixture]
		public class when_processing_a_new_alert : with_the_weather_alert_service
		{
			[Test]
			public async Task should_insert_new_alert_when_external_id_not_found()
			{
				var sourceId = Guid.NewGuid();
				var source = new WeatherAlertSource
				{
					WeatherAlertSourceId = sourceId,
					DepartmentId = TestDepartmentId,
					SourceType = (int)WeatherAlertSourceType.NationalWeatherService,
					Active = true
				};

				var fetchedAlert = new WeatherAlert
				{
					WeatherAlertId = Guid.NewGuid(),
					DepartmentId = TestDepartmentId,
					WeatherAlertSourceId = sourceId,
					ExternalId = "NWS-ALERT-001",
					Event = "Tornado Warning",
					Severity = (int)WeatherAlertSeverity.Extreme
				};

				var providerMock = new Mock<IWeatherAlertProvider>();
				providerMock.Setup(p => p.FetchAlertsAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new List<WeatherAlert> { fetchedAlert });

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(sourceId.ToString()))
					.ReturnsAsync(source);
				_providerFactoryMock
					.Setup(x => x.GetProvider(WeatherAlertSourceType.NationalWeatherService))
					.Returns(providerMock.Object);
				_weatherAlertRepoMock
					.Setup(x => x.GetByExternalIdAndSourceIdAsync("NWS-ALERT-001", sourceId))
					.ReturnsAsync((WeatherAlert)null);
				_weatherAlertRepoMock
					.Setup(x => x.InsertAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync(fetchedAlert);
				_weatherAlertSourceRepoMock
					.Setup(x => x.UpdateAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync(source);

				await _weatherAlertService.ProcessWeatherAlertSourceAsync(sourceId);

				_weatherAlertRepoMock.Verify(x => x.InsertAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_weatherAlertRepoMock.Verify(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_processing_a_duplicate_alert : with_the_weather_alert_service
		{
			[Test]
			public async Task should_update_existing_alert_when_external_id_already_exists()
			{
				var sourceId = Guid.NewGuid();
				var source = new WeatherAlertSource
				{
					WeatherAlertSourceId = sourceId,
					DepartmentId = TestDepartmentId,
					SourceType = (int)WeatherAlertSourceType.NationalWeatherService,
					Active = true
				};

				var existingAlert = new WeatherAlert
				{
					WeatherAlertId = Guid.NewGuid(),
					DepartmentId = TestDepartmentId,
					WeatherAlertSourceId = sourceId,
					ExternalId = "NWS-ALERT-001",
					Event = "Tornado Warning",
					Severity = (int)WeatherAlertSeverity.Severe,
					Status = (int)WeatherAlertStatus.Active
				};

				var fetchedAlert = new WeatherAlert
				{
					WeatherAlertId = Guid.NewGuid(),
					DepartmentId = TestDepartmentId,
					WeatherAlertSourceId = sourceId,
					ExternalId = "NWS-ALERT-001",
					Event = "Tornado Warning",
					Severity = (int)WeatherAlertSeverity.Extreme,
					Headline = "Updated headline"
				};

				var providerMock = new Mock<IWeatherAlertProvider>();
				providerMock.Setup(p => p.FetchAlertsAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new List<WeatherAlert> { fetchedAlert });

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(sourceId.ToString()))
					.ReturnsAsync(source);
				_providerFactoryMock
					.Setup(x => x.GetProvider(WeatherAlertSourceType.NationalWeatherService))
					.Returns(providerMock.Object);
				_weatherAlertRepoMock
					.Setup(x => x.GetByExternalIdAndSourceIdAsync(It.IsAny<string>(), It.IsAny<Guid>()))
					.ReturnsAsync(existingAlert);
				_weatherAlertRepoMock
					.Setup(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync(existingAlert);
				_weatherAlertSourceRepoMock
					.Setup(x => x.UpdateAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync(source);

				await _weatherAlertService.ProcessWeatherAlertSourceAsync(sourceId);

				_weatherAlertRepoMock.Verify(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_weatherAlertRepoMock.Verify(x => x.InsertAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
				existingAlert.Severity.Should().Be((int)WeatherAlertSeverity.Extreme);
				existingAlert.Headline.Should().Be("Updated headline");
			}
		}

		// ── Alert Expiration ─────────────────────────────────────────────────────

		[TestFixture]
		public class when_expiring_old_alerts : with_the_weather_alert_service
		{
			[Test]
			public async Task should_mark_expired_alerts_with_expired_status()
			{
				var expiredAlert = new WeatherAlert
				{
					WeatherAlertId = Guid.NewGuid(),
					DepartmentId = TestDepartmentId,
					ExternalId = "NWS-EXPIRED-001",
					Status = (int)WeatherAlertStatus.Active,
					ExpiresUtc = DateTime.UtcNow.AddHours(-2)
				};

				_weatherAlertRepoMock
					.Setup(x => x.GetExpiredUnprocessedAlertsAsync())
					.ReturnsAsync(new List<WeatherAlert> { expiredAlert });
				_weatherAlertRepoMock
					.Setup(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WeatherAlert a, CancellationToken _, bool __) => a);

				await _weatherAlertService.ExpireOldAlertsAsync();

				expiredAlert.Status.Should().Be((int)WeatherAlertStatus.Expired);
				expiredAlert.LastUpdatedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
				_weatherAlertRepoMock.Verify(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			}

			[Test]
			public async Task should_handle_no_expired_alerts_gracefully()
			{
				_weatherAlertRepoMock
					.Setup(x => x.GetExpiredUnprocessedAlertsAsync())
					.ReturnsAsync((IEnumerable<WeatherAlert>)null);

				await _weatherAlertService.ExpireOldAlertsAsync();

				_weatherAlertRepoMock.Verify(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}
		}

		// ── Severity Threshold Filtering ─────────────────────────────────────────

		[TestFixture]
		public class when_filtering_by_severity : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_alerts_matching_max_severity()
			{
				var alerts = new List<WeatherAlert>
				{
					new WeatherAlert { WeatherAlertId = Guid.NewGuid(), DepartmentId = TestDepartmentId, Severity = (int)WeatherAlertSeverity.Extreme },
					new WeatherAlert { WeatherAlertId = Guid.NewGuid(), DepartmentId = TestDepartmentId, Severity = (int)WeatherAlertSeverity.Severe }
				};

				_weatherAlertRepoMock
					.Setup(x => x.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, (int)WeatherAlertSeverity.Severe))
					.ReturnsAsync(alerts);

				var result = await _weatherAlertService.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, WeatherAlertSeverity.Severe);

				result.Should().NotBeNull();
				result.Count.Should().Be(2);
			}

			[Test]
			public async Task should_return_empty_list_when_no_alerts_match_severity()
			{
				_weatherAlertRepoMock
					.Setup(x => x.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, (int)WeatherAlertSeverity.Extreme))
					.ReturnsAsync(new List<WeatherAlert>());

				var result = await _weatherAlertService.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, WeatherAlertSeverity.Extreme);

				result.Should().NotBeNull();
				result.Should().BeEmpty();
			}

			[Test]
			public async Task should_pass_correct_enum_value_to_repository()
			{
				_weatherAlertRepoMock
					.Setup(x => x.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, (int)WeatherAlertSeverity.Moderate))
					.ReturnsAsync(new List<WeatherAlert>());

				await _weatherAlertService.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, WeatherAlertSeverity.Moderate);

				_weatherAlertRepoMock.Verify(
					x => x.GetAlertsByDepartmentAndSeverityAsync(TestDepartmentId, (int)WeatherAlertSeverity.Moderate),
					Times.Once);
			}
		}

		// ── Zone CRUD ────────────────────────────────────────────────────────────

		[TestFixture]
		public class when_saving_a_new_zone : with_the_weather_alert_service
		{
			[Test]
			public async Task should_assign_new_guid_and_created_date_for_new_zone()
			{
				var zone = new WeatherAlertZone
				{
					DepartmentId = TestDepartmentId,
					Name = "Downtown Station Area",
					ZoneCode = "WAZ021",
					RadiusMiles = 10,
					IsActive = true
				};

				_weatherAlertZoneRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<WeatherAlertZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WeatherAlertZone z, CancellationToken _, bool __) => z);

				var result = await _weatherAlertService.SaveZoneAsync(zone);

				result.Should().NotBeNull();
				result.WeatherAlertZoneId.Should().NotBe(Guid.Empty);
				result.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
			}

			[Test]
			public async Task should_not_overwrite_existing_zone_guid_on_update()
			{
				var existingId = Guid.NewGuid();
				var zone = new WeatherAlertZone
				{
					WeatherAlertZoneId = existingId,
					DepartmentId = TestDepartmentId,
					Name = "Updated Zone",
					ZoneCode = "WAZ022",
					RadiusMiles = 15,
					IsActive = true,
					CreatedOn = DateTime.UtcNow.AddDays(-5)
				};

				_weatherAlertZoneRepoMock
					.Setup(x => x.SaveOrUpdateAsync(It.IsAny<WeatherAlertZone>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WeatherAlertZone z, CancellationToken _, bool __) => z);

				var result = await _weatherAlertService.SaveZoneAsync(zone);

				result.WeatherAlertZoneId.Should().Be(existingId);
			}
		}

		[TestFixture]
		public class when_getting_zone_by_id : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_zone_when_found()
			{
				var expected = new WeatherAlertZone
				{
					WeatherAlertZoneId = TestZoneId,
					DepartmentId = TestDepartmentId,
					Name = "Test Zone"
				};

				_weatherAlertZoneRepoMock
					.Setup(x => x.GetByIdAsync(TestZoneId.ToString()))
					.ReturnsAsync(expected);

				var result = await _weatherAlertService.GetZoneByIdAsync(TestZoneId);

				result.Should().NotBeNull();
				result.WeatherAlertZoneId.Should().Be(TestZoneId);
			}

			[Test]
			public async Task should_return_null_when_zone_not_found()
			{
				_weatherAlertZoneRepoMock
					.Setup(x => x.GetByIdAsync(It.IsAny<string>()))
					.ReturnsAsync((WeatherAlertZone)null);

				var result = await _weatherAlertService.GetZoneByIdAsync(Guid.NewGuid());

				result.Should().BeNull();
			}
		}

		[TestFixture]
		public class when_getting_zones_by_department : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_zones_for_department()
			{
				var zones = new List<WeatherAlertZone>
				{
					new WeatherAlertZone { WeatherAlertZoneId = Guid.NewGuid(), DepartmentId = TestDepartmentId, Name = "Zone A" },
					new WeatherAlertZone { WeatherAlertZoneId = Guid.NewGuid(), DepartmentId = TestDepartmentId, Name = "Zone B" }
				};

				_weatherAlertZoneRepoMock
					.Setup(x => x.GetZonesByDepartmentIdAsync(TestDepartmentId))
					.ReturnsAsync(zones);

				var result = await _weatherAlertService.GetZonesByDepartmentIdAsync(TestDepartmentId);

				result.Should().NotBeNull();
				result.Count.Should().Be(2);
			}

			[Test]
			public async Task should_return_empty_list_when_no_zones()
			{
				_weatherAlertZoneRepoMock
					.Setup(x => x.GetZonesByDepartmentIdAsync(TestDepartmentId))
					.ReturnsAsync((IEnumerable<WeatherAlertZone>)null);

				var result = await _weatherAlertService.GetZonesByDepartmentIdAsync(TestDepartmentId);

				result.Should().NotBeNull();
				result.Should().BeEmpty();
			}
		}

		[TestFixture]
		public class when_deleting_a_zone : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_true_when_zone_exists()
			{
				var zone = new WeatherAlertZone
				{
					WeatherAlertZoneId = TestZoneId,
					DepartmentId = TestDepartmentId
				};

				_weatherAlertZoneRepoMock
					.Setup(x => x.GetByIdAsync(TestZoneId.ToString()))
					.ReturnsAsync(zone);
				_weatherAlertZoneRepoMock
					.Setup(x => x.DeleteAsync(It.IsAny<WeatherAlertZone>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(true);

				var result = await _weatherAlertService.DeleteZoneAsync(TestZoneId);

				result.Should().BeTrue();
			}

			[Test]
			public async Task should_return_false_when_zone_not_found()
			{
				_weatherAlertZoneRepoMock
					.Setup(x => x.GetByIdAsync(It.IsAny<string>()))
					.ReturnsAsync((WeatherAlertZone)null);

				var result = await _weatherAlertService.DeleteZoneAsync(Guid.NewGuid());

				result.Should().BeFalse();
			}
		}

		// ── Processing inactive source ───────────────────────────────────────────

		[TestFixture]
		public class when_processing_an_inactive_source : with_the_weather_alert_service
		{
			[Test]
			public async Task should_skip_inactive_source()
			{
				var sourceId = Guid.NewGuid();
				var source = new WeatherAlertSource
				{
					WeatherAlertSourceId = sourceId,
					DepartmentId = TestDepartmentId,
					Active = false
				};

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(sourceId.ToString()))
					.ReturnsAsync(source);

				await _weatherAlertService.ProcessWeatherAlertSourceAsync(sourceId);

				_providerFactoryMock.Verify(
					x => x.GetProvider(It.IsAny<WeatherAlertSourceType>()), Times.Never);
			}

			[Test]
			public async Task should_skip_null_source()
			{
				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(It.IsAny<string>()))
					.ReturnsAsync((WeatherAlertSource)null);

				await _weatherAlertService.ProcessWeatherAlertSourceAsync(Guid.NewGuid());

				_providerFactoryMock.Verify(
					x => x.GetProvider(It.IsAny<WeatherAlertSourceType>()), Times.Never);
			}
		}

		// ── Reference cancellation ───────────────────────────────────────────────

		[TestFixture]
		public class when_processing_alerts_with_references : with_the_weather_alert_service
		{
			[Test]
			public async Task should_cancel_referenced_alert()
			{
				var sourceId = Guid.NewGuid();
				var source = new WeatherAlertSource
				{
					WeatherAlertSourceId = sourceId,
					DepartmentId = TestDepartmentId,
					SourceType = (int)WeatherAlertSourceType.NationalWeatherService,
					Active = true
				};

				var referencedAlert = new WeatherAlert
				{
					WeatherAlertId = Guid.NewGuid(),
					ExternalId = "NWS-ORIGINAL-001",
					Status = (int)WeatherAlertStatus.Active
				};

				var cancellingAlert = new WeatherAlert
				{
					WeatherAlertId = Guid.NewGuid(),
					DepartmentId = TestDepartmentId,
					WeatherAlertSourceId = sourceId,
					ExternalId = "NWS-CANCEL-002",
					ReferencesExternalId = "NWS-ORIGINAL-001",
					Event = "Cancellation",
					Severity = (int)WeatherAlertSeverity.Unknown
				};

				var providerMock = new Mock<IWeatherAlertProvider>();
				providerMock.Setup(p => p.FetchAlertsAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new List<WeatherAlert> { cancellingAlert });

				_weatherAlertSourceRepoMock
					.Setup(x => x.GetByIdAsync(sourceId.ToString()))
					.ReturnsAsync(source);
				_providerFactoryMock
					.Setup(x => x.GetProvider(WeatherAlertSourceType.NationalWeatherService))
					.Returns(providerMock.Object);
				_weatherAlertRepoMock
					.Setup(x => x.GetByExternalIdAndSourceIdAsync("NWS-CANCEL-002", sourceId))
					.ReturnsAsync((WeatherAlert)null);
				_weatherAlertRepoMock
					.Setup(x => x.GetByExternalIdAndSourceIdAsync("NWS-ORIGINAL-001", sourceId))
					.ReturnsAsync(referencedAlert);
				_weatherAlertRepoMock
					.Setup(x => x.InsertAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync(cancellingAlert);
				_weatherAlertRepoMock
					.Setup(x => x.UpdateAsync(It.IsAny<WeatherAlert>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((WeatherAlert a, CancellationToken _, bool __) => a);
				_weatherAlertSourceRepoMock
					.Setup(x => x.UpdateAsync(It.IsAny<WeatherAlertSource>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync(source);

				await _weatherAlertService.ProcessWeatherAlertSourceAsync(sourceId);

				referencedAlert.Status.Should().Be((int)WeatherAlertStatus.Cancelled);
			}
		}

		// ── Active alerts near location ──────────────────────────────────────────

		[TestFixture]
		public class when_getting_active_alerts_near_location : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_alerts_within_radius()
			{
				// Alert at approximately 47.6062, -122.3321 (Seattle)
				var alerts = new List<WeatherAlert>
				{
					new WeatherAlert
					{
						WeatherAlertId = Guid.NewGuid(),
						DepartmentId = TestDepartmentId,
						CenterGeoLocation = "47.6062,-122.3321",
						Status = (int)WeatherAlertStatus.Active
					},
					new WeatherAlert
					{
						WeatherAlertId = Guid.NewGuid(),
						DepartmentId = TestDepartmentId,
						CenterGeoLocation = "34.0522,-118.2437", // Los Angeles - far away
						Status = (int)WeatherAlertStatus.Active
					}
				};

				_weatherAlertRepoMock
					.Setup(x => x.GetActiveAlertsByDepartmentIdAsync(TestDepartmentId))
					.ReturnsAsync(alerts);

				// Query near Seattle with 50 mile radius
				var result = await _weatherAlertService.GetActiveAlertsNearLocationAsync(TestDepartmentId, 47.61, -122.33, 50);

				result.Should().NotBeNull();
				result.Count.Should().Be(1);
				result[0].CenterGeoLocation.Should().Contain("47.6062");
			}

			[Test]
			public async Task should_exclude_alerts_without_geolocation()
			{
				var alerts = new List<WeatherAlert>
				{
					new WeatherAlert
					{
						WeatherAlertId = Guid.NewGuid(),
						DepartmentId = TestDepartmentId,
						CenterGeoLocation = null
					}
				};

				_weatherAlertRepoMock
					.Setup(x => x.GetActiveAlertsByDepartmentIdAsync(TestDepartmentId))
					.ReturnsAsync(alerts);

				var result = await _weatherAlertService.GetActiveAlertsNearLocationAsync(TestDepartmentId, 47.61, -122.33, 50);

				result.Should().NotBeNull();
				result.Should().BeEmpty();
			}
		}

		// ── Cache invalidation ───────────────────────────────────────────────────

		[TestFixture]
		public class when_invalidating_cache : with_the_weather_alert_service
		{
			[Test]
			public async Task should_return_true()
			{
				var result = await _weatherAlertService.InvalidateDepartmentWeatherCacheAsync(TestDepartmentId);

				result.Should().BeTrue();
			}
		}
	}
}

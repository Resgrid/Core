using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Providers.Bus;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// Deployments without the legacy Azure hubs used to throw ArgumentNullException('connectionString')
	/// out of every register/unregister call, which in the unit worker skipped the Novu registration.
	/// </summary>
	[TestFixture]
	public class NotificationProviderUnconfiguredHubTests
	{
		private (string, string) _savedConfig;

		[SetUp]
		public void SetUp()
		{
			_savedConfig = (ServiceBusConfig.AzureNotificationHub_FullConnectionString, ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString);
		}

		[TearDown]
		public void TearDown()
		{
			(ServiceBusConfig.AzureNotificationHub_FullConnectionString, ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString) = _savedConfig;
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  ")]
		public async Task UnitProvider_register_and_unregister_should_no_op_when_hub_is_unconfigured(string connectionString)
		{
			ServiceBusConfig.AzureUnitNotificationHub_FullConnectionString = connectionString;
			var provider = new UnitNotificationProvider();
			var pushUri = CreatePushUri();

			Func<Task> act = async () =>
			{
				await provider.UnRegisterPush(pushUri);
				await provider.RegisterPush(pushUri);
				await provider.UnRegisterPushByUserDeviceId(pushUri);
				await provider.UnRegisterPushByUUID(pushUri.Uuid);
			};

			await act.Should().NotThrowAsync();
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("  ")]
		public async Task UserProvider_register_and_unregister_should_no_op_when_hub_is_unconfigured(string connectionString)
		{
			ServiceBusConfig.AzureNotificationHub_FullConnectionString = connectionString;
			var provider = new NotificationProvider();
			var pushUri = CreatePushUri();

			Func<Task> act = async () =>
			{
				await provider.UnRegisterPush(pushUri);
				await provider.RegisterPush(pushUri);
				await provider.UnRegisterPushByUserDeviceId(pushUri);
			};

			await act.Should().NotThrowAsync();
		}

		private static PushUri CreatePushUri()
		{
			return new PushUri
			{
				UserId = "D4813806-7923-4948-A219-439D6FDCE86A",
				UnitId = 9,
				DepartmentId = 7,
				PlatformType = (int)Platforms.Android,
				PushLocation = "DEPT",
				DeviceId = "device-token",
				Uuid = "device-uuid"
			};
		}
	}
}

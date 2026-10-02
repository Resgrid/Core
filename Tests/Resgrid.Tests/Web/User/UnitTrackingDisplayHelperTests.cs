using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Helpers;
using CommonResource = Resgrid.Localization.Common;
using UnitsResource = Resgrid.Localization.Areas.User.Units.Units;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The UnitTracking pages used to print the tracking enums raw ("CustomHeader", "NeverSeen",
	/// "FixtureVerified") in every language. Every value needs a key in Units.en.resx;
	/// ResourceKeyParityTests then requires it in each locale.
	/// </summary>
	[TestFixture]
	public class UnitTrackingDisplayHelperTests
	{
		private const string UnitsPrefix = "Units:";
		private const string CommonUnknown = "Common:Unknown";

		private IStringLocalizer<UnitsResource> _units;
		private IStringLocalizer<CommonResource> _common;
		private HashSet<string> _unitsKeys;

		[SetUp]
		public void SetUp()
		{
			_units = EchoLocalizer<UnitsResource>("Units");
			_common = EchoLocalizer<CommonResource>("Common");
			_unitsKeys = EnglishKeys(typeof(UnitsResource));
		}

		[Test]
		public void every_auth_mode_should_have_a_label()
		{
			AssertEveryValueHasALabel<UnitTrackingAuthMode>(mode => UnitTrackingDisplayHelper.GetLocalizedAuthMode(mode, _units, _common));
		}

		[Test]
		public void every_transport_should_have_a_label()
		{
			AssertEveryValueHasALabel<UnitTrackingTransportType>(transport => UnitTrackingDisplayHelper.GetLocalizedTransport(transport, _units, _common));
		}

		[Test]
		public void every_device_status_should_have_a_label()
		{
			AssertEveryValueHasALabel<UnitTrackingDeviceStatus>(status => UnitTrackingDisplayHelper.GetLocalizedDeviceStatus(status, _units));
		}

		[Test]
		public void every_certification_status_should_have_a_label()
		{
			AssertEveryValueHasALabel<UnitTrackingCertificationStatus>(status => UnitTrackingDisplayHelper.GetLocalizedCertificationStatus(status, _units, _common));
		}

		[Test]
		public void unknown_values_should_use_the_common_unknown_label()
		{
			EnglishKeys(typeof(CommonResource)).Should().Contain("Unknown");

			UnitTrackingDisplayHelper.GetLocalizedAuthMode(UnitTrackingAuthMode.Unknown, _units, _common).Should().Be(CommonUnknown);
			UnitTrackingDisplayHelper.GetLocalizedTransport(UnitTrackingTransportType.Unknown, _units, _common).Should().Be(CommonUnknown);
			UnitTrackingDisplayHelper.GetLocalizedCertificationStatus(UnitTrackingCertificationStatus.Unknown, _units, _common).Should().Be(CommonUnknown);
		}

		[Test]
		public void unmapped_values_should_fall_back_to_the_enum_name()
		{
			UnitTrackingDisplayHelper.GetLocalizedAuthMode((UnitTrackingAuthMode)99, _units, _common).Should().Be("99");
			UnitTrackingDisplayHelper.GetLocalizedDeviceStatus((UnitTrackingDeviceStatus)99, _units).Should().Be("99");
		}

		[Test]
		public void labels_should_follow_the_request_culture()
		{
			var previous = CultureInfo.CurrentUICulture;
			try
			{
				using var provider = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
				var units = provider.GetRequiredService<IStringLocalizer<UnitsResource>>();
				var common = provider.GetRequiredService<IStringLocalizer<CommonResource>>();

				CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
				UnitTrackingDisplayHelper.GetLocalizedAuthMode(UnitTrackingAuthMode.Basic, units, common).Should().Be("Basic-Authentifizierung");
				UnitTrackingDisplayHelper.GetLocalizedTransport(UnitTrackingTransportType.Unknown, units, common).Should().Be("Unbekannt");

				CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
				UnitTrackingDisplayHelper.GetLocalizedAuthMode(UnitTrackingAuthMode.CapabilityPath, units, common).Should().Be("Capability URL (token in path)");
			}
			finally
			{
				CultureInfo.CurrentUICulture = previous;
			}
		}

		private void AssertEveryValueHasALabel<TEnum>(Func<TEnum, string> render) where TEnum : struct, Enum
		{
			var keys = new List<string>();
			foreach (var value in Enum.GetValues<TEnum>())
			{
				var label = render(value);
				if (label == CommonUnknown)
				{
					value.ToString().Should().Be("Unknown", "only the Unknown value should borrow the Common label");
					continue;
				}

				label.Should().StartWith(UnitsPrefix, $"{typeof(TEnum).Name}.{value} should render a Units resource, not its enum name");
				var key = label.Substring(UnitsPrefix.Length);
				_unitsKeys.Should().Contain(key, $"{typeof(TEnum).Name}.{value} uses {key}, which Units.en.resx must define");
				keys.Add(key);
			}

			keys.Should().OnlyHaveUniqueItems($"each {typeof(TEnum).Name} value needs its own label");
		}

		private static IStringLocalizer<T> EchoLocalizer<T>(string resource)
		{
			var localizer = new Mock<IStringLocalizer<T>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string name) => new LocalizedString(name, $"{resource}:{name}"));
			return localizer.Object;
		}

		private static HashSet<string> EnglishKeys(Type resource)
		{
			var manager = new ResourceManager(resource.FullName!, resource.Assembly);
			return manager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, false)!
				.Cast<DictionaryEntry>()
				.Select(entry => (string)entry.Key)
				.ToHashSet();
		}
	}
}

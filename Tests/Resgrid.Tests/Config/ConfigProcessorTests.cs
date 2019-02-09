using FluentAssertions;
using NUnit.Framework;
using System.Collections.Generic;

namespace Resgrid.Tests.Config
{
	[TestFixture]
	public class ConfigProcessorTests
	{
		[Test]
		public void TestLoadingAndSettingConfig()
		{
			Resgrid.Config.ConfigProcessor.LoadAndProcessConfig();

			Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl.Should().NotBeEmpty();
			Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl.Should().Be("http://resgrid.local");
		}
	}
}

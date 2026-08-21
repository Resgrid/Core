using NUnit.Framework;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ClientSessionMetadataParserTests
	{
		private readonly ClientSessionMetadataParser _parser = new ClientSessionMetadataParser();

		[Test]
		public void parses_a_modern_windows_edge_session()
		{
			var result = _parser.Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0");

			Assert.That(result.DeviceType, Is.EqualTo("Computer"));
			Assert.That(result.DeviceName, Is.EqualTo("Windows 10/11 Computer"));
			Assert.That(result.OperatingSystem, Is.EqualTo("Windows 10/11"));
			Assert.That(result.Browser, Is.EqualTo("Edge 126.0.0.0"));
		}

		[Test]
		public void explicit_mobile_app_metadata_wins_over_user_agent_fallbacks()
		{
			var result = _parser.Parse("okhttp/4.12", "Engine 4", "Tablet", "Android 15", "Native",
				"9.2.1");

			Assert.That(result.DeviceName, Is.EqualTo("Engine 4"));
			Assert.That(result.DeviceType, Is.EqualTo("Tablet"));
			Assert.That(result.OperatingSystem, Is.EqualTo("Android 15"));
			Assert.That(result.Browser, Is.EqualTo("Native"));
			Assert.That(result.ApplicationVersion, Is.EqualTo("9.2.1"));
		}
	}
}

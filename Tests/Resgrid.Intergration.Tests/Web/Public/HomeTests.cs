using FluentAssertions;
using NHtmlUnit;
using NHtmlUnit.Html;
using NUnit.Framework;

namespace Resgrid.Intergration.Tests.Web.Public
{
	[TestFixture]
	public class HomeTests: WebTestBase
	{
		[SetUp]
		public void SetUp()
		{
			StartWebServer();
		}

		[TearDown]
		public void TearDown()
		{
			StopWebServer();
		}

		[Test]
		public void Render_Index()
		{
			WebClient webClient = new WebClient();
			HtmlPage page = (HtmlPage)webClient.GetPage(string.Format("http://localhost:{0}/", WebServerPort));
			WebResponse response = page.WebResponse;

			page.Should().NotBeNull();
			page.TitleText.Should().Be("Grid personnel resource logistics");
			response.StatusCode.Should().Be(200);
		}
	}
}

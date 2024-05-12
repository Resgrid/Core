using System;
using FluentAssertions;
using NHtmlUnit;
using NHtmlUnit.Html;
using NUnit.Framework;

namespace Resgrid.Intergration.Tests.Web.Public
{
	[TestFixture]
	public class LoginTests : WebTestBase
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
		public void Render_LoginPage()
		{
			WebClient webClient = new WebClient();
			HtmlPage page = (HtmlPage)webClient.GetPage(string.Format("http://localhost:{0}/Account/LogOn", WebServerPort));
			WebResponse response = page.WebResponse;

			page.Should().NotBeNull();
			page.TitleText.Should().Be("Resgrid | Log in to Resgrid");
			response.StatusCode.Should().Be(200);
		}

		[Test]
		public void Login_WithBlankUsernameAndPassword()
		{
			string url = string.Format("http://localhost:{0}/Account/LogOn", WebServerPort);
			WebClient webClient = new WebClient();
			HtmlPage page = (HtmlPage)webClient.GetPage(url);
			WebResponse response = page.WebResponse;

			HtmlForm form = page.GetFormByName("login_form");
			HtmlInput loginButton = form.GetInputByName("commit");
			HtmlInput userNameField = form.GetInputByName("UserName");
			HtmlInput passwordField = form.GetInputByName("Password");

			userNameField.ValueAttribute = "";
			passwordField.ValueAttribute = "";

			IPage page2 = loginButton.Click();

			page.TextContent.Should().Contain("The User name field is required.");
			page.TextContent.Should().Contain("The Password field is required.");
		}

		[Test]
		public void Login_WithInalidUsernameAndPassword()
		{
			string url = string.Format("http://localhost:{0}/Account/LogOn", WebServerPort);
			WebClient webClient = new WebClient();
			HtmlPage page = (HtmlPage)webClient.GetPage(url);
			WebResponse response = page.WebResponse;

			HtmlForm form = page.GetFormByName("login_form");
			HtmlInput loginButton = form.GetInputByName("commit");
			HtmlInput userNameField = form.GetInputByName("UserName");
			HtmlInput passwordField = form.GetInputByName("Password");

			userNameField.ValueAttribute = "123";
			passwordField.ValueAttribute = "123";

			IPage page2 = loginButton.Click();

			page.TextContent.Should().Contain("The User name field is required.");
			page.TextContent.Should().Contain("The Password field is required.");
		}
	}
}

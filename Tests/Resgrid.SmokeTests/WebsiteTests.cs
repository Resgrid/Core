using FluentAssertions;
using NUnit.Framework;
using RestSharp;
using SimpleBrowser;

namespace Resgrid.SmokeTests
{
	[TestFixture]
	public class WebsiteTests : SmokeTestBase
	{
		[Test]
		public void TestWebsiteLogin()
		{
			var browser = new Browser();
			// log the browser request/response data to files so we can interrogate them in case of an issue with our scraping
			//browser.RequestLogged += OnBrowserRequestLogged;
			//browser.MessageLogged += new Action<Browser, string>(OnBrowserMessageLogged);
			browser.UserAgent = "Mozilla/5.0 (Windows; U; Windows NT 6.1; en-US) AppleWebKit/534.10 (KHTML, like Gecko) Chrome/8.0.552.224 Safari/534.10";

			browser.Navigate("https://resgrid.com/Account/LogOn");
			LastRequestFailed(browser).Should().Be(false);

			var userNameField = browser.Find("Username");
			if (userNameField.Exists)
				userNameField.Value = "TestUser";

			var passwordField = browser.Find("Password");
			if (passwordField.Exists)
				passwordField.Value = "Password";

			var loginButton = browser.Find(ElementType.Button, "type", "submit");//.Click();
			if (loginButton.Exists)
				loginButton.Click();

			if (LastRequestFailed(browser) == false && browser.ContainsText("Test Department"))
			{
				try
				{
					var client2 = new RestClient("https://status.resgrid.com");
					var setMetricRequest = new RestRequest("api/v1/metrics/2/points", Method.Post);

					//client2.Execute(setMetricRequest);
				}
				catch { }
			}

			LastRequestFailed(browser).Should().Be(false);
			browser.ContainsText("Test Department").Should().Be(true);
		}

		static bool LastRequestFailed(Browser browser)
		{
			if (browser.LastWebException != null)
			{
				browser.Log("There was an error loading the page: " + browser.LastWebException.Message);
				return true;
			}
			return false;
		}
	}
}

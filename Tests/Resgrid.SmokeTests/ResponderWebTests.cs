//using FluentAssertions;
//using NUnit.Framework;
//using SimpleBrowser;

//namespace Resgrid.SmokeTests
//{
//	[TestFixture]
//	public class ResponderWebTests : SmokeTestBase
//	{
//		[Test]
//		public void TestResponderWebEdition()
//		{
//			var browser = new Browser();
//			// log the browser request/response data to files so we can interrogate them in case of an issue with our scraping
//			//browser.RequestLogged += OnBrowserRequestLogged;
//			//browser.MessageLogged += new Action<Browser, string>(OnBrowserMessageLogged);
//			browser.UserAgent = "Mozilla/5.0 (Windows; U; Windows NT 6.1; en-US) AppleWebKit/534.10 (KHTML, like Gecko) Chrome/8.0.552.224 Safari/534.10";

//			browser.Navigate("https://responder.resgrid.com");
//			LastRequestFailed(browser).Should().Be(false);
//		}

//		[Test]
//		public void TestUnitWebEdition()
//		{
//			var browser = new Browser();
//			// log the browser request/response data to files so we can interrogate them in case of an issue with our scraping
//			//browser.RequestLogged += OnBrowserRequestLogged;
//			//browser.MessageLogged += new Action<Browser, string>(OnBrowserMessageLogged);
//			browser.UserAgent = "Mozilla/5.0 (Windows; U; Windows NT 6.1; en-US) AppleWebKit/534.10 (KHTML, like Gecko) Chrome/8.0.552.224 Safari/534.10";

//			browser.Navigate("https://unit.resgrid.com");
//			LastRequestFailed(browser).Should().Be(false);
//		}

//		static bool LastRequestFailed(Browser browser)
//		{
//			if (browser.LastWebException != null)
//			{
//				browser.Log("There was an error loading the page: " + browser.LastWebException.Message);
//				return true;
//			}
//			return false;
//		}
//	}
//}

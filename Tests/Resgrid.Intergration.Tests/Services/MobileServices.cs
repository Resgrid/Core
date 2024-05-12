using System;
using System.Collections.Generic;
using FluentAssertions;
using NHtmlUnit;
using NHtmlUnit.Html;
using NUnit.Framework;

namespace Resgrid.Intergration.Tests.Services
{
	[TestFixture]
	public class MobileServices : WebTestBase
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

		#region SetUserStatus Method Tests
		[Test]
		public void SetUserStatus_CanSetNotResponding()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "1", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);
			
			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_CanSetResponding()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "1", "xxxx", "2");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_CanSetStandingBy()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "1", "xxxx", "0");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithWrongDepartmentCode()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "1", "1234", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
			
		}

		[Test]
		public void SetUserStatus_FailsWithEmptyDepartmentCode()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "1", "", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithNullDepartmentCode()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&action={4}",
											WebServerPort, "TestUser", "1", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithWrongDepartmentId()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "5", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithZeroDepartmentId()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "0", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithWrongUsername()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "badUsername", "1", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithEmptyUsername()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?userName={1}&departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "", "1", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}

		[Test]
		public void SetUserStatus_FailsWithNullUsername()
		{
			string url =
				string.Format("http://localhost:{0}/service/SetUserStatus?departmentId={2}&departmentCode={3}&action={4}",
											WebServerPort, "TestUser", "1", "xxxx", "1");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
		}
		#endregion SetUserStatus

		#region GetDepartmentStatus Method Tests
		[Test]
		public void GetDepartmentStatus_CanDepartmentStatus()
		{
			string url =
				string.Format("http://localhost:{0}/service/GetDepartmentStatus?userName={1}&departmentId={2}&departmentCode={3}", WebServerPort, "testUser1", "1", "xxxx");

			//var browser = new Browser();
			//browser.Navigate(url);

			//browser.Url.Should().Be(new Uri(url));
			//browser.Text.Should().NotBeNullOrEmpty();

			//List<Resgrid.Web.Models.Results.DepartmentStatus> statuses = JsonConvert.DeserializeObject<List<Resgrid.Web.Models.Results.DepartmentStatus>>(browser.Text);
			//statuses.Should().HaveCount(4);
		}

		[Test]
		public void GetDepartmentStatus_FailsToGetDepartmentStatusWithZeroId()
		{
			//JsonResult result = _controller.GetDepartmentStatus("testUser", "0", "xxxx") as JsonResult;


		}

		[Test]
		public void GetDepartmentStatus_FailsToGetDepartmentStatusWithCode()
		{
			//JsonResult result = _controller.GetDepartmentStatus("testUser", "5", "1234") as JsonResult;


		}

		[Test]
		public void GetDepartmentStatus_FailsToGetDepartmentStatusWithEmptyCode()
		{
			//JsonResult result = _controller.GetDepartmentStatus("testUser", "5", "") as JsonResult;


		}

		[Test]
		public void GetDepartmentStatus_FailsToGetDepartmentStatusWithNullCode()
		{
			//JsonResult result = _controller.GetDepartmentStatus("testUser", "5", null) as JsonResult;


		}
		#endregion GetDepartmentStatus Method Tests

		#region GetUserStatus Method Tests
		[Test]
		public void GetUserStatus_CanGetUserStatus()
		{
			//JsonResult result = _controller.GetUserStatus("testUser", "1", "xxxx") as JsonResult;


		}

		[Test]
		public void GetUserStatus_FailsToGetDepartmentStatusWithZeroId()
		{
			//JsonResult result = _controller.GetUserStatus("testUser", "0", "xxxx") as JsonResult;

		}

		[Test]
		public void GetUserStatus_FailsToGetDepartmentStatusWithCode()
		{
			//JsonResult result = _controller.GetUserStatus("testUser", "5", "1234") as JsonResult;

		}

		[Test]
		public void GetUserStatus_FailsToGetDepartmentStatusWithEmptyCode()
		{
			//JsonResult result = _controller.GetUserStatus("testUser", "5", "") as JsonResult;

		}

		[Test]
		public void GetUserStatus_FailsToGetDepartmentStatusWithNullCode()
		{
			//JsonResult result = _controller.GetUserStatus("testUser", "5", null) as JsonResult;

		}
		#endregion GetDepartmentStatus Method Tests
	}
}

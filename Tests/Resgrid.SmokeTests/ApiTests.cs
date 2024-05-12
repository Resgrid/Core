using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using RestSharp;

namespace Resgrid.SmokeTests
{
	[TestFixture]
	public class ApiTests : SmokeTestBase
	{
		private const string Token = "RkRSbUUwWjUxajAyNVNuRlhKdGp5c0VpeGFFQ3l3SUZlZEtWaUwvOUxTbz0=";

		[Test]
		public async Task GetToken()
		{
			var client = new RestClient("http://resgridapi.local");

			var setStatusRequest = new RestRequest("api/v3/Auth/Validate", Method.Post);

			setStatusRequest.AddObject(new
			{
				Usr = "TestUser",
				Pass = "Password"
			});

			var setStatusResponse = await client.ExecuteAsync(setStatusRequest);
			setStatusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
			setStatusResponse.Content.Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task TestUserStatusApiCall()
		{
			var client = new RestClient("http://resgridapi.local");

			var setStatusRequest = new RestRequest("api/v3/Status/SetCurrentStatus", Method.Post);
			var getStatusRequest = new RestRequest("api/v3/Status/GetCurrentStatus", Method.Get);

			setStatusRequest.AddHeader("Authorization", "Basic " + Token);
			getStatusRequest.AddHeader("Authorization", "Basic " + Token);

			var rnd = new Random();
			var type = rnd.Next(0, 3);

			var statusInput = new StatusInput();
			statusInput.Typ = type;
			setStatusRequest.AddObject(statusInput);

			var setStatusResponse = await client.ExecuteAsync(setStatusRequest);

			Thread.Sleep(250);

			var getStatusResponse = await client.ExecuteAsync<StatusResult>(getStatusRequest);

			if (setStatusResponse.StatusCode == HttpStatusCode.Created)
			{
				try
				{
					var client2 = new RestClient("https://status.resgrid.com");
					var setMetricRequest = new RestRequest("api/v1/metrics/1/points", Method.Post);

					//client2.Execute(setMetricRequest);
				}
				catch { }
			}

			setStatusResponse.StatusCode.Should().Be(HttpStatusCode.Created);
			getStatusResponse.Content.Should().NotBeNullOrWhiteSpace();
			getStatusResponse.Data.Should().NotBeNull();
			getStatusResponse.Data.Act.Should().Be(type);
			setStatusResponse.Content.Should().NotContain("Authorization has been denied for this request");
			getStatusResponse.Content.Should().NotContain("Authorization has been denied for this request");
		}

		[Test]
		public async Task TestUserStaffingLevelApiCall()
		{
			var tasks = await Resolve<IScheduledTasksService>().GetScheduledStaffingTasksForUserAsync("50DEC5DB-2612-4D6A-97E3-2F04B7228C85");
			var department = await Resolve<IDepartmentsService>().GetDepartmentByIdAsync(971);
			//var username = "TestUser";

			var client = new RestClient("http://resgridapi.local");
			//var stringData = username + "|" + department.DepartmentId + "|" + department.Code;

			var request = new RestRequest("api/v3/Staffing/GetCurrentStaffing", Method.Get);

			request.AddHeader("Authorization", "Basic " + Token);
			var response = await client.ExecuteAsync<StaffingResult>(request);

			response.Content.Should().NotBeNullOrWhiteSpace();
			response.Data.Should().NotBeNull();
			response.Content.Should().NotContain("Authorization has been denied for this request");

			if (tasks != null && tasks.Count > 0)
			{
				var currentTime = DateTime.UtcNow.TimeConverter(department);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select task).FirstOrDefault();
				var currentStaffingLevel = int.Parse(currentStaffing.Data);

				if (currentTime.Minute <= 53)
					response.Data.Typ.Should().Be(currentStaffingLevel);
			}
		}
	}

	public class StatusInput
	{
		public string Uid { get; set; }
		public int Typ { get; set; }
		public int Rto { get; set; }
		public string Geo { get; set; }
		public int Dtp { get; set; }
		public string Not { get; set; }
	}

	public class StaffingResult
	{
		public string Uid { get; set; }
		public string Nme { get; set; }
		public int Typ { get; set; }
		public DateTime Tms { get; set; }
		public string Not { get; set; }
	}

	public class StatusResult
	{
		public string Uid { get; set; }
		public int Act { get; set; }
		public DateTime Ats { get; set; }
		public int Ste { get; set; }
		public DateTime Sts { get; set; }
		public int Did { get; set; }
	}
}

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	namespace ScheduledTasksServiceTests
	{
		public class with_the_scheduled_tasks_service : TestBase
		{
			protected IScheduledTasksService _scheduledTasksService;

			public with_the_scheduled_tasks_service()
			{
				_scheduledTasksService = Resolve<IScheduledTasksService>();
			}
		}

		[TestFixture]
		public class when_reading_scheduled_tasks: with_the_scheduled_tasks_service
		{
			[Test]
			public async Task should_be_able_to_get_all_tasks_for_user()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				tasks.Should().NotBeEmpty();
				tasks.Should().HaveCount(24);
				tasks.Should().OnlyContain(x => x.UserId == TestData.Users.TestUser2Id);
			}

			//[Test]
			public async Task should_be_able_to_get_staffing_for_midnight()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014,5,10,0,36,51,9);
				var currentStaffing = (from task in tasks
					let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
					where whenShouldJobRun < currentTime
					orderby whenShouldJobRun descending
					select new {
						Task = task,
						WhenShouldRun = whenShouldJobRun
					});//.FirstOrDefault();

				currentStaffing.Should().NotBeNull();
				//currentStaffing.Data.Should().Be("3");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0000()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 0, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("3");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0100()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 1, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("0");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0200()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 2, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("1");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0300()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 3, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("2");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0400()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 4, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("3");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0500()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 5, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("4");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0600()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 6, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("0");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0700()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 7, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("1");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0800()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 8, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("2");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_0900()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 9, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("3");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1000()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 10, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("4");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1100()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 11, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("0");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1200()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 12, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("1");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1300()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 13, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("2");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1400()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 14, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("3");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1500()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 15, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("4");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1600()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 16, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("0");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1700()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 17, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("1");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1800()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 18, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("2");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_1900()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 19, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("3");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_2000()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 20, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("4");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_2100()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 21, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("0");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_2200()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 22, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("1");
			}

			[Test]
			public async Task should_be_able_to_get_staffing_for_2300()
			{
				var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(TestData.Users.TestUser2Id);

				var currentTime = new DateTime(2014, 5, 10, 23, 36, 51, 9);
				var currentStaffing = (from task in tasks
									   let whenShouldJobRun = task.WhenShouldJobBeRun(currentTime)
									   where whenShouldJobRun < currentTime
									   orderby whenShouldJobRun descending
									   select new
									   {
										   Task = task,
										   WhenShouldRun = whenShouldJobRun
									   }).FirstOrDefault();


				currentStaffing.Should().NotBeNull();
				currentStaffing.Task.Should().NotBeNull();
				currentStaffing.Task.Data.Should().Be("2");
			}
		}

		[TestFixture]
		public class when_getting_upcoming_tasks : with_the_scheduled_tasks_service
		{
			[Test]
			public async Task should_be_able_to_get_staffing_reset_for_midnight()
			{
				var currentTime = new DateTime(2014,5,11,3,59,51,9);
				var tasks = await _scheduledTasksService.GetUpcomingScheduledTasksAsync(currentTime, null);

				var staffingResetTask = from t in tasks
					where t.UserId == TestData.Users.TestUser1Id && t.TaskType == 2
					select t;

				staffingResetTask.Should().NotBeEmpty();
				staffingResetTask.Should().HaveCount(1);
				staffingResetTask.Should().OnlyContain(x => x.UserId == TestData.Users.TestUser1Id);
			}
		}
	}
}

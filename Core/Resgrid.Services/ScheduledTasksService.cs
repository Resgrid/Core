using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	public class ScheduledTasksService : IScheduledTasksService
	{
		private static string CacheKey = "AllScheduledTasks";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly IScheduledTasksRepository _scheduledTaskRepository;
		private readonly IScheduledTaskLogsRepository _scheduledTaskLogRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICacheProvider _cacheProvider;

		public ScheduledTasksService(IScheduledTasksRepository scheduledTaskRepository, IScheduledTaskLogsRepository scheduledTaskLogRepository,
			IDepartmentsService departmentsService, ICacheProvider cacheProvider)
		{
			_scheduledTaskRepository = scheduledTaskRepository;
			_scheduledTaskLogRepository = scheduledTaskLogRepository;
			_departmentsService = departmentsService;
			_cacheProvider = cacheProvider;
		}

		public async Task<List<ScheduledTask>> GetScheduledStaffingTasksForUserAsync(string userId)
		{
			return (from st in await GetAllScheduledTasksAsync()
							where st.UserId == userId && st.TaskType == (int)TaskTypes.UserStaffingLevel
							select st).ToList();
		}

		public async Task<List<ScheduledTask>> GetScheduledReportingTasksForUserAsync(string userId)
		{
			return (from st in await GetAllScheduledTasksAsync()
							where st.UserId == userId && st.TaskType == (int)TaskTypes.ReportDelivery
							select st).ToList();
		}

		public async Task<ScheduledTask> SaveScheduledTaskAsync(ScheduledTask scheduledTask, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _scheduledTaskRepository.SaveOrUpdateAsync(scheduledTask, cancellationToken);

			InvalidateScheduledTasksCache();

			return saved;
		}

		public async Task<ScheduledTask> GetScheduledTaskByIdAsync(int scheduledTaskId)
		{
			return await _scheduledTaskRepository.GetByIdAsync(scheduledTaskId);
		}

		public async Task<List<ScheduledTask>> GetScheduledTasksByUserTypeAsync(string userId, int taskType)
		{
			return (from st in await GetAllScheduledTasksAsync()
							where st.UserId == userId && st.TaskType == taskType
							select st).ToList();
		}

		public async Task<bool> DeleteDepartmentStaffingResetJobAsync(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			// TODO: Possible bug here, if they change the managing user after setting up a job, it will miss here
			var tasks = (from st in await _scheduledTaskRepository.GetAllAsync()
						 where st.DepartmentId == departmentId && st.TaskType == (int)TaskTypes.DepartmentStaffingReset
					select st).ToList();

			//var tasks = GetScheduledTasksByUserType(department.ManagingUserId, (int)TaskTypes.DepartmentStaffingReset);

			foreach (var task in tasks)
			{
				await _scheduledTaskRepository.DeleteAsync(task, cancellationToken);
			}

			var tasks2 = (from st in await _scheduledTaskRepository.GetAllAsync()
						  where st.UserId == department.ManagingUserId && st.TaskType == (int)TaskTypes.DepartmentStaffingReset
						 select st).ToList();

			//var tasks = GetScheduledTasksByUserType(department.ManagingUserId, (int)TaskTypes.DepartmentStaffingReset);

			foreach (var task in tasks2)
			{
				await _scheduledTaskRepository.DeleteAsync(task, cancellationToken);
			}

			InvalidateScheduledTasksCache();

			return true;
		}

		public async Task<bool> DeleteDepartmentStatusResetJob(int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId);

			// TODO: Possible bug here, if they change the managing user after setting up a job, it will miss here
			//var tasks = GetScheduledTasksByUserType(department.ManagingUserId, (int)TaskTypes.DepartmentStatusReset);

			// TODO: Possible bug here, if they change the managing user after setting up a job, it will miss here
			var tasks = (from st in await _scheduledTaskRepository.GetAllAsync()
						 where st.DepartmentId == departmentId && st.TaskType == (int)TaskTypes.DepartmentStatusReset
						 select st).ToList();

			//var tasks = GetScheduledTasksByUserType(department.ManagingUserId, (int)TaskTypes.DepartmentStaffingReset);

			foreach (var task in tasks)
			{
				await _scheduledTaskRepository.DeleteAsync(task, cancellationToken);
			}

			var tasks2 = (from st in await _scheduledTaskRepository.GetAllAsync()
						  where st.UserId == department.ManagingUserId && st.TaskType == (int)TaskTypes.DepartmentStatusReset
						  select st).ToList();

			//var tasks = GetScheduledTasksByUserType(department.ManagingUserId, (int)TaskTypes.DepartmentStaffingReset);

			foreach (var task in tasks2)
			{
				await _scheduledTaskRepository.DeleteAsync(task, cancellationToken);
			}

			InvalidateScheduledTasksCache();

			return true;
		}

		public async Task<ScheduledTask> DisabledScheduledTaskByIdAsync(int scheduledTaskId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var scheduledTask = await GetScheduledTaskByIdAsync(scheduledTaskId);

			if (scheduledTask != null)
			{
				scheduledTask.Active = false;
				return await SaveScheduledTaskAsync(scheduledTask, cancellationToken);
			}

			return null;
		}

		public async Task<ScheduledTask> EnableScheduledTaskByIdAsync(int scheduledTaskId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var scheduledTask = await GetScheduledTaskByIdAsync(scheduledTaskId);

			if (scheduledTask != null)
			{
				scheduledTask.Active = true;
				return await SaveScheduledTaskAsync(scheduledTask, cancellationToken);
			}

			return null;
		}

		public async Task<bool> DeleteScheduledTask(int scheduledTaskId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var scheduledTask = await GetScheduledTaskByIdAsync(scheduledTaskId);

			if (scheduledTask != null)
			{
				var result = await _scheduledTaskRepository.DeleteAsync(scheduledTask, cancellationToken);
				InvalidateScheduledTasksCache();
				return result;
			}

			return false;
		}

		public void InvalidateScheduledTasksCache()
		{
			_cacheProvider.Remove(CacheKey);
		}

		public async Task<List<ScheduledTask>> GetAllScheduledTasksAsync(bool bypassCache = false)
		{
			async Task<List<ScheduledTask>> getAllScheduledTasks()
			{
				var items = await _scheduledTaskRepository.GetAllAsync();

				if (items != null && items.Any())
					return items.ToList();

				return new List<ScheduledTask>();
			}


			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync<List<ScheduledTask>>(CacheKey, getAllScheduledTasks, CacheLength);
			else
				return await getAllScheduledTasks();
		}

		public async Task<List<ScheduledTask>> GetAllUpcomingStaffingScheduledTasksAsync()
		{
			//var tasks = (from st in GetAllScheduledTasks()
			//						 where st.Active == true && (st.TaskType == (int)TaskTypes.DepartmentStaffingReset || st.TaskType == (int)TaskTypes.UserStaffingLevel)
			//						 select st).ToList();

			var tasks = await _scheduledTaskRepository.GetAllActiveTasksForTypesAsync(new[] { (int)TaskTypes.DepartmentStaffingReset, (int)TaskTypes.UserStaffingLevel }.ToList());

			return await GetUpcomingScheduledTasksAsync(DateTime.UtcNow, tasks.ToList());
		}

		public async Task<List<ScheduledTask>> GetAllUpcomingStatusScheduledTasksAsync()
		{
			var tasks = await _scheduledTaskRepository.GetAllActiveTasksForTypesAsync(new[] { (int)TaskTypes.DepartmentStatusReset }.ToList());

			return await GetUpcomingScheduledTasksAsync(DateTime.UtcNow, tasks.ToList());
		}

		public async Task<List<ScheduledTask>> GetUpcomingScheduledTasksAsync()
		{
			return await GetUpcomingScheduledTasksAsync(DateTime.UtcNow, null);
		}

		public async Task<List<ScheduledTask>> GetAllUpcomingOrRecurringReportDeliveryTasksAsync()
		{
			var items = await _scheduledTaskRepository.GetAllUpcomingOrRecurringReportDeliveryTasksAsync();

			if (items != null && items.Any())
			{
				return items.ToList();
			}

			return new List<ScheduledTask>();
		}

		public async Task<List<ScheduledTask>> GetUpcomingScheduledTasksAsync(DateTime currentTime, List<ScheduledTask> tasks)
		{
			//Logging.LogTrace("ScheduledTasksService: Entering GetUpcomingScheduledTaks");

			var upcomingTasks = new List<ScheduledTask>();

			if (tasks == null || !tasks.Any())
			{
				tasks = (from st in await GetAllScheduledTasksAsync()
								 where st.Active == true
								 select st).ToList();
			}

			var logs = await _scheduledTaskLogRepository.GetAllLogForDateAsync(DateTime.UtcNow);

			//Logging.LogTrace(string.Format("ScheduledTasksService: Analyzing {0} active tasks.", tasks.Count));

			foreach (var scheduledTask in tasks)
			{
				try
				{
					//Logging.LogTrace(string.Format("ScheduledTasksService: ScheduledTask Id: {0}", scheduledTask.ScheduledTaskId));

					//var department = _departmentsService.GetDepartmentByUserId(scheduledTask.UserId, false);
					//if (department != null)
					//{
					//	//Logging.LogTrace(string.Format("ScheduledTasksService: Department Id: {0}, Name: {1}, TimeZone: {2}",
					//		department.DepartmentId, department.Name, department.TimeZone));

						var runDate = TimeConverterHelper.TimeConverter(currentTime, new Department() { TimeZone = scheduledTask.DepartmentTimeZone });
						//Logging.LogTrace(string.Format("ScheduledTasksService: Running For Department Localized Time: {0}", runDate));

						///* We need to pull the Log for this schedule. There should only ever be 1 log per ScheduledTask
					 //* per day, you can't run a scheduled task multiple times per day (at the current time)
					 //*
					 //* SJ 6/8/2014: I think a problem here was that we are tracking the log in UTC, but all other work is occuring
					 //* in a localized DateTime. We were seeing some very strange behavior when UTC ticked past midnight. I've modified
					 //* the selection below to run on a localized DateTime. Linq to Entities doesn't support time converion, so I broke
					 //* up the query.
					 //*/
						//var logs = _scheduledTaskLogRepository.GetAllLogsForTask(scheduledTask.ScheduledTaskId);

						var log = (from stl in logs
											 where stl.ScheduledTaskId == scheduledTask.ScheduledTaskId
											 //let localizedRunDate = TimeConverterHelper.TimeConverter(stl.RunDate, new Department() { TimeZone = scheduledTask.DepartmentTimeZone })
											 //where localizedRunDate.Day == runDate.Day &&
											 //		 localizedRunDate.Month == runDate.Month &&
											 //		 localizedRunDate.Year == runDate.Year
											 select stl).FirstOrDefault();

						//if (log == null)
							//Logging.LogTrace("ScheduledTasksService: No previous log");
						//else
							//Logging.LogTrace("ScheduledTasksService: Previous Task Log detected.");

						var runTime = scheduledTask.WhenShouldJobBeRun(runDate);

						if (runTime.HasValue)
						{
							//Logging.LogTrace(string.Format("ScheduledTasksService: When should Task be run: {0}", runTime));

							if (scheduledTask.AddedOn.HasValue)
							{
								//Logging.LogTrace(string.Format("ScheduledTasksService: Schedule task AddedOn has value: {0}",	scheduledTask.AddedOn));

								if (scheduledTask.AddedOn.Value < DateTime.UtcNow)
								{
									//Logging.LogTrace(string.Format("ScheduledTasksService: Schedule task AddedOn is less then UtcNow"));

									if (runTime.Value > runDate && log == null)
										upcomingTasks.Add(scheduledTask);
									else if (runTime.Value < runDate && log == null)
										upcomingTasks.Add(scheduledTask);
								}
							}
							else
							{
								//Logging.LogTrace(string.Format("ScheduledTasksService: Schedule task AddedOn has no value"));

								if (runTime.Value > runDate && log == null)
									upcomingTasks.Add(scheduledTask);
								else if (runTime.Value < runDate && log == null)
									upcomingTasks.Add(scheduledTask);
							}
						}
						//else
							//Logging.LogTrace(string.Format("ScheduledTasksService: NO SCHEDULE RUNTIME VALUE DETECTED"));
					//}
					//else
					//{
					//	Logging.LogError(string.Format("ScheduledTasksService: Cannot find Department for UserId: {0}", scheduledTask.UserId));
					//}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

			}

			//Logging.LogTrace("ScheduledTasksService: Leaving GetUpcomingScheduledTaks");

			return upcomingTasks;
		}

		public async Task<List<ScheduledTask>> GetUpcomingScheduledTasksByUserIdAsync(string userId)
		{
			var upcomingTasks = new List<ScheduledTask>();

			var tasks = (from st in await GetAllScheduledTasksAsync()
									 where st.Active == true && st.UserId == userId
									 select st).ToList();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var runDate = TimeConverterHelper.TimeConverter(DateTime.UtcNow, department);

			foreach (var scheduledTask in tasks)
			{
				var log = await _scheduledTaskLogRepository.GetLogForTaskAndDateAsync(scheduledTask.ScheduledTaskId, DateTime.UtcNow);

				var runTime = scheduledTask.WhenShouldJobBeRun(runDate);

				if (runTime.HasValue)
				{
					if (scheduledTask.AddedOn.HasValue)
					{
						if (scheduledTask.AddedOn.Value < DateTime.UtcNow)
						{
							if (runTime.Value > runDate && log == null)
								upcomingTasks.Add(scheduledTask);
							else if (runTime.Value < runDate && log == null)
								upcomingTasks.Add(scheduledTask);
						}
					}
					else
					{
						if (runTime.Value > runDate && log == null)
							upcomingTasks.Add(scheduledTask);
						else if (runTime.Value < runDate && log == null)
							upcomingTasks.Add(scheduledTask);
					}
				}
			}

			return upcomingTasks;
		}

		public async Task<List<ScheduledTask>> GetUpcomingScheduledTasksByUserIdTaskTypeAsync(string userId, TaskTypes? type)
		{
			var upcomingTasks = new List<ScheduledTask>();
			var tasks = new List<ScheduledTask>();

			if (type == null)
				tasks = (from st in await GetAllScheduledTasksAsync()
								 where st.Active == true && st.UserId == userId
								 select st).ToList();
			else
				tasks = (from st in await GetAllScheduledTasksAsync()
								 where st.Active == true && st.UserId == userId && st.TaskType == (int)type.Value
								 select st).ToList();

			var department = await _departmentsService.GetDepartmentByUserIdAsync(userId);
			var runDate = TimeConverterHelper.TimeConverter(DateTime.UtcNow, department);

			foreach (var scheduledTask in tasks)
			{
				var log = await _scheduledTaskLogRepository.GetLogForTaskAndDateAsync(scheduledTask.ScheduledTaskId, DateTime.UtcNow);

				var runTime = scheduledTask.WhenShouldJobBeRun(runDate);

				if (runTime.HasValue)
				{
					if (scheduledTask.AddedOn.HasValue)
					{
						if (scheduledTask.AddedOn.Value < DateTime.UtcNow)
						{
							if (runTime.Value > runDate && log == null)
								upcomingTasks.Add(scheduledTask);
							else if (runTime.Value < runDate && log == null)
								upcomingTasks.Add(scheduledTask);
						}
					}
					else
					{
						if (runTime.Value > runDate && log == null)
							upcomingTasks.Add(scheduledTask);
						else if (runTime.Value < runDate && log == null)
							upcomingTasks.Add(scheduledTask);
					}
				}
			}

			return upcomingTasks;
		}

		public async Task<ScheduledTaskLog> CreateScheduleTaskLogAsync(ScheduledTask task, CancellationToken cancellationToken = default(CancellationToken))
		{
			var log = new ScheduledTaskLog();
			log.RunDate = DateTime.UtcNow;
			log.Successful = true;
			log.ScheduledTaskId = task.ScheduledTaskId;

			return await _scheduledTaskLogRepository.SaveOrUpdateAsync(log, cancellationToken);
		}
	}
}

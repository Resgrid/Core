using Resgrid.Console.Args;
using System;
using Consolas2.Core;
using Resgrid.Model.Services;
using Resgrid.Workers.Framework;
using Autofac;

namespace Resgrid.Console.Commands
{
	public class ClearCacheCommand : Command
	{
		private readonly IConsole _console;

		public ClearCacheCommand(IConsole console)
		{
			_console = console;
		}

		public string Execute(ClearCacheArgs args)
		{
			_console.WriteLine("Clearing Cache for Department Id " + args.DepartmentId);
			_console.WriteLine("Please Wait...");

			try
			{
				var callsService = Bootstrapper.GetKernel().Resolve<ICallsService>();
				var userProfileService = Bootstrapper.GetKernel().Resolve<IUserProfileService>();
				var communicationService = Bootstrapper.GetKernel().Resolve<ICommunicationService>();
				var messageService = Bootstrapper.GetKernel().Resolve<IMessageService>();
				var departmentSettingsService = Bootstrapper.GetKernel().Resolve<IDepartmentSettingsService>();
				var queueService = Bootstrapper.GetKernel().Resolve<IQueueService>();
				var pushService = Bootstrapper.GetKernel().Resolve<IPushService>();
				var subscriptionService = Bootstrapper.GetKernel().Resolve<ISubscriptionsService>();
				var scheduledTasksService = Bootstrapper.GetKernel().Resolve<IScheduledTasksService>();
				var departmentService = Bootstrapper.GetKernel().Resolve<IDepartmentsService>();
				var actionLogsService = Bootstrapper.GetKernel().Resolve<IActionLogsService>();
				var customStatesService = Bootstrapper.GetKernel().Resolve<ICustomStateService>();
				var usersService = Bootstrapper.GetKernel().Resolve<IUsersService>();

				subscriptionService.ClearCacheForCurrentPayment(args.DepartmentId);
				departmentService.InvalidateDepartmentUsersInCache(args.DepartmentId);
				departmentService.InvalidateDepartmentInCache(args.DepartmentId);
				departmentService.InvalidatePersonnelNamesInCache(args.DepartmentId);
				userProfileService.ClearAllUserProfilesFromCache(args.DepartmentId);
				usersService.ClearCacheForDepartment(args.DepartmentId);
				actionLogsService.InvalidateActionLogs(args.DepartmentId);
				customStatesService.InvalidateCustomStateInCache(args.DepartmentId);
				departmentService.InvalidateDepartmentMembers();

				_console.WriteLine("Completed Clearing Cache");
			}
			catch (Exception ex)
			{
				_console.WriteLine("Failed to clear Cache");
				_console.WriteLine(ex.ToString());
			}

			return "";
		}
	}
}

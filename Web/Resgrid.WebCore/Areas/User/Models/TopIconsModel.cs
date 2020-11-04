using System;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using CommonServiceLocator;

namespace Resgrid.Web.Areas.User.Models
{
	public class TopIconsModel
	{
		private static IDepartmentsService _departmentsService;

		public TopIconsModel()
		{
			if (_departmentsService == null)
				_departmentsService = ServiceLocator.Current.GetInstance<IDepartmentsService>();
		}

		public int NewCalls { get; set; }
		public int NewMessages { get; set; }

		public async Task<bool> SetMessages(string userId, int departmentId)
		{
			// Trying to speed this up (may need to ditch it all together) but at times
			// this is a big hit on NewRelic, most likely because it's constructing the
			// message and call service every time. It's not an ideal solution, but hopefully
			// it will help for a bit.
			// TODO: Make a SQL Call
			try
			{
				var stats = await _departmentsService.GetDepartmentStatsByDepartmentUserIdAsync(departmentId, userId);
				NewMessages = stats.UnreadMessageCount;
				NewCalls = stats.OpenCallsCount;
			}
			catch (Exception)
			{
				_departmentsService =  ServiceLocator.Current.GetInstance<IDepartmentsService>();

				var stats = await _departmentsService.GetDepartmentStatsByDepartmentUserIdAsync(departmentId, userId);
				NewMessages = stats.UnreadMessageCount;
				NewCalls = stats.OpenCallsCount;
			}

			return true;
		}
	}
}

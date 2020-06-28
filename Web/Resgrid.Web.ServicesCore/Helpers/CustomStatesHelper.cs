using Autofac;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.ServicesCore.Helpers
{
	public static class CustomStatesHelper
	{
		public static CustomStateDetail GetCustomState(int departmentId, int detailId)
		{
			var customStateService = WebBootstrapper.GetKernel().Resolve<ICustomStateService>();

			var stateDetail = customStateService.GetCustomDetailForDepartment(departmentId, detailId);

			return stateDetail;
		}

		public static CustomStateDetail GetCustomPersonnelStaffing(int departmentId, UserState state)
		{
			if (state.State <= 25)
			{
				var detail = new CustomStateDetail();

				detail.ButtonText = state.GetStaffingText();
				detail.ButtonColor = state.GetStaffingCss();

				if (string.IsNullOrWhiteSpace(detail.ButtonColor))
					detail.ButtonColor = "label-default";

				return detail;
			}
			else
			{
				var customStateService = WebBootstrapper.GetKernel().Resolve<ICustomStateService>();
				var stateDetail = customStateService.GetCustomDetailForDepartment(departmentId, state.State);

				return stateDetail;
			}
		}

		public static CustomStateDetail GetCustomPersonnelStatus(int departmentId, ActionLog state)
		{
			if (state.ActionTypeId <= 25)
			{
				var detail = new CustomStateDetail();

				detail.ButtonText = state.GetActionText();
				detail.ButtonColor = state.GetActionCss();

				if (string.IsNullOrWhiteSpace(detail.ButtonColor))
					detail.ButtonColor = "label-default";

				return detail;
			}
			else
			{
				var customStateService = WebBootstrapper.GetKernel().Resolve<ICustomStateService>();
				var stateDetail = customStateService.GetCustomDetailForDepartment(departmentId, state.ActionTypeId);

				return stateDetail;
			}
		}
	}
}

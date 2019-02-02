using Autofac;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models;

namespace Resgrid.Web.Helpers
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

		public static CustomStateDetail GetCustomUnitState(UnitState state)
		{
			if (state.State <= 25)
			{
				var detail = new CustomStateDetail();

				detail.ButtonText = state.ToStateDisplayText();
				detail.ButtonColor = state.ToStateCss();

				if (string.IsNullOrWhiteSpace(detail.ButtonColor))
					detail.ButtonColor = "label-default";

				return detail;
			}
			else
			{
				var customStateService = WebBootstrapper.GetKernel().Resolve<ICustomStateService>();
				var stateDetail = customStateService.GetCustomDetailForDepartment(state.Unit.DepartmentId, state.State);

				return stateDetail;
			}
		}
	}
}
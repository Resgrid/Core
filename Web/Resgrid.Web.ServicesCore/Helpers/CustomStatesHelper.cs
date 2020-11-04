using System.Threading.Tasks;
using Autofac;
using CommonServiceLocator;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.BigBoard.BigBoardX;

namespace Resgrid.Web.Helpers
{
	public static class CustomStatesHelper
	{
		public static async Task<CustomStateDetail> GetCustomState(int departmentId, int detailId)
		{
			var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>(); //WebBootstrapper.GetKernel().Resolve<ICustomStateService>();

			var stateDetail = await customStateService.GetCustomDetailForDepartmentAsync(departmentId, detailId);

			return stateDetail;
		}

		public static async Task<CustomStateDetail> GetCustomPersonnelStaffing(int departmentId, UserState state)
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
				var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();
				var stateDetail = await customStateService.GetCustomDetailForDepartmentAsync(departmentId, state.State);

				return stateDetail;
			}
		}

		public static async Task<CustomStateDetail> GetCustomPersonnelStatus(int departmentId, ActionLog state)
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
				var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();
				var stateDetail = await customStateService.GetCustomDetailForDepartmentAsync(departmentId, state.ActionTypeId);

				return stateDetail;
			}
		}

		public static async Task<CustomStateDetail> GetCustomUnitState(UnitState state)
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
				var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();
				var stateDetail = await customStateService.GetCustomDetailForDepartmentAsync(state.Unit.DepartmentId, state.State);

				return stateDetail;
			}
		}
	}
}

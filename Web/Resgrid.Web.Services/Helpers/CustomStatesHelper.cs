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

		/// <summary>
		/// The department's own option of one kind for a stored value (deleted ones included), or null when the value is a
		/// built-in status or staffing level (StatusOptionIds).
		/// </summary>
		public static async Task<CustomStateDetail> FindCustomState(int departmentId, CustomStateTypes type, int rawValue)
		{
			var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();

			return StatusOptionIds.FindCustom(await customStateService.GetAllCustomStatesForDepartmentAsync(departmentId), type, rawValue);
		}

		// The department's own option first (by its custom option ids, deleted ones included), then the built-in value:
		// see StatusOptionIds. The service resolvers apply the same rule.
		public static async Task<CustomStateDetail> GetCustomPersonnelStaffing(int departmentId, UserState state)
		{
			var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();

			return await customStateService.GetCustomPersonnelStaffingAsync(departmentId, state);
		}

		public static async Task<CustomStateDetail> GetCustomPersonnelStatus(int departmentId, ActionLog state)
		{
			var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();

			return await customStateService.GetCustomPersonnelStatusAsync(departmentId, state);
		}

		public static async Task<CustomStateDetail> GetCustomUnitState(UnitState state)
		{
			var customStateService = ServiceLocator.Current.GetInstance<ICustomStateService>();

			if (state.Unit != null)
			{
				var custom = StatusOptionIds.FindCustom(await customStateService.GetAllCustomStatesForDepartmentAsync(state.Unit.DepartmentId),
					CustomStateTypes.Unit, state.State);

				if (custom != null)
					return custom;
			}

			if (state.State > CallStatusLinkage.MaxBuiltInStatusId)
				return null;

			var detail = new CustomStateDetail();

			detail.ButtonText = state.ToStateDisplayText();
			detail.ButtonColor = state.ToStateCss();
			detail.BaseType = (int)BuiltInStatusBaseTypes.ForUnitState(state.State);

			if (string.IsNullOrWhiteSpace(detail.ButtonColor))
				detail.ButtonColor = "label-default";

			return detail;
		}
	}
}
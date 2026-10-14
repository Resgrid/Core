using System.Linq;
using System.Threading.Tasks;
using Autofac;
using CommonServiceLocator;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models;

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

		/// <summary>
		/// Placeholder for a status row whose status can no longer be resolved (e.g. a deleted custom status), so call
		/// views and exports still list the row instead of failing to render.
		/// </summary>
		public static CustomStateDetail UnknownState(string text)
		{
			return new CustomStateDetail { ButtonText = text, ButtonColor = "label-default" };
		}

		/// <summary>
		/// Built-in statuses carry either a CSS label class or a hex colour; custom statuses carry a hex colour.
		/// </summary>
		public static bool IsHexColor(string color)
		{
			return !string.IsNullOrWhiteSpace(color) && color.TrimStart().StartsWith("#");
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

			var defaultStatuses = customStateService.GetDefaultUnitStatuses();

			var status = defaultStatuses.FirstOrDefault(x => x.CustomStateDetailId == state.State);

			if (status != null)
				return status;

			// Old code needed to support status we don't show by default anymore.
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
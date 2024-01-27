using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class LimitsService : ILimitsService
	{

		public LimitsService()
		{

		}

		/// <summary>
		/// Validates that a department is within the limits for their subscription plan.
		/// </summary>
		/// <param name="departmentId">DepartmentId to check the limits for</param>
		/// <returns>Return true if the department is within limits and false if they have exceeded them</returns>
		public async Task<bool> ValidateDepartmentIsWithinLimitsAsync(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentAddNewUserAsync(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentAddNewGroup(int departmentId)
		{
			return true;
		}

		public bool CanDepartmentAddNewRole(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentAddNewUnit(int departmentId)
		{
			return true;
		}

		public async Task<int> GetPersonnelLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			return int.MaxValue;
		}

		public async Task<int> GetGroupsLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			return int.MaxValue;
		}

		public int GetRolesLimitForDepartment(int departmentId)
		{
			return int.MaxValue;
		}

		public async Task<int> GetUnitsLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			return int.MaxValue;
		}

		public async Task<bool> CanDepartmentProvisionNumberAsync(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentUseVoiceAsync(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentUseLinksAsync(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentCreateOrdersAsync(int departmentId)
		{
			return true;
		}

		public async Task<DepartmentLimits> GetLimitsForEntityPlanWithFallbackAsync(int departmentId, bool bypassCache = false)
		{
			var departmentLimits = new DepartmentLimits();
			departmentLimits.PersonnelLimit = int.MaxValue;
			departmentLimits.UnitsLimit = int.MaxValue;
			departmentLimits.IsEntityPlan = false;

			return await Task.FromResult(departmentLimits);
		}

		public async Task<bool> InvalidateDepartmentsEntityLimitsCache(int departmentId)
		{
			return await Task.FromResult(true);
		}
	}
}

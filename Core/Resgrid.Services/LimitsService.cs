using System;
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
		public bool ValidateDepartmentIsWithinLimits(int departmentId)
		{
			return true;
		}

		public bool CanDepartentAddNewUser(int departmentId)
		{
			return true;
		}

		public bool CanDepartentAddNewGroup(int departmentId)
		{
			return true;
		}

		public bool CanDepartentAddNewRole(int departmentId)
		{
			return true;
		}

		public bool CanDepartentAddNewUnit(int departmentId)
		{
			return true;
		}

		public int GetPersonnelLimitForDepartment(int departmentId, Plan plan = null)
		{
			return int.MaxValue;
		}

		public int GetGroupslLimitForDepartment(int departmentId, Plan plan = null)
		{
			return int.MaxValue;
		}

		public int GetRoleslLimitForDepartment(int departmentId)
		{
			return int.MaxValue;
		}

		public int GetUnitslLimitForDepartment(int departmentId, Plan plan = null)
		{
			return int.MaxValue;
		}

		public bool CanDepartmentProvisionNumber(int departmentId)
		{
			return true;
		}

		public bool CanDepartmentUseVoice(int departmentId)
		{
			return true;
		}

		public bool CanDepartmentUseLinks(int departmentId)
		{
			return true;
		}

		public bool CanDepartmentCreateOrders(int departmentId)
		{
			return true;
		}
	}
}
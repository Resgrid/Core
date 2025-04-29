using System;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class LimitsService : ILimitsService
	{
		private static string PlanLimitsForDepartmentCacheKey = "DepartmentPlanLimits_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(14);

		private readonly ISubscriptionsService _subscriptionsService;

		private readonly ICacheProvider _cacheProvider;

		public LimitsService(ISubscriptionsService subscriptionsService, ICacheProvider cacheProvider)
		{
			_subscriptionsService = subscriptionsService;
			_cacheProvider = cacheProvider;
		}

		/// <summary>
		/// Validates that a department is within the limits for their subscription plan.
		/// </summary>
		/// <param name="departmentId">DepartmentId to check the limits for</param>
		/// <returns>Return true if the department is within limits and false if they have exceeded them</returns>
		public async Task<bool> ValidateDepartmentIsWithinLimitsAsync(int departmentId)
		{
			var departmentCount = await _subscriptionsService.GetPlanCountsForDepartmentAsync(departmentId);
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			if (departmentCount.UsersCount > await GetPersonnelLimitForDepartmentAsync(departmentId, plan))
				return false;

			if (departmentCount.GroupsCount > await GetGroupsLimitForDepartmentAsync(departmentId, plan))
				return false;

			if (departmentCount.UnitsCount > await GetUnitsLimitForDepartmentAsync(departmentId, plan))
				return false;

			return true;
		}

		public async Task<bool> CanDepartmentAddNewUserAsync(int departmentId)
		{
			//int userCount = (await _departmentsService.GetAllUsersForDepartmentUnlimitedMinusDisabledAsync(departmentId)).Count;

			//if (userCount >= await GetPersonnelLimitForDepartmentAsync(departmentId))
			//	return false;

			//return true;

			var limits = await GetLimitsForEntityPlanWithFallbackAsync(departmentId);

			if (limits.EntityTotal == 0 && limits.PersonnelCount >= limits.PersonnelLimit)
				return false;
			else if (limits.EntityTotal != 0 && limits.GetCurrentEntityTotal() >= limits.EntityTotal)
				return false;

			return true;
		}

		public async Task<bool> CanDepartmentAddNewGroup(int departmentId)
		{
			// As of 1/1/2024, all plans have unlimited groups
			return true;
		}

		public bool CanDepartmentAddNewRole(int departmentId)
		{
			return true;
		}

		public async Task<bool> CanDepartmentAddNewUnit(int departmentId)
		{
			var departmentCount = await _subscriptionsService.GetPlanCountsForDepartmentAsync(departmentId);

			if (departmentCount.UnitsCount >= await GetUnitsLimitForDepartmentAsync(departmentId))
				return false;

			return true;
		}

		public async Task<int> GetPersonnelLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			//if (plan == null)
			//	plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			//return plan.GetLimitForTypeAsInt(PlanLimitTypes.Personnel);

			var limits = await GetLimitsForEntityPlanWithFallbackAsync(departmentId);
			return limits.PersonnelLimit;
		}

		public async Task<int> GetEntityLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			if (plan == null)
				plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			return plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities);
		}

		public async Task<int> GetGroupsLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			// As of 1/1/2024, all plans have unlimited groups
			return int.MaxValue;
		}

		public int GetRolesLimitForDepartment(int departmentId)
		{
			return int.MaxValue;
		}

		public async Task<int> GetUnitsLimitForDepartmentAsync(int departmentId, Plan plan = null)
		{
			//if (plan == null)
			//	plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			//return plan.GetLimitForTypeAsInt(PlanLimitTypes.Units);
			var limits = await GetLimitsForEntityPlanWithFallbackAsync(departmentId);
			return limits.UnitsLimit;
		}

		public async Task<bool> CanDepartmentProvisionNumberAsync(int departmentId)
		{
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			if (plan.PlanId == 4 || plan.PlanId == 5 || plan.PlanId == 10 || plan.PlanId == 14 || plan.PlanId == 15 || plan.PlanId == 16 || plan.PlanId == 17 ||
				  plan.PlanId == 18 || plan.PlanId == 19 || plan.PlanId == 20 || plan.PlanId == 21 || plan.PlanId == 26 || plan.PlanId == 27 || plan.PlanId == 28 ||
					plan.PlanId == 29 || plan.PlanId == 30 || plan.PlanId == 31 || plan.PlanId == 32 || plan.PlanId == 33 || plan.PlanId == 36 || plan.PlanId == 37)
				return true;

			return false;
		}

		public async Task<bool> CanDepartmentUseVoiceAsync(int departmentId)
		{
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			if (plan.PlanId == 5 || plan.PlanId == 10 || plan.PlanId == 15 || plan.PlanId == 16 || plan.PlanId == 17 || plan.PlanId == 18 || plan.PlanId == 19 ||
				  plan.PlanId == 20 || plan.PlanId == 21 || plan.PlanId == 28 || plan.PlanId == 29 || plan.PlanId == 30 || plan.PlanId == 31 || plan.PlanId == 32 || plan.PlanId == 33
				  || plan.PlanId == 36 || plan.PlanId == 37)
				return true;

			return false;
		}

		public async Task<bool> CanDepartmentUseLinksAsync(int departmentId)
		{
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			if (plan.PlanId > 1)
				return true;

			return false;
		}

		public async Task<bool> CanDepartmentCreateOrdersAsync(int departmentId)
		{
			var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);

			if (plan.PlanId > 1)
				return true;

			return false;
		}

		public async Task<DepartmentLimits> GetLimitsForEntityPlanWithFallbackAsync(int departmentId, bool bypassCache = false)
		{
			async Task<DepartmentLimits> getCurrentPlanForDepartmentAsync()
			{
				var limits = new DepartmentLimits();
				var plan = await _subscriptionsService.GetCurrentPlanForDepartmentAsync(departmentId);
				var departmentCount = await _subscriptionsService.GetPlanCountsForDepartmentAsync(departmentId);
				limits.PersonnelCount = departmentCount.UsersCount;
				limits.UnitsCount = departmentCount.UnitsCount;

				if (plan != null && plan.PlanId >= 36)
				{
					limits.EntityTotal = plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities);
					limits.IsEntityPlan = true;

					if ((departmentCount.UsersCount + departmentCount.UnitsCount) <= plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities))
					{
						limits.PersonnelLimit = plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities);
						limits.UnitsLimit = plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities);

						return limits;
					}
					else
					{
						/* If we are over limit, we need to figure out the percent of users and units we are over the limit by
						 * and then return the % of the limit for each. This allows an more even cap on users and units based
						 * on how many they have of each, instead of just capping the users and units at the same number.
						 */
						var personnelPrecent = Math.Round((double)departmentCount.UsersCount / (departmentCount.UsersCount + departmentCount.UnitsCount), 2);
						var unitsPrecent = Math.Round((double)departmentCount.UnitsCount / (departmentCount.UsersCount + departmentCount.UnitsCount), 2);
						var total = plan.GetLimitForTypeAsInt(PlanLimitTypes.Entities);

						limits.PersonnelLimit = (int)(total * personnelPrecent);
						limits.UnitsLimit = (int)(total * unitsPrecent);

						return limits;
					}
				}
				else if ((!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey)) && plan.PlanId == 1)
				{
					limits.PersonnelLimit = plan.GetLimitForTypeAsInt(PlanLimitTypes.Personnel);
					limits.UnitsLimit = plan.GetLimitForTypeAsInt(PlanLimitTypes.Units);

					return limits;
				}
				else if (plan == null)
				{
					limits.PersonnelLimit = 10;
					limits.UnitsLimit = 10;

					return limits;
				}

				limits.PersonnelLimit = plan.GetLimitForTypeAsInt(PlanLimitTypes.Personnel);
				limits.UnitsLimit = plan.GetLimitForTypeAsInt(PlanLimitTypes.Units);

				return limits;
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
				return await _cacheProvider.RetrieveAsync<DepartmentLimits>(string.Format(PlanLimitsForDepartmentCacheKey, departmentId), getCurrentPlanForDepartmentAsync, CacheLength);
			else
				return await getCurrentPlanForDepartmentAsync();
		}

		public async Task<bool> InvalidateDepartmentsEntityLimitsCache(int departmentId)
		{
			return await _cacheProvider.RemoveAsync(string.Format(PlanLimitsForDepartmentCacheKey, departmentId));
		}
	}
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Department Import gate (wizard plan sections 5.1 and 14.2): the master flag plus a current department administrator.
	/// Permissions 72 and 73 are department-admins-only and never offered on the Security screen, so no saved row can widen
	/// them; checking current administrator status here is the fresh, service-layer form of the claim. Fails closed.
	/// </summary>
	public class DepartmentImportAccessService : IDepartmentImportAccessService
	{
		private readonly IFeatureToggleService _flags;
		private readonly IDepartmentsService _departmentsService;
		private readonly IImportArtifactStore _store;

		public DepartmentImportAccessService(IFeatureToggleService flags, IDepartmentsService departmentsService, IImportArtifactStore store)
		{
			_flags = flags;
			_departmentsService = departmentsService;
			_store = store;
		}

		public async Task<bool> IsEnabledAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				return (await _flags.EvaluateFreshAsync(FeatureFlagKeys.OnboardingMigrationWizard, departmentId))?.IsEnabled == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> IsManagedMigrationEnabledAsync(int departmentId)
		{
			if (!await IsEnabledAsync(departmentId))
				return false;

			try
			{
				return (await _flags.EvaluateFreshAsync(FeatureFlagKeys.OnboardingManagedMigration, departmentId))?.IsEnabled == true;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task<bool> CanManageAsync(int departmentId, string userId, bool records = false)
		{
			if (string.IsNullOrWhiteSpace(userId) || !await IsEnabledAsync(departmentId))
				return false;

			try
			{
				if (records && (await _flags.EvaluateFreshAsync(FeatureFlagKeys.OnboardingHistoricalRecords, departmentId))?.IsEnabled != true)
					return false;

				var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, true);
				if (!DepartmentMemberStateHelper.IsCurrentMember(member, departmentId))
					return false;

				if (member.IsAdmin == true)
					return true;

				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, true);
				return department != null && string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public Task<ImportStoreHealth> GetStoreHealthAsync(CancellationToken cancellationToken = default) => _store.CheckHealthAsync(cancellationToken);
	}
}

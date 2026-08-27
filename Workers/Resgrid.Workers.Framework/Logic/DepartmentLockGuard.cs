using System;
using System.Threading.Tasks;
using Autofac;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Workers.Framework.Logic
{
	/// <summary>
	/// Shared worker-side enforcement of the department operation lock (ADP plan section 20.2):
	/// queue consumers and scheduled-task executors call this before performing a department-scoped
	/// mutation and DEFER the item when locked — skip without completing so the scheduler re-picks
	/// it, or requeue, but never dead-letter and never process. Reads are unaffected.
	///
	/// Failure posture matches IDepartmentLockService: a lock-store fault reads as unlocked (fail
	/// open) — the lock protects a migration, dispatch availability beats migration progress, and
	/// the migration worker refuses to proceed when it cannot verify its own lock.
	/// </summary>
	public static class DepartmentLockGuard
	{
		public static async Task<bool> IsDepartmentLockedAsync(int departmentId)
		{
			if (departmentId <= 0)
				return false;

			try
			{
				var lockService = Bootstrapper.GetKernel().Resolve<IDepartmentLockService>();
				return await lockService.IsDepartmentLockedAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"DepartmentLockGuard failed for department {departmentId}; failing open (unlocked)");
				return false;
			}
		}
	}
}

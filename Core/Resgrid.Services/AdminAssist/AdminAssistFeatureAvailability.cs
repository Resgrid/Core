using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	/// <summary>Fresh rollout checks. Missing flags and evaluation failures disable the optional feature.</summary>
	public static class AdminAssistFeatureAvailability
	{
		public static async Task<bool> IsEnabledAsync(IFeatureToggleService flags, int departmentId, bool setup, CancellationToken ct = default)
		{
			ct.ThrowIfCancellationRequested();
			if (departmentId <= 0) return false;
			try
			{
				return (await flags.EvaluateFreshAsync(setup ? FeatureFlagKeys.AdminSetup : FeatureFlagKeys.AdminAssist, departmentId).WaitAsync(ct))?.IsEnabled == true;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception) { return false; }
		}

		public static async Task<bool> CanConfigureOperatingProfileAsync(IFeatureToggleService flags, int departmentId, CancellationToken ct = default) =>
			await IsEnabledAsync(flags, departmentId, true, ct) || await IsEnabledAsync(flags, departmentId, false, ct);
	}
}

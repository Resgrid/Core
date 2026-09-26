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
		public static Task<bool> IsEnabledAsync(IFeatureToggleService flags, int departmentId, bool setup, CancellationToken ct = default) =>
			IsFlagEnabledAsync(flags, setup ? FeatureFlagKeys.AdminSetup : FeatureFlagKeys.AdminAssist, departmentId, ct);

		/// <summary>
		/// The Admin Assist workspace (its menu entry and pages) launches with its AI, after the Setup Wizard and Setup Report.
		/// Admin.Assist alone still drives field help, impact previews and digests; the workspace also needs Ai.AdminAssist.
		/// </summary>
		public static async Task<bool> IsWorkspaceEnabledAsync(IFeatureToggleService flags, int departmentId, CancellationToken ct = default) =>
			await IsEnabledAsync(flags, departmentId, false, ct) && await IsFlagEnabledAsync(flags, FeatureFlagKeys.AiAdminAssist, departmentId, ct);

		public static async Task<bool> CanConfigureOperatingProfileAsync(IFeatureToggleService flags, int departmentId, CancellationToken ct = default) =>
			await IsEnabledAsync(flags, departmentId, true, ct) || await IsEnabledAsync(flags, departmentId, false, ct);

		private static async Task<bool> IsFlagEnabledAsync(IFeatureToggleService flags, string key, int departmentId, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			if (departmentId <= 0) return false;
			try
			{
				return (await flags.EvaluateFreshAsync(key, departmentId).WaitAsync(ct))?.IsEnabled == true;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
			catch (Exception) { return false; }
		}
	}
}

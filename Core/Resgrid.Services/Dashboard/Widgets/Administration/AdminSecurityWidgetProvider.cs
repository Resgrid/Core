using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Administration;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Administration
{
	/// <summary>
	/// admin.security (plan §7.11): Security &amp; Permissions headline numbers for department admins (SecurityController's rule).
	/// Three reads: the posture counts added for this widget (IUserSessionService.GetDepartmentSecurityPostureAsync — one aggregate
	/// over members, authenticators, passkeys and sessions), the SSO configurations and the security policy. Counts and switches only.
	/// </summary>
	[DashboardWidget("admin.security")]
	public class AdminSecurityWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IUserSessionService _userSessionService;
		private readonly IDepartmentSsoService _departmentSsoService;

		public AdminSecurityWidgetProvider(IUserSessionService userSessionService, IDepartmentSsoService departmentSsoService)
		{
			_userSessionService = userSessionService;
			_departmentSsoService = departmentSsoService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var posture = await _userSessionService.GetDepartmentSecurityPostureAsync(departmentId, cancellationToken) ?? new DepartmentSecurityPosture();
			var sso = (await _departmentSsoService.GetSsoConfigsForDepartmentAsync(departmentId, cancellationToken) ?? Enumerable.Empty<DepartmentSsoConfig>())
				.Where(c => c.DepartmentId == departmentId).ToList();
			var policy = await _departmentSsoService.GetSecurityPolicyForDepartmentAsync(departmentId, cancellationToken);

			return new AdminSecurityWidgetData
			{
				Members = posture.Members,
				MfaEnabled = posture.MfaEnabled,
				PasskeyUsers = posture.PasskeyUsers,
				StrongAuthentication = posture.StrongAuthentication,
				StrongAuthenticationPercent = posture.Members == 0 ? 0 : (int)Math.Round(posture.StrongAuthentication * 100.0 / posture.Members),
				ActiveSessions = posture.ActiveSessions,
				UsersWithSessions = posture.UsersWithSessions,
				SsoProviders = sso.Count,
				SsoEnabled = sso.Any(c => c.IsEnabled),
				ScimEnabled = sso.Any(c => c.IsEnabled && c.ScimEnabled),
				RequireMfa = policy?.RequireMfa == true,
				RequireSso = policy?.RequireSso == true,
				MaxConcurrentSessions = policy == null || policy.MaxConcurrentSessions <= 0 ? null : policy.MaxConcurrentSessions,
				SessionTimeoutMinutes = policy == null || policy.SessionTimeoutMinutes <= 0 ? null : policy.SessionTimeoutMinutes
			};
		}
	}
}

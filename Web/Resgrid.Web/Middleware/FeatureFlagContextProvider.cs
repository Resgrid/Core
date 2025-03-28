using Microsoft.AspNetCore.Http;
using PostHog.FeatureManagement;
using PostHog;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Middleware
{
	/// <summary>
	/// Provides context and options used to evaluate feature flags for the Resgrid application.
	/// </summary>
	/// <param name="httpContextAccessor">The <see cref="IHttpContextAccessor"/> used to get the current user's Id.</param>
	public class FeatureFlagContextProvider(IHttpContextAccessor httpContextAccessor)
		: PostHogFeatureFlagContextProvider
	{
		protected override string? GetDistinctId() =>
			httpContextAccessor.HttpContext?.User.Identity?.Name;

		protected override ValueTask<FeatureFlagOptions> GetFeatureFlagOptionsAsync()
		{
			return ValueTask.FromResult(
				new FeatureFlagOptions
				{
					PersonProperties = new Dictionary<string, object?>
					{
						["email"] = ClaimsAuthorizationHelper.GetEmailAddress(),
					},
					Groups = [
						new Group("departmentId", ClaimsAuthorizationHelper.GetDepartmentId().ToString())
					],
					OnlyEvaluateLocally = true
				});
		}
	}
}

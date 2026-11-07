using System;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// A member count behind a department module switch (DepartmentModuleSettings) and, optionally, a feature flag: the
	/// gates the dashboard applies to the same module's widgets. Nothing is counted when either is off.
	/// </summary>
	public abstract class ModuleBadgeCountProvider : IBadgeCountProvider
	{
		private readonly IFeatureToggleService _featureToggleService;

		protected ModuleBadgeCountProvider(IFeatureToggleService featureToggleService)
		{
			_featureToggleService = featureToggleService;
		}

		public abstract string Key { get; }

		public abstract string RequiredPolicy { get; }

		/// <summary>The department feature flag the module needs, or null for none.</summary>
		protected virtual string RequiredFlag => null;

		/// <summary>Whether the department switched the module off.</summary>
		protected abstract bool IsDisabled(DepartmentModuleSettings settings);

		protected abstract Task<BadgeCount> CountAsync(BadgeCountContext context);

		public async Task<BadgeCount> GetCountAsync(BadgeCountContext context)
		{
			var settings = await context.GetModuleSettingsAsync();

			if (settings != null && IsDisabled(settings))
				return null;

			if (RequiredFlag != null && (_featureToggleService == null || !await _featureToggleService.IsEnabledAsync(RequiredFlag, context.DepartmentId)))
				return null;

			return await CountAsync(context);
		}
	}
}

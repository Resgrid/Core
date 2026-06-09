using System.Text;
using Resgrid.Config;
using Resgrid.Framework;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Central, config-gated entry point for scrubbing entity string values before they are bound
	/// to a SQL command via Dapper's <c>DynamicParameters(entity)</c> (which bypasses
	/// <see cref="DynamicParametersExtension.Add"/>). Keeps the UTF-8 guard logic in one place.
	/// </summary>
	internal static class Utf8WriteGuard
	{
		/// <summary>
		/// In-place sanitizes the string properties of <paramref name="entity"/> when UTF-8 guarding
		/// is enabled. No-op when disabled or when the entity is null.
		/// </summary>
		public static void Sanitize(object entity)
		{
			if (entity == null || !SystemBehaviorConfig.SanitizeTextForUtf8)
				return;

			Utf8Sanitizer.CleanEntity(entity,
				SystemBehaviorConfig.Utf8RepairDoubleEncoding,
				SystemBehaviorConfig.Utf8NormalizeToNfc ? NormalizationForm.FormC : (NormalizationForm?)null);
		}
	}
}

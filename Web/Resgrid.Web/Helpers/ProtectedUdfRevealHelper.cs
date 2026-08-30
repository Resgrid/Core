using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Adds a record's user-defined field values to an ADP reveal response (plan 7.2).
	///
	/// Every server-rendered page that hosts a UDF form or read-only block has the same problem:
	/// udffieldvalues.value is cataloged, so a protected department's custom fields render as the
	/// REDACTED placeholder, and revealing the record while leaving its custom fields showing the
	/// placeholder is an odd half-reveal of one record. The key shape
	/// ("udffieldvalues.value:{udfFieldId}") matches what UdfRenderingService writes into
	/// data-adp-field, so the client module fills the right control without knowing anything about
	/// user-defined fields.
	///
	/// Services are passed in rather than resolved: every caller is a controller that already has
	/// them injected.
	/// </summary>
	public static class ProtectedUdfRevealHelper
	{
		/// <param name="isDepartmentAdmin">
		/// The caller's department-admin status, and <paramref name="isGroupAdmin"/> their group-admin
		/// status. Both are REQUIRED rather than optional: a grant proves the caller stepped up, it is
		/// not a field-visibility decision, so a reveal must hide exactly what its hosting page hides.
		/// Making them parameters means a new reveal endpoint cannot forget the check.
		/// </param>
		/// <returns>
		/// The resolution result, or null when the record has no custom field values at all. A
		/// caller whose page has nothing else protected uses it to answer the client with the
		/// machine-readable reason (an expired grant must re-prompt, not silently show placeholders).
		/// </returns>
		public static async Task<ProtectedReadResult> AddUdfValuesAsync(IDictionary<string, string> fields,
			IUserDefinedFieldsService userDefinedFieldsService, IProtectedReadService protectedReadService,
			int departmentId, UdfEntityType entityType, string entityId, string grantToken, string userId,
			bool isDepartmentAdmin, bool isGroupAdmin)
		{
			if (fields == null || string.IsNullOrWhiteSpace(entityId))
				return null;

			var values = await userDefinedFieldsService.GetFieldValuesForEntityAsync(departmentId,
				(int)entityType, entityId);

			if (values == null || !values.Any())
				return null;

			// Field visibility is decided the same way the hosting page decides it. Without this a
			// caller holding a grant but neither department nor group admin would receive, through
			// the reveal response, the very values the page filtered out of the render.
			var visibleFields = await userDefinedFieldsService.GetVisibleFieldsForActiveDefinitionAsync(
				departmentId, (int)entityType, isDepartmentAdmin, isGroupAdmin);

			var visibleFieldIds = (visibleFields ?? new List<UdfField>())
				.Where(f => !string.IsNullOrEmpty(f.UdfFieldId))
				.Select(f => f.UdfFieldId)
				.ToHashSet(StringComparer.Ordinal);

			values = values.Where(v => v != null && !string.IsNullOrEmpty(v.UdfFieldId)
				&& visibleFieldIds.Contains(v.UdfFieldId)).ToList();

			if (!values.Any())
				return null;

			var result = await protectedReadService.ResolveUdfFieldValuesForReadAsync(departmentId, values,
				grantToken, userId);

			// A value the caller's grant could not open comes back as the placeholder; the client
			// treats that as "nothing to write" and leaves the field concealed, so it is passed
			// through unchanged rather than filtered here.
			foreach (var value in values)
				fields[$"udffieldvalues.value:{value.UdfFieldId}"] = value.Value;

			return result;
		}
	}
}

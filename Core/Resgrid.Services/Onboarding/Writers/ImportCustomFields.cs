using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding.Writers
{
	/// <summary>
	/// "Keep as custom field" columns (wizard plan section 7.5). Values go to the active custom-field definition's fields
	/// whose label matches the kept column. Publishing a definition creates a new version and every value saved against
	/// the previous one stops showing, so the import publishes one only when the department has no definition for that
	/// record kind yet; otherwise the administrator adds the fields on the Custom Fields screen first (L7 issue).
	/// </summary>
	public static class ImportCustomFields
	{
		/// <summary>Labels the active definition is missing, and whether the import may add them by publishing a first definition.</summary>
		public static async Task<(List<string> Missing, bool CanPublish)> CheckAsync(IUserDefinedFieldsService udf, int departmentId, UdfEntityType entityType,
			IEnumerable<string> labels)
		{
			var wanted = labels.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (wanted.Count == 0)
				return (new List<string>(), true);

			var definition = await udf.GetActiveDefinitionAsync(departmentId, (int)entityType);
			var fields = definition == null ? new List<UdfField>() : await udf.GetFieldsForActiveDefinitionAsync(departmentId, (int)entityType) ?? new List<UdfField>();
			var known = new HashSet<string>(fields.Select(f => ImportFieldConverter.Normalize(f.Label ?? f.Name)), StringComparer.Ordinal);
			var missing = wanted.Where(l => !known.Contains(ImportFieldConverter.Normalize(l))).ToList();
			return (missing, definition == null);
		}

		/// <summary>
		/// Publishes the first definition with text fields for <paramref name="labels"/>, visible to department admins only
		/// (imported free text may be sensitive). Commits its own unit of work: never call it inside a chunk transaction.
		/// </summary>
		public static async Task<bool> PublishFirstDefinitionAsync(IUserDefinedFieldsService udf, int departmentId, UdfEntityType entityType,
			IReadOnlyList<string> labels, string userId, CancellationToken cancellationToken)
		{
			var (missing, canPublish) = await CheckAsync(udf, departmentId, entityType, labels);
			if (missing.Count == 0)
				return true;
			if (!canPublish)
				return false;

			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var fields = missing.Select((label, i) => new UdfField
			{
				Name = UniqueName(label, used),
				Label = label.Length > 100 ? label.Substring(0, 100) : label,
				FieldDataType = (int)UdfFieldDataType.Text,
				IsEnabled = true,
				IsVisibleOnReports = true,
				Visibility = (int)UdfFieldVisibility.DepartmentAdminsOnly,
				SortOrder = i
			}).ToList();

			await udf.SaveDefinitionAsync(departmentId, (int)entityType, fields, userId, cancellationToken);
			return true;
		}

		/// <summary>Values for the kept columns whose label matches an enabled field of the active definition.</summary>
		public static async Task<List<UdfFieldValue>> BuildValuesAsync(IUserDefinedFieldsService udf, int departmentId, UdfEntityType entityType,
			IReadOnlyDictionary<string, string> kept)
		{
			var values = new List<UdfFieldValue>();
			if (kept == null || kept.Count == 0)
				return values;

			var fields = await udf.GetFieldsForActiveDefinitionAsync(departmentId, (int)entityType) ?? new List<UdfField>();
			var byLabel = fields.Where(f => f.IsEnabled).GroupBy(f => ImportFieldConverter.Normalize(f.Label ?? f.Name)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
			foreach (var (label, value) in kept)
				if (!string.IsNullOrWhiteSpace(value) && byLabel.TryGetValue(ImportFieldConverter.Normalize(label), out var field))
					values.Add(new UdfFieldValue { UdfFieldId = field.UdfFieldId, UdfDefinitionId = field.UdfDefinitionId, EntityType = (int)entityType, Value = value });

			return values;
		}

		private static string UniqueName(string label, HashSet<string> used)
		{
			var stem = new string(ImportFieldConverter.Normalize(label).Select(c => c < 128 && char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
			if (stem.Length == 0 || !(stem[0] < 128 && char.IsLetter(stem[0])))
				stem = "imported_" + stem;
			if (stem.Length > 50)
				stem = stem.Substring(0, 50);

			var name = stem;
			for (var i = 2; !used.Add(name); i++)
				name = stem + "_" + i;
			return name;
		}
	}
}

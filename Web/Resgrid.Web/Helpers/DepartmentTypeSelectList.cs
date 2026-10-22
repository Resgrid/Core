using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Builds the organization type picker from <see cref="DepartmentTypeCatalog"/>, grouped, with names from the Department
	/// resources (<c>OrgType.{key}</c>, <c>OrgTypeGroup.{group}</c>). Used by sign-up and Department -> Settings so the two
	/// lists cannot drift apart.
	/// </summary>
	public static class DepartmentTypeSelectList
	{
		/// <param name="departmentLocalizer">The Department area localizer.</param>
		/// <param name="currentValue">
		/// The department's stored type. When it is not a catalog value (an older sign-up list) it is offered as the first
		/// option, so saving the page keeps it rather than silently changing it.
		/// </param>
		/// <param name="offerNotSet">Offer a blank "Not set" option when the department has no type yet.</param>
		public static List<SelectListItem> Build(IStringLocalizer departmentLocalizer, string currentValue, bool offerNotSet)
		{
			var items = new List<SelectListItem>();

			if (string.IsNullOrWhiteSpace(currentValue))
			{
				if (offerNotSet)
					items.Add(new SelectListItem(Localize(departmentLocalizer, "OrgTypeNotSet", "Not set"), string.Empty));
			}
			else if (DepartmentTypeCatalog.Find(currentValue) == null)
			{
				items.Add(new SelectListItem(currentValue, currentValue));
			}

			var groups = new Dictionary<DepartmentTypeGroups, SelectListGroup>();
			foreach (var type in DepartmentTypeCatalog.All)
			{
				if (!groups.TryGetValue(type.Group, out var group))
				{
					group = new SelectListGroup { Name = Localize(departmentLocalizer, "OrgTypeGroup." + type.Group, type.Group.ToString()) };
					groups.Add(type.Group, group);
				}

				items.Add(new SelectListItem(Localize(departmentLocalizer, "OrgType." + type.Key, type.Value), type.Value) { Group = group });
			}

			return items;
		}

		private static string Localize(IStringLocalizer localizer, string key, string fallback)
		{
			if (localizer == null)
				return fallback;

			var text = localizer[key];
			return text == null || text.ResourceNotFound || string.IsNullOrWhiteSpace(text.Value) ? fallback : text.Value;
		}
	}
}

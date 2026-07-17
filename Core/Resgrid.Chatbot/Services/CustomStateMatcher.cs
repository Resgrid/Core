using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Chatbot.Services
{
	internal static class CustomStateMatcher
	{
		public static CustomStateDetail FindById(IEnumerable<CustomStateDetail> details, int detailId)
			=> details?.FirstOrDefault(x => x != null && !x.IsDeleted && x.CustomStateDetailId == detailId);

		public static CustomStateDetail FindByName(IEnumerable<CustomStateDetail> details, params string[] names)
		{
			var normalizedNames = (names ?? new string[0])
				.Select(Normalize)
				.Where(x => x.Length > 0)
				.ToList();

			return details?.FirstOrDefault(detail => detail != null && !detail.IsDeleted
				&& normalizedNames.Contains(Normalize(detail.ButtonText)));
		}

		public static CustomStateDetail FindByBaseType(IEnumerable<CustomStateDetail> details,
			params ActionBaseTypes[] baseTypes)
		{
			foreach (var baseType in baseTypes ?? new ActionBaseTypes[0])
			{
				var match = details?.FirstOrDefault(detail => detail != null && !detail.IsDeleted
					&& detail.CustomStateDetailId > 25
					&& detail.BaseType == (int)baseType);
				if (match != null)
					return match;
			}

			return null;
		}

		public static CustomStateDetail FindBySelection(IEnumerable<CustomStateDetail> details, string value)
		{
			var active = details?.Where(x => x != null && !x.IsDeleted).OrderBy(x => x.Order).ToList();
			if (active == null || !int.TryParse(value?.Trim().TrimStart('s', 'S'), out var selection)
				|| selection < 1 || selection > active.Count)
				return null;

			return active[selection - 1];
		}

		public static ActionBaseTypes? PersonnelBaseTypeFor(string value)
		{
			return Normalize(value) switch
			{
				"responding" => ActionBaseTypes.Responding,
				"respondingtoscene" => ActionBaseTypes.Responding,
				"onmyway" => ActionBaseTypes.Responding,
				"omw" => ActionBaseTypes.Responding,
				"enroute" => ActionBaseTypes.Enroute,
				"notresponding" => ActionBaseTypes.NotResponding,
				"notgoing" => ActionBaseTypes.NotResponding,
				"onscene" => ActionBaseTypes.OnScene,
				"standingby" => ActionBaseTypes.Standby,
				"standby" => ActionBaseTypes.Standby,
				"available" => ActionBaseTypes.Available,
				"availablestation" => ActionBaseTypes.Available,
				"unavailable" => ActionBaseTypes.Unavailable,
				_ => null
			};
		}

		public static ActionBaseTypes? UnitBaseTypeFor(string value)
		{
			return Normalize(value) switch
			{
				"available" => ActionBaseTypes.Available,
				"inservice" => ActionBaseTypes.Available,
				"responding" => ActionBaseTypes.Responding,
				"enroute" => ActionBaseTypes.Enroute,
				"onmyway" => ActionBaseTypes.Enroute,
				"onscene" => ActionBaseTypes.OnScene,
				"staging" => ActionBaseTypes.Staging,
				"returning" => ActionBaseTypes.Returning,
				"unavailable" => ActionBaseTypes.Unavailable,
				"outofservice" => ActionBaseTypes.Unavailable,
				"maintenance" => ActionBaseTypes.Maintenance,
				_ => null
			};
		}

		public static string Normalize(string value)
			=> string.IsNullOrWhiteSpace(value)
				? string.Empty
				: new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
	}
}

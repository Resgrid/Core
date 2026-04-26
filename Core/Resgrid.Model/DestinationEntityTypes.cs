using System;
using Microsoft.Extensions.Localization;

namespace Resgrid.Model
{
	public enum DestinationEntityTypes
	{
		None = 0,
		Station = 1,
		Call = 2,
		Poi = 3
	}

	public static class DestinationEntityTypeExtensions
	{
		public static DestinationEntityTypes ToDestinationEntityType(this int? destinationType)
		{
			if (!destinationType.HasValue || !Enum.IsDefined(typeof(DestinationEntityTypes), destinationType.Value))
				return DestinationEntityTypes.None;

			return (DestinationEntityTypes)destinationType.Value;
		}

		public static string GetDisplayName(this DestinationEntityTypes destinationType, IStringLocalizer localizer)
		{
			switch (destinationType)
			{
				case DestinationEntityTypes.Station:
					return localizer["Station"];
				case DestinationEntityTypes.Call:
					return localizer["Call"];
				case DestinationEntityTypes.Poi:
					return localizer["POI"];
				default:
					return string.Empty;
			}
		}
	}
}

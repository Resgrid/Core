using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Web.Areas.User.Models.DepartmentImport
{
	/// <summary>
	/// The day/month question (wizard plan section 6.4): "Is 03/04/2026 the 3rd of April or March 4th?", asked once per column
	/// with both readings shown in the administrator's language. The answers are column formats; any separator reads alike.
	/// </summary>
	public static class ImportDateQuestion
	{
		public const string DayFirst = "dd/MM/yyyy";
		public const string MonthFirst = "MM/dd/yyyy";
		public const string YearFirst = "yyyy-MM-dd";

		/// <summary>The stand-in when the column has no example that reads both ways.</summary>
		public const string StandIn = "03/04/2026";

		/// <summary>A value from the column that reads both ways, else the stand-in.</summary>
		public static string Sample(IEnumerable<string> examples) =>
			(examples ?? Enumerable.Empty<string>()).Select(e => e?.Trim()).FirstOrDefault(e => Parts(e) is { } p && p.A <= 12 && p.B <= 12 && p.A != p.B) ?? StandIn;

		/// <summary>The sample read day first or month first ("3 April 2026"), or the sample itself when it is not a date.</summary>
		public static string Render(string sample, bool dayFirst, CultureInfo culture)
		{
			if (Parts(sample) is not { } p)
				return sample;
			try
			{
				var date = dayFirst ? new DateTime(p.Year, p.B, p.A) : new DateTime(p.Year, p.A, p.B);
				return date.ToString("d MMMM yyyy", culture);
			}
			catch (ArgumentOutOfRangeException)
			{
				return sample;
			}
		}

		private static (int A, int B, int Year)? Parts(string value)
		{
			// "03/04/2026 14:00" asks the same question as its date.
			var parts = (value ?? string.Empty).Trim().Split(' ', 'T')[0].Split('/', '.', '-');
			return parts.Length == 3 && parts[2].Length == 4 && int.TryParse(parts[0], out var a) && int.TryParse(parts[1], out var b) &&
				int.TryParse(parts[2], out var year) && a >= 1 && b >= 1
				? (a, b, year)
				: null;
		}
	}
}

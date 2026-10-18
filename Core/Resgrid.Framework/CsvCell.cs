using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Framework
{
	/// <summary>
	/// CSV cell formatting shared by every export that can carry user-entered text: quotes the cell and neutralises
	/// spreadsheet formula prefixes (= + - @ tab CR) so a value can never run as a formula when the file is opened.
	/// </summary>
	public static class CsvCell
	{
		private const string FormulaPrefixes = "=+-@\t\r";

		/// <summary>Quotes a CSV cell and neutralises spreadsheet formula prefixes in user-entered text.</summary>
		public static string Format(string value)
		{
			if (string.IsNullOrEmpty(value))
				return string.Empty;

			if (FormulaPrefixes.IndexOf(value[0]) >= 0)
				value = "'" + value;

			return "\"" + value.Replace("\"", "\"\"") + "\"";
		}

		/// <summary>One CSV line (no terminator) from cells, each formatted with <see cref="Format"/>.</summary>
		public static string Line(IEnumerable<string> cells) => string.Join(",", (cells ?? Enumerable.Empty<string>()).Select(Format));
	}
}

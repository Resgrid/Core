using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenPop.Mime.Decode
{
	/// <summary>
	/// Thanks to http://stackoverflow.com/a/7333402/477854 for inspiration
	/// This class can convert from strings like "104 kB" (104 kilobytes) to bytes.
	/// It does not know about differences such as kilobits vs kilobytes.
	/// </summary>
	static class SizeParser
	{
		private static readonly Dictionary<string, long> UnitsToMultiplicator = InitializeSizes();

		private static Dictionary<string, long> InitializeSizes()
		{
			return new Dictionary<string, long>
			{ 
				{ "", 1L },  // No unit is the same as a byte
				{ "B", 1L }, // Byte
				{ "KB", 1024L }, // Kilobyte
				{ "MB", 1024L * 1024L}, // Megabyte
				{ "GB", 1024L * 1024L * 1024L}, // Gigabyte
				{ "TB", 1024L * 1024L * 1024L * 1024L} // Terabyte
			};
		}

		public static long Parse(string value)
		{
			value = value.Trim();

			string unit = ExtractUnit(value);
			string valueWithoutUnit = value.Substring(0, value.Length - unit.Length).Trim();

			long multiplicatorForUnit = MultiplicatorForUnit(unit);

			double size = double.Parse(valueWithoutUnit, NumberStyles.Number, CultureInfo.InvariantCulture);

			return (long)(multiplicatorForUnit * size);
		}

		private static string ExtractUnit(string sizeWithUnit)
		{
			// start right, end at the first digit
			int lastChar = sizeWithUnit.Length - 1;
			int unitLength = 0;

			while (unitLength <= lastChar
				&& sizeWithUnit[lastChar - unitLength] != ' '       // stop when a space
				&& !IsDigit(sizeWithUnit[lastChar - unitLength]))   // or digit is found
			{
				unitLength++;
			}

			return sizeWithUnit.Substring(sizeWithUnit.Length - unitLength).ToUpperInvariant();
		}

		private static bool IsDigit(char value)
		{
			// we don't want to use char.IsDigit since it would accept esoterical unicode digits
			return value >= '0' && value <= '9';
		}

		private static long MultiplicatorForUnit(string unit)
		{
			unit = unit.ToUpperInvariant();

			if (!UnitsToMultiplicator.ContainsKey(unit))
				throw new ArgumentException("illegal or unknown unit: \"" + unit + "\"", "unit");

			return UnitsToMultiplicator[unit];
		}
	}
}
using System;
using System.Text;

namespace Resgrid.Model
{
	/// <summary>
	/// Plus Codes (Open Location Code) decoded locally (maplibre-mapping-routing-navigation-plan.md D25), so they keep
	/// working when the install's geocoding chain has no Google. Full codes ("849VCWC8+R9") decode on their own; short
	/// codes ("CWC8+R9") are recovered against a nearby reference point, as the reference library does.
	/// </summary>
	public static class OpenLocationCode
	{
		private const string Alphabet = "23456789CFGHJMPQRVWX";
		private const int SeparatorPosition = 8;
		private const int PairCodeLength = 10;
		private const int GridColumns = 4;
		private const int GridRows = 5;

		/// <summary>True when <paramref name="code"/> (first token) is a full code; returns the centre of its area.</summary>
		public static bool TryDecodeFull(string code, out double latitude, out double longitude)
		{
			latitude = 0;
			longitude = 0;

			var token = FirstToken(code);
			if (token == null || token.IndexOf('+') != SeparatorPosition)
				return false;

			return TryDecode(token, out latitude, out longitude);
		}

		/// <summary>True when the first token is a short code; recovers it against the reference point.</summary>
		public static bool TryRecoverShort(string code, double referenceLatitude, double referenceLongitude, out double latitude, out double longitude)
		{
			latitude = 0;
			longitude = 0;

			var token = FirstToken(code);
			if (token == null)
				return false;

			var separator = token.IndexOf('+');
			if (separator < 2 || separator >= SeparatorPosition || separator % 2 != 0)
				return false;

			var paddingLength = SeparatorPosition - separator;
			var resolution = Math.Pow(20, 2 - paddingLength / 2);
			var halfResolution = resolution / 2.0;

			var prefix = Encode(referenceLatitude, referenceLongitude).Substring(0, paddingLength);
			if (!TryDecode(prefix + token, out latitude, out longitude))
				return false;

			if (referenceLatitude + halfResolution < latitude && latitude - resolution >= -90)
				latitude -= resolution;
			else if (referenceLatitude - halfResolution > latitude && latitude + resolution <= 90)
				latitude += resolution;

			if (referenceLongitude + halfResolution < longitude)
				longitude -= resolution;
			else if (referenceLongitude - halfResolution > longitude)
				longitude += resolution;

			return true;
		}

		/// <summary>The 10-digit code for a point (used to recover short codes).</summary>
		public static string Encode(double latitude, double longitude)
		{
			var lat = Math.Min(Math.Max(latitude, -90), 90) + 90;
			if (lat >= 180)
				lat = 180 - 0.000001;

			var lng = longitude;
			while (lng < -180)
				lng += 360;
			while (lng >= 180)
				lng -= 360;
			lng += 180;

			var builder = new StringBuilder();
			var place = 20.0;

			for (var pair = 0; pair < PairCodeLength / 2; pair++)
			{
				var latDigit = (int)Math.Floor(lat / place);
				var lngDigit = (int)Math.Floor(lng / place);
				lat -= latDigit * place;
				lng -= lngDigit * place;
				builder.Append(Alphabet[Math.Min(latDigit, 19)]).Append(Alphabet[Math.Min(lngDigit, 19)]);
				place /= 20;

				if (builder.Length == SeparatorPosition)
					builder.Append('+');
			}

			return builder.ToString();
		}

		private static bool TryDecode(string code, out double latitude, out double longitude)
		{
			latitude = 0;
			longitude = 0;

			var digits = code.Replace("+", string.Empty).Replace("0", string.Empty).ToUpperInvariant();
			if (digits.Length < 2 || digits.Length % 2 != 0 && digits.Length <= PairCodeLength)
				return false;

			double lat = 0, lng = 0, latSize = 20, lngSize = 20, place = 20;

			for (var i = 0; i < Math.Min(digits.Length, PairCodeLength); i += 2)
			{
				var latIndex = Alphabet.IndexOf(digits[i]);
				var lngIndex = Alphabet.IndexOf(digits[i + 1]);
				if (latIndex < 0 || lngIndex < 0)
					return false;

				lat += latIndex * place;
				lng += lngIndex * place;
				latSize = place;
				lngSize = place;
				place /= 20;
			}

			for (var i = PairCodeLength; i < digits.Length; i++)
			{
				var index = Alphabet.IndexOf(digits[i]);
				if (index < 0)
					return false;

				latSize /= GridRows;
				lngSize /= GridColumns;
				lat += (index / GridColumns) * latSize;
				lng += (index % GridColumns) * lngSize;
			}

			latitude = Math.Round(lat + latSize / 2 - 90, 7);
			longitude = Math.Round(lng + lngSize / 2 - 180, 7);
			return true;
		}

		private static string FirstToken(string code)
		{
			if (string.IsNullOrWhiteSpace(code))
				return null;

			var token = code.Trim().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();

			foreach (var character in token)
			{
				if (character != '+' && character != '0' && Alphabet.IndexOf(character) < 0)
					return null;
			}

			return token.Contains('+') ? token : null;
		}
	}
}

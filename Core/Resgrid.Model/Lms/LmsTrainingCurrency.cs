using System;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// The department's training currency (round 9C, region neutrality): one ISO 4217 code, kept in Trainings → Programs → Settings
	/// (<see cref="LmsProgramSettings.RequestCurrency"/>, setting 144), that training requests, class costs, public class fees and the cost
	/// roll-ups default to. Every LMS default goes through here, so nothing assumes US dollars: a department in Canada sets CAD, one in the EU
	/// EUR. Until a department chooses one, the default follows the cluster's billing provider (EUR on the EU cluster, USD otherwise).
	/// </summary>
	public static class LmsTrainingCurrency
	{
		/// <summary>The upper-case three-letter code, or null when the text is not one.</summary>
		public static string Normalize(string currency)
		{
			var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
			return code.Length == 3 && code.All(c => c >= 'A' && c <= 'Z') ? code : null;
		}

		/// <summary>The currency a department that never chose one gets: EUR where the cluster bills in euros (Paddle), USD elsewhere.</summary>
		public static string SystemDefault
		{
			get
			{
				try
				{
					return Resgrid.Config.PaymentProviderConfig.IsPaddleActive() ? "EUR" : "USD";
				}
				catch (Exception)
				{
					return "USD";
				}
			}
		}

		/// <summary>The first valid code of <paramref name="candidates"/> (most specific first), else <see cref="SystemDefault"/>.</summary>
		public static string Resolve(params string[] candidates)
		{
			foreach (var candidate in candidates ?? Array.Empty<string>())
			{
				var code = Normalize(candidate);
				if (code != null)
					return code;
			}

			return SystemDefault;
		}

		/// <summary>
		/// An amount for a page or an e-mail in the viewer's culture: the culture's number format with the ISO code after it ("1 250,00 EUR",
		/// "1,250.00 CAD"), never a currency symbol that could mean another country's dollar.
		/// </summary>
		public static string Format(decimal amount, string currency, CultureInfo culture)
		{
			var text = amount.ToString("N2", culture ?? CultureInfo.InvariantCulture);
			var code = Normalize(currency);
			return code == null ? text : text + " " + code;
		}
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Resgrid.Model.WorkOrders
{
    public static class WorkOrderCurrencies
    {
        // Always present so a runtime without ICU data (globalization-invariant mode returns no specific
        // cultures) still validates the currencies departments are most likely to have saved.
        private static readonly IReadOnlyDictionary<string, string> Baseline = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AUD"] = "Australian Dollar", ["CAD"] = "Canadian Dollar", ["CHF"] = "Swiss Franc", ["DKK"] = "Danish Krone", ["EUR"] = "Euro",
            ["GBP"] = "British Pound", ["JPY"] = "Japanese Yen", ["MXN"] = "Mexican Peso", ["NOK"] = "Norwegian Krone", ["NZD"] = "New Zealand Dollar",
            ["PLN"] = "Polish Zloty", ["SEK"] = "Swedish Krona", ["USD"] = "US Dollar"
        };

        // The runtime's region catalog feeds both the selector and server validation; the baseline is merged in.
        public static IReadOnlyDictionary<string, string> Options { get; } = Load();

        public static bool IsSupported(string currency) => currency != null && Options.ContainsKey(currency);

        private static IReadOnlyDictionary<string, string> Load()
        {
            var options = new SortedDictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
                {
                    RegionInfo region;
                    try { region = new RegionInfo(culture.Name); } catch (ArgumentException) { continue; }
                    var code = region.ISOCurrencySymbol;
                    // ICU reports "XXX" or the "¤¤" placeholder for world/region cultures; ISO 4217 codes are three ASCII letters.
                    if (!IsIsoCode(code) || code == "XXX" || options.ContainsKey(code)) continue;
                    options[code] = string.IsNullOrWhiteSpace(region.CurrencyEnglishName) ? code : region.CurrencyEnglishName;
                }
            }
            catch (Exception ex)
            {
                // A broken culture catalog must not poison the type initializer, but a silent fallback to the
                // baseline would hide why a department's saved currency stopped validating.
                Framework.Logging.LogError(ex, "Currency catalog discovery failed; only the baseline currencies are available.");
                options.Clear();
            }
            foreach (var pair in Baseline) options.TryAdd(pair.Key, pair.Value);
            return options;
        }

        private static bool IsIsoCode(string code) => code?.Length == 3 && code[0] is >= 'A' and <= 'Z' && code[1] is >= 'A' and <= 'Z' && code[2] is >= 'A' and <= 'Z';
    }
}

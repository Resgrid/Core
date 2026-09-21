using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Resgrid.Model.WorkOrders
{
    public static class WorkOrderCurrencies
    {
        // Use the runtime's region catalog for both the selector and server validation.
        public static IReadOnlyDictionary<string, string> Options { get; } = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => new RegionInfo(c.Name))
            .Where(r => r.ISOCurrencySymbol != "XXX")
            .GroupBy(r => r.ISOCurrencySymbol, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().CurrencyEnglishName, StringComparer.Ordinal);

        public static bool IsSupported(string currency) => currency != null && Options.ContainsKey(currency);
    }
}

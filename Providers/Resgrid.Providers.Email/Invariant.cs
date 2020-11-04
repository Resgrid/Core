using System;
using System.Globalization;

namespace Resgrid.Providers.EmailProvider
{
	internal static class Invariant
    {
        public static void IsNotNull(object target, string parameterName)
        {
            if (target == null)
            {
                throw new ArgumentNullException(parameterName);
            }
        }

        public static void IsNotBlank(string target, string parameterName)
        {
            if (string.IsNullOrEmpty(target))
            {
                throw new ArgumentException(string.Format(CultureInfo.CurrentUICulture, "\"{0}\" cannot be blank.", parameterName));
            }
        }
    }
}
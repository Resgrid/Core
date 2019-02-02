#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Config {

    internal static class XElementExtension {

        public static string GetAttributeValue(this XElement element, string name, bool required = true) {
            var attribute = element == null ? null : element.Attribute(XName.Get(name));
            if (attribute == null) {
                if (required && element != null)
                    throw new TemplateConfigurationException("required attribute '{0}' missing at element '{1}'.".FormatWith(name, element.Name));
                return null;
            }
            return attribute.Value;
        }

        // convenience implementation for fluency reasons and being able to report a clear configuration error
        public static XElement SingleOrDefault(this IEnumerable<XElement> elements, XElement defaultValue) {
            if (elements == null || elements.Count() == 0)
                return defaultValue;
            if (elements.Count() > 1)
                throw new TemplateConfigurationException("More that one element named '{0}' found at your configuration.".FormatWith(elements.First().Name));
            return elements.Single();
        }

        public static bool HasClearChildElement(this IEnumerable<XElement> root, string parentName) {
            if (root == null) throw new ArgumentNullException("root");
            if (parentName == null) throw new ArgumentNullException("parentName");
            return root
                .Descendants(parentName)
                .SingleOrDefault(new XElement(parentName))
                .Descendants("clear")
                .SingleOrDefault() != null;

        }
    }
}
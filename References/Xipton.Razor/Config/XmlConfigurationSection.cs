#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Configuration;
using System.Xml;
using System.Xml.Linq;

namespace Xipton.Razor.Config {
    /// <summary>
    /// General purpose configuration section. The whole section's inner xml is loaded without any validation.
    /// </summary>
    public class XmlConfigurationSection : ConfigurationSection
    {
        private bool _firstUnrecognizedElementHandled;

        public XElement InnerXml { get; private set; }

        protected override bool OnDeserializeUnrecognizedElement(string elementName, XmlReader reader) {
            if (!_firstUnrecognizedElementHandled){
                _firstUnrecognizedElementHandled = true;
                InnerXml = (XElement) XNode.ReadFrom(reader);
            }
            else{
                throw new ConfigurationErrorsException("The XmlConfigurationSection must contain at most one child element (holding as many grand childs as you like).");
            }
            return true;
        }
    }
}

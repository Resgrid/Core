#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Xml.Linq;

namespace Xipton.Razor.Config
{
    public class RootOperatorElement : ConfigElement {

        public string Path { get; internal set; }

        #region Overrides
        protected override void Load(XElement e) {
            Path = e.GetAttributeValue("path");
        }

        protected internal override void SetDefaults() {
            Path = "/";
        }
        #endregion
    }
}
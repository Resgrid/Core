#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Xml.Linq;

namespace Xipton.Razor.Config
{
    public abstract class ConfigElement {

        public static TElement Create<TElement>()
            where TElement : ConfigElement, new() {
            var e = new TElement();
            e.SetDefaults();
            return e;
        }

        internal ConfigElement TryLoadElement(XElement e) {
            if (e != null)
                Load(e);
            return this;
        }

        protected abstract void Load(XElement e);
        protected internal abstract void SetDefaults();

    }
}
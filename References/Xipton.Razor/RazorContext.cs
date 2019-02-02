#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using Xipton.Razor.Config;
using Xipton.Razor.Core;

namespace Xipton.Razor
{
    /// <summary>
    /// The RazorContext holds the template factory (with its type cache) and the configurtation.
    /// </summary>
    public class RazorContext : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RazorContext"/> class.
        /// </summary>
        public RazorContext(RazorConfig config = null)
        {
            Config = config ?? new RazorConfig();
            TemplateFactory = new TemplateFactory(this);
        }

        public RazorConfig Config { get; private set; }
        /// <summary>
        /// Gets the template factory. The template factory instantiates the template instances by a virtual template name.
        /// Internally it handles caching of the generated template types.
        /// </summary>
        public TemplateFactory TemplateFactory { get; private set; }

        #region Implementation of IDisposable

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            var target = TemplateFactory;
            TemplateFactory = null;
            if (target != null)
            {
                target.Dispose();
            }
        }

        #endregion

    }
}

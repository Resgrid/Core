#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Xipton.Razor.Core.ContentProvider
{
    /// <summary>
    /// Provides in memory registration of content by its virtual path name with <see cref="RegisterTemplate"/>
    /// </summary>
    public class MemoryContentProvider : IContentProvider
    {
        private readonly Dictionary<string,string>
            _content = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #region Implementation of IContentProvider

        public event EventHandler<ContentModifiedArgs> ContentModified;

        public string TryGetResourceName(string virtualPath)
        {
            lock (_content)
            {
                return virtualPath != null && _content.ContainsKey(virtualPath) ? virtualPath : null;
            }
        }

        public string TryGetContent(string resourceName)
        {
            lock (_content)
            {
                string content;
                return resourceName != null && _content.TryGetValue(resourceName, out content) ? content : null;
            }
        }

        public IContentProvider InitFromConfig(XElement element){
            return this;
        }

        #endregion

        public MemoryContentProvider RegisterTemplate(string virtualRootRelativePath, string content)
        {
            lock (_content)
            {
                var notify = ContentModified != null && _content.ContainsKey(virtualRootRelativePath);

                if (content == null)
                    _content.Remove(virtualRootRelativePath);
                else
                    _content[virtualRootRelativePath] = content;

                if (notify)
                    ContentModified(this, new ContentModifiedArgs(virtualRootRelativePath));
            }
            return this;
        }
    }
}

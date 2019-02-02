#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Xml.Linq;

namespace Xipton.Razor.Core
{
    /// <summary>
    /// The contentent provider provides content by its virtual path.
    /// </summary>
    public interface IContentProvider
    {
        /// <summary>
        /// Occurs when content is modified. If the corresponding content provider provides content that
        /// cannot be modified, this event is never thrown by that instance, e.g., an embedded resource content provider.
        /// </summary>
        event EventHandler<ContentModifiedArgs> ContentModified;
        /// <summary>
        /// Maps the virtual path to the native resource name, e.g., a file system path name,
        /// an embedded resource name, a database key, etc... You could use such a native name
        /// as a cache key
        /// </summary>
        /// <param name="virtualPath">The virtual path.</param>
        /// <returns>The reousrce name or null if no resource was found</returns>
        string TryGetResourceName(string virtualPath);

        /// <summary>
        /// Returns the content by its native resource name. It must return null
        /// if no content was found.
        /// </summary>
        /// <param name="resourceName">Name of the resource.</param>
        /// <returns>The content of null if no content was found</returns>
        string TryGetContent(string resourceName);

        /// <summary>
        /// Initializes this instance from the corresponding configuration node.
        /// </summary>
        /// <param name="element">The configuration element.</param>
        /// <returns></returns>
        IContentProvider InitFromConfig(XElement element);
    }
}
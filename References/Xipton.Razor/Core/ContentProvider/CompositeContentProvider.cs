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

namespace Xipton.Razor.Core.ContentProvider
{
    /// <summary>
    /// The CompositeContentProvider behaves like an IContentProvider, and holds 
    /// an inner list of (one or more) specific IContentProvider implementations that were registered by <see cref="AddContentProvider"/>. 
    /// The first inner registered IContentProvider that has a valid response to a request returns the result. 
    /// So requests for different virtual path names may be handled by different content providers.
    /// 
    /// You cannot explicitly map (or route) virtual path names to specific content providers here. The first content provider that has
    /// a valid result on any request holds. But you can control the order in which the inner specific content providers
    /// are invoked, and so manage the provider priority. e.g. you could add a file provider first followed by an embedded
    /// resource provider. As a result you can work with content as embedded resources unless you create a file that is resolved
    /// by a same virtual path as any embedded resource. In that case the file content is returned because it is found first thus
    /// giving you the possibility to replace or customize embedded resources (e.g. layouts) by file content simply by creating 
    /// templates (layouts) at a file location that is resolved by the same virtual path name.
    /// </summary>
    internal sealed class CompositeContentProvider : IContentProvider, IDisposable
    {
        //now order matters no dictionary is applied here
        private readonly List<IContentProvider>
            _contentProviders = new List<IContentProvider>();

        #region Implementation of IContentProvider

        public event EventHandler<ContentModifiedArgs> ContentModified;

        public string TryGetResourceName(string virtualPath)
        {
            lock(_contentProviders)
            {
                return _contentProviders
                    .Select(provider => provider.TryGetResourceName(virtualPath))
                    .Where(s => s != null)
                    .FirstOrDefault();
            }
        }

        public string TryGetContent(string resourceName)
        {
            lock (_contentProviders)
            {
                return _contentProviders
                    .Select(provider => provider.TryGetContent(resourceName))
                    .Where(s => s != null)
                    .FirstOrDefault();
            }
        }

        public IContentProvider InitFromConfig(XElement element){
            throw new NotImplementedException("The composite cannot be initialized by configuration directly (attempted by '{0}'). Instead for all individual childs (specific content providers) constructors are created at the GeneratorConfig class.".FormatWith(element));
        }

        #endregion

        public CompositeContentProvider AddContentProvider(IContentProvider contentProvider, int order = 0)
        {
            if (contentProvider == null) throw new ArgumentNullException("contentProvider");
            lock (_contentProviders)
            {
                contentProvider.ContentModified += OnContentModified;
                if (order < _contentProviders.Count && order >= 0)
                    _contentProviders.Insert(order, contentProvider);
                else
                    _contentProviders.Add(contentProvider);
                return this;
            }
        }

        public CompositeContentProvider RemoveContentProvider(IContentProvider contentProvider) {
            if (contentProvider == null) return this;
            lock (_contentProviders){
                if (_contentProviders.Remove(contentProvider)){
                    contentProvider.ContentModified -= OnContentModified;
                    contentProvider.TryDispose();
                }
            }
            return this;
        }

        public CompositeContentProvider RemoveContentProviders(Type type)
        {
            lock (_contentProviders){
                foreach (var provider in _contentProviders.ToList().Where(provider => provider.GetType() == type)){
                    RemoveContentProvider(provider);
                }
                return this;
            }
        }

        public CompositeContentProvider ClearAllContentProviders(){
            lock(_contentProviders){
                _contentProviders.ForEach(p => 
                { 
                    p.ContentModified -= OnContentModified;
                    p.TryDispose();
                });
                _contentProviders.Clear();
                return this;
            }
        }

        /// <summary>
        /// gets the content provider by its type. Throws an exception if more than one content providers have been registered for the same type
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public IContentProvider TryGetContentProvider(Type type) {
            lock (_contentProviders){
                return _contentProviders.Where(p => p.GetType() == type).SingleOrDefault();
            }
        }

        /// <summary>
        /// gets all content provider with the requested type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public IEnumerable<IContentProvider> GetContentProviders(Type type) {
            lock (_contentProviders){
                return _contentProviders.Where(p => p.GetType() == type);
            }
        }


        private void OnContentModified(object sender, ContentModifiedArgs e)
        {
            if (ContentModified != null)
            {
                ContentModified(sender, e);
            }
        }

        #region Implementation of IDisposable

        public void Dispose()
        {
            ClearAllContentProviders();
        }

        #endregion
    }
}

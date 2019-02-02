#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xipton.Razor.Config;
using Xipton.Razor.Core.ContentProvider;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Core
{
    /// <summary>
    /// The ContentManager assembles content and delivers final content to the template factory. It implements additional routing logic on top of the content providers.
    /// </summary>
    public class ContentManager : IDisposable
    {

        #region Fields
        // if cached null results reaches this count then the cache is purged (null results are removed).
        // this is needed to prevent the cache from getting loaded endlessly with null results from invalid requests
        private const int _cacheMaxNullCount = 1000; 

        private CompositeContentProvider 
            _contentProvider;

        private int 
            _nullNormalizedEntryCount,
            _nullResourceEntryCount;


        private readonly VirtualPathBuilder 
            _pathBuilder;

        private readonly IList<string>
            _virtualSearchPathList;

        private readonly ConcurrentDictionary<string,string>
            _requestToResourceNameMap = new ConcurrentDictionary<string, string>(),
            _resourceNameToVirtualPathMap = new ConcurrentDictionary<string, string>(),
            _requestToVirtualTargetMap = new ConcurrentDictionary<string, string>();

        private readonly string 
            _autoIncludeName;
        private readonly string 
            _defaultExtension;
        #endregion

        public event EventHandler<ContentModifiedArgs> SharedContentModified;

        public ContentManager(RazorConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            _contentProvider = new CompositeContentProvider();
            ContentProvider.ContentModified += OnContentModified;
            config.ContentProviders.ToList().ForEach(ctor => AddContentProvider(ctor()));

            _pathBuilder = new VirtualPathBuilder(config.RootOperator.Path);
            _autoIncludeName = config.Templates.AutoIncludeName.EmptyAsNull();
            _defaultExtension = config.Templates.DefaultExtension.EmptyAsNull();
            if (_autoIncludeName != null && !_autoIncludeName.Contains(".") && _defaultExtension != null)
                _autoIncludeName = _autoIncludeName + "." + _defaultExtension.TrimStart('.');
            var shared = config.Templates.SharedLocation.EmptyAsNull();
            var searchPathList = new List<string>();
            if (shared != null)
                searchPathList.Add(GetPathBuilderWithRootOperator(shared).RemoveTrailingSlash());
            _virtualSearchPathList = searchPathList.AsReadOnly();
        }

        #region Content provider management

        public IContentProvider ContentProvider{
            get { return _contentProvider; }
        }

        public ContentManager AddContentProvider(IContentProvider contentProvider, int order = 0)
        {
            if (contentProvider == null) throw new ArgumentNullException("contentProvider");
            ContentProvider.CastTo<CompositeContentProvider>().AddContentProvider(contentProvider, order);
            return this;
        }

        public ContentManager RemoveContentProviders(Type type)
        {
            ContentProvider.CastTo<CompositeContentProvider>().RemoveContentProviders(type);
            return this;
        }
        public ContentManager RemoveContentProviders<TContentProvider>() where TContentProvider : IContentProvider {
            return RemoveContentProviders(typeof(TContentProvider));
        }
        public ContentManager RemoveContentProvider(IContentProvider provider) {
            ContentProvider.CastTo<CompositeContentProvider>().RemoveContentProvider(provider);
            return this;
        }

        public IContentProvider TryGetContentProvider(Type contentProviderType)
            {
            return ContentProvider
                .CastTo<CompositeContentProvider>()
                .TryGetContentProvider(contentProviderType);
        }
        public T TryGetContentProvider<T>() where T : IContentProvider
        {
            return TryGetContentProvider(typeof(T))
                .CastTo<T>();
        }

        public IEnumerable<IContentProvider> GetContentProviders(Type contentProviderType) {
            return ContentProvider
                .CastTo<CompositeContentProvider>()
                .GetContentProviders(contentProviderType);
        }
        public IEnumerable<T> GetContentProviders<T>() where T : IContentProvider {
            return GetContentProviders(typeof (T)).Cast<T>();
        }

        public ContentManager ClearAllContentProviders(){
            ContentProvider.CastTo<CompositeContentProvider>().ClearAllContentProviders();
            return this;
        }
        #endregion

        /// <summary>
        /// Resolves a virtual path (i.e. an existing virtual path) for any incomming virtual request. 
        /// example: a layout request like Layout="_layout" at template ~/templates/mytemplate.cshtml at first is attempted to get resolved at "~/templates/_layout.cshtml"
        /// But if the "_layout" only exists at "~/shared" this method translates the incomming request "~/templates/_layout.cshtml" into "~/shared/_layout.cshtml".
        /// </summary>
        /// <remarks>
        /// Since searching at resources may need considarable amounts of time the map results are cached. 
        /// If any content resource changes (notified by <see cref="OnContentModified"/>) the whole mapping cache is cleared.
        /// </remarks>
        /// <param name="requestedPath">The requested path.</param>
        /// <returns>The actual virtual path or null if no resolve could be made</returns>
        public string TryGetVirtualPath(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return null;

            if (_nullNormalizedEntryCount > _cacheMaxNullCount) {
                // clear entries that gave a null result once in a while  
                _nullNormalizedEntryCount = 0;
                string value;
                _requestToVirtualTargetMap
                    .Where(x => x.Value == null)
                    .ToList()
                    .ForEach(x => _requestToVirtualTargetMap.TryRemove(x.Key, out value));
            }

            return _requestToVirtualTargetMap.GetOrAdd(requestedPath, path =>
                {
                    var pathBuilder = GetPathBuilderWithRootOperator(path);
                    pathBuilder.AddOrKeepExtension(_defaultExtension);

                    var resourceName = ContentProvider.TryGetResourceName(pathBuilder);
                    if (resourceName != null)
                        // content was found at the requested virtual location
                        return pathBuilder;

                    // search all search paths for an actual virtual path
                    var name = pathBuilder.GetLastPart(true);
                    var result = VirtualSearchPathList.Any(searchPath => (resourceName = ContentProvider.TryGetResourceName(pathBuilder.Clear().CombineWith(searchPath, name))) != null) ? pathBuilder : null;
                    if (result == null)
                        _nullNormalizedEntryCount++;
                    return result;
                });
        }

        /// <summary>
        /// Try to reolve a virtual request into a resource name (e.g. a filename).
        /// </summary>
        /// <remarks>
        /// Since searching in resources may need considarable amounts of time the results are cached. 
        /// If any content resource changes (notified by <see cref="OnContentModified"/>) the whole cache is cleared.
        /// </remarks>
        /// <param name="requestedPath">The requested path.</param>
        /// <returns>The targeted resource name, or null if no resource name was found</returns>
        public string TryGetResourceName(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
                return null;

            if (_nullResourceEntryCount > _cacheMaxNullCount) {
                _nullResourceEntryCount = 0;
                string value;
                _requestToResourceNameMap
                    .Where(x => x.Value == null)
                    .ToList()
                    .ForEach(x => _requestToResourceNameMap.TryRemove(x.Key, out value));
            }

            return _requestToResourceNameMap
                .GetOrAdd(requestedPath, path =>
                {
                    var virtualLocation = TryGetVirtualPath(path);
                    var resourceName = virtualLocation == null ? null : ContentProvider.TryGetResourceName(virtualLocation);
                    if(resourceName != null)
                        _resourceNameToVirtualPathMap[resourceName] = virtualLocation;
                    else
                        _nullResourceEntryCount++;
                    return resourceName;
                });
        }

        /// <summary>
        /// Try to resolve, read and return the content by the requested path.
        /// All includes (respresented by AutoIncludeName e.g. _viewStart) from the 
        /// current folder until te virtual root folder are inserted as well. 
        /// The nearest include is inserted as the last, so the nearest include 
        /// defines any possible  override (e.g. Layout setting).
        /// </summary>
        /// <remarks>
        /// The content itself is never cached.
        /// </remarks>
        /// <param name="requestedPath">The requested path.</param>
        /// <returns>The content or null if no content was found</returns>
        public string TryGetContent(string requestedPath)
        {
            var resourceName = TryGetResourceName(requestedPath);
            if (resourceName == null)
                return null;

            var content = ContentProvider.TryGetContent(resourceName);
            if (content == null || _autoIncludeName == null)
                return content;

            var virtualPath = TryGetVirtualPath(requestedPath);

            var sb = new StringBuilder(content);
            var nearestInclude = _pathBuilder
                .New()
                .CombineWith(virtualPath)
                .RemoveLastPart()
                .CombineWith(_autoIncludeName)
                .AddOrKeepExtension(_defaultExtension);

            var includes = FindAutoIncludes(nearestInclude).ToList();

            foreach (var include in includes)
            {
                //sb.Insert(0, Environment.NewLine);
                sb.Insert(0, ContentProvider.TryGetContent(include));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Gets the search path list as a read only list. Normally this list has one entry.
        /// </summary>
        public IList<string> VirtualSearchPathList
        {
            get { return _virtualSearchPathList; }
        }

        #region Implementation of IDisposable

        public void Dispose()
        {
            Dispose(true);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            var provider = _contentProvider;
            _contentProvider = null;
            if (provider == null) return;
            provider.ContentModified -= OnContentModified;
            provider.Dispose();
        }

        #endregion

        #region Private
        private IEnumerable<string> FindAutoIncludes(string virtualPathToNearestAutoInclude)
        {
            var vp = GetPathBuilderWithRootOperator(virtualPathToNearestAutoInclude);

            vp.AddOrKeepExtension(_defaultExtension);

            var actual = ContentProvider.TryGetResourceName(vp);
            if (actual != null)
                yield return actual;

            var name = vp.GetLastPart(true);
            var searchPathes = VirtualSearchPathList;
            while (!searchPathes.Contains(vp.RemoveTrailingSlash(),StringComparer.OrdinalIgnoreCase) && vp.RemoveLastPart().Length != 0)
            {
                // search all parents until the virtual root, or until any shared folder name
                actual = ContentProvider.TryGetResourceName(vp.CombineWith(name));
                if (actual != null) yield return actual;
                vp.RemoveLastPart();
            }
        }
        private VirtualPathBuilder GetPathBuilderWithRootOperator(string path)
        {
            return _pathBuilder.New().CombineWith(path).Normalize().WithRootOperator();
        }
        private void OnContentModified(object sender, ContentModifiedArgs e)
        {
            _requestToResourceNameMap.Clear();
            _requestToVirtualTargetMap.Clear();

            if (SharedContentModified == null) return;

            string virtualLocation;
            if (!_resourceNameToVirtualPathMap.TryGetValue(e.ModifiedResourceName, out virtualLocation)){
                if (_autoIncludeName != null && e.ModifiedResourceName.ToLower().Contains(_autoIncludeName.ToLower())){
                    // extra check to force clearing type cache of no _viewStart has been requested until now
                    SharedContentModified(this, new ContentModifiedArgs(e.ModifiedResourceName));
                }
                return;
            }

            var modifiedName = _pathBuilder
                .New()
                .CombineWith(virtualLocation)
                .AddOrKeepExtension(_defaultExtension)
                .GetLastPart(true);

            if (string.Equals(modifiedName, _autoIncludeName, StringComparison.OrdinalIgnoreCase))
            {
                // If the name equals _autoIncludeName (like _viewStart) notify listeners. Such content is included in all generated source. Normally as a result the whole type cache needs to be cleared.
                SharedContentModified(this, new ContentModifiedArgs(e.ModifiedResourceName));
            }
        }
        #endregion

    }
}
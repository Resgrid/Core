#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.IO;
using System.Security.Permissions;
using System.Xml.Linq;
using Xipton.Razor.Config;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Core.ContentProvider
{
    /// <summary>
    /// The FileContentProvider provides content from files requested by virtual path names.
    /// A FileWatcher is created to monitor and publish file changes.
    /// </summary>
    [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)] // needed because of the file watcher events
    [SecurityPermission(SecurityAction.InheritanceDemand, Flags = SecurityPermissionFlag.UnmanagedCode)] // needed because of the file watcher events, needed for notification on sub classing
    public class FileContentProvider : IContentProvider, IDisposable
    {
        #region Fields
        private string
            _rootFolder = ".".MakeAbsoluteDirectoryPath();

        private FileSystemWatcher
            _watcher;

        private readonly object 
            _syncRoot = new object();
        #endregion

        protected FileContentProvider() {}

        /// <summary>
        /// Initializes a new instance of the <see cref="FileContentProvider"/> class.
        /// It needs the root directory of where the content folders start. Normally this is the application's
        /// base folder, or something like [basefolder]/Views
        /// </summary>
        /// <param name="rootFolder">The root directory.</param>
        public FileContentProvider(string rootFolder)
        {
            _rootFolder = new DirectoryInfo(rootFolder.MakeAbsoluteDirectoryPath()).FullName;
        }

        #region Implementation of IContentProvider

        public event EventHandler<ContentModifiedArgs> ContentModified;

        public virtual string TryGetResourceName(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath)) return null;
            var file = new FileInfo(Path.Combine(_rootFolder, virtualPath.RemoveRoot()).Replace("/","\\"));
            if (!file.Exists) return null;
            AssureWatcherInitialized();
            return file.FullName;
        }

        public virtual string TryGetContent(string resourceFileName){
            return !File.Exists(resourceFileName) ? null : File.ReadAllText(resourceFileName);
        }

        public virtual IContentProvider InitFromConfig(XElement element){
            var rootFolder = (element.GetAttributeValue("rootFolder", false) ?? _rootFolder).MakeAbsoluteDirectoryPath();
            _rootFolder = new DirectoryInfo(rootFolder).FullName;
            return this;
        }

        #endregion

        #region Implementation of IDisposable
        public void Dispose()
        {
            Dispose(true);
        }
        protected virtual void Dispose(bool disposing)
        {
            lock (_syncRoot)
            {
                if (disposing && _watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Changed -= OnChanged;
                    _watcher.Deleted -= OnChanged;
                    _watcher.Renamed -= OnRenamed;
                    _watcher.Dispose();
                    _watcher = null;
                }
            }
        }
        #endregion

        #region FileWatcher
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "_watcher is disposed on FileContentProvider.Dispose()")]
        private void AssureWatcherInitialized()
        {
            lock (_syncRoot)
            {
                if (_watcher != null || ContentModified == null) return;

                _watcher = new FileSystemWatcher
                               {
                                   Path = _rootFolder,
                                   NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastAccess,
                                   Filter = "*.*",
                                   IncludeSubdirectories = true
                               };

                _watcher.Changed += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnRenamed;
                _watcher.Created += OnChanged;
                _watcher.EnableRaisingEvents = true;
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (ContentModified != null)
                ContentModified(this, new ContentModifiedArgs(e.OldFullPath));
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (ContentModified != null)
                ContentModified(this, new ContentModifiedArgs(e.FullPath));
        }

        #endregion

    }
}

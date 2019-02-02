#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Text;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Core
{
    /// <summary>
    /// The VirtualPathBuilder builds a virtual path and provides virtual path operations.
    /// This builder neither does support schemas nor arguments / query strings / anchors (e.g. http:// or ?p1=5 or #anchor1). An exception is thrown if such parts are detected.
    /// It does support extensions (like .html) and it does support the root operator (~). An applicationRoot is optional at the constructor. 
    /// By default the applicationRoot is: /
    /// </summary>
    public class VirtualPathBuilder: ICloneable
    {
        #region Types
        private struct PathPart
        {
            public int Start;
            public int Length;
            public bool Skip;
        }
        #endregion

        #region Fields

        private readonly StringBuilder
            _builder = new StringBuilder();

        #endregion

        public VirtualPathBuilder(string applicationRoot = null)
        {
            if (applicationRoot == null)
                applicationRoot = "/";
            else
            {
                if (!applicationRoot.StartsWith("/"))
                    throw new ArgumentException("Argument applicationRoot must start with a '/'", "applicationRoot");
                if (applicationRoot.StartsWith("~"))
                    throw new ArgumentException("Argument applicationRoot can not start with the root operator '~'. In fact it represents the root operator", "applicationRoot");
                if (!applicationRoot.EndsWith("/"))
                    applicationRoot = applicationRoot + "/";
            }
            ApplicationRoot = applicationRoot;
        }

        public virtual string ApplicationRoot { get; private set; }
        public virtual int Length{
            get { return _builder.Length; }
        }
        public virtual VirtualPathBuilder Clear()
        {
            _builder.Clear();
            return this;
        }
        public virtual bool HasRootOperator()
        {
            return _builder.Length > 0 && _builder[0] == '~';
        }
        public virtual bool IsAbsolutePath()
        {
            return _builder.Length > 0 && _builder[0] == '/';
        }
        public virtual bool IsApplicationRoot(){
            return _builder.Length > 0 && 
                (_builder[0] != '~'
                    ? string.Compare(Clone().AppendTrailingSlash(), ApplicationRoot,StringComparison.OrdinalIgnoreCase) == 0
                    : _builder.Length == 1 || (_builder.Length == 2 && _builder[1] == '/')
                );
        }
        public virtual bool IsValidAbsolutePath()
        {
            if (_builder.Length == 0)
                return false;
            var hadTrailingSlah = HasTrailingSlash();
            AppendTrailingSlash();
            var result =  ApplicationRoot == "/"
                ? IsAbsolutePath()
                : IsAbsolutePath() && _builder.Length >= ApplicationRoot.Length && string.Compare(_builder.ToString(0, ApplicationRoot.Length), ApplicationRoot, StringComparison.OrdinalIgnoreCase) == 0;

            if (!hadTrailingSlah)
                RemoveTrailingSlash();

            return result;
        }
        public virtual bool IsRelativePath()
        {
            return _builder.Length > 0 && !HasRootOperator() && !IsAbsolutePath();
        }
        public virtual VirtualPathBuilder ResolveRootOperator()
        {
            Normalize();
            if (!IsValidAbsolutePath()) {
                throw new InvalidOperationException(string.Format("Path '{0}' is not a valid absolute path and cannot be resolved.", _builder));
            }
            if (IsAbsolutePath()) return this;
            if (!HasRootOperator())
            {
                throw new InvalidOperationException(string.Format("Path '{0}' is a relative path and cannot be resolved.",_builder));
            }
            if (_builder.Length >= 2 && _builder[1] == '/')
                _builder.Remove(0, 2);
            else
                _builder.Remove(0, 1);
            _builder.Insert(0, ApplicationRoot);
            return this;
        }
        /// <summary>
        /// replaces the application root with the root operator.
        /// </summary>
        /// <returns></returns>
        public virtual VirtualPathBuilder WithRootOperator()
        {
            Normalize();
            if (HasRootOperator()) return this;
            if (!IsValidAbsolutePath())
                throw new InvalidOperationException(string.Format("Path '{0}' is not an absolute path, or an invalid absolute path, and cannot be added with a root operator", _builder));
            var hadTrailingSlash = HasTrailingSlash();
            AppendTrailingSlash();
            _builder.Remove(0, ApplicationRoot.Length);
            _builder.Insert(0, "~/");
            if (!hadTrailingSlash && _builder.Length > 2)
                RemoveTrailingSlash();
            return this;
        }
        /// <summary>
        /// Normalizes the path, i.e., resolves any root operator and any relative path jumps (like '..' and '.').
        /// Its purpose is to be able to determine pathes's equality
        /// </summary>
        /// <returns></returns>
        public virtual VirtualPathBuilder Normalize()
        {
            if (_builder.Length == 0){
                return this;
            }
            if (_builder.Length > 1)
                RemoveTrailingSlash();
            if (_builder[0] == '~')
            {
                _builder.Remove(0, 1);
                if (_builder.Length > 0 && _builder[0] == '/')
                    _builder.Remove(0, 1);
                _builder.Insert(0, ApplicationRoot);
            }
            if (_builder[0] != '/' && _builder[0] != '.') {
                _builder.Insert(0, ApplicationRoot);
                return this;
            }
            if (_builder.Length == 1 && _builder[0] == '/')
                return this;

            const int maxPartCount = 100; // you will never ever exceed this value... there is a max count value because we are working here with a fixed array size for performance reasons
            var parts = new PathPart[maxPartCount];
            var partIndex = -1;
            var dotCount = 0;
            var charCount = 0;
            var anySkip = false;

            // resolve relative jumps (like ../ and ./)
            for (var i = 0; i < _builder.Length; i++ )
            {
                var ch = _builder[i];
                charCount++;
                if (ch == '/' || i == _builder.Length - 1)
                {
                    if (partIndex == maxPartCount - 1)
                    {
                        // ... so this will never happen
                        throw new InvalidOperationException(string.Format(string.Format("Path '{0}' contains too many parts (max = {0}) and cannot be normalized.",maxPartCount), _builder));
                    }
                    if (ch == '.') dotCount++;
                    anySkip = anySkip || dotCount > 0;
                    parts[++partIndex] = new PathPart {Length = charCount, Start = i - charCount+1, Skip = dotCount > 0};
                    if (dotCount == 2 )
                    {
                        var index = partIndex - 1;
                        while (index > 0 && parts[index].Skip)
                            index--;
                        if (index >= 0)
                            parts[index].Skip = true;
                        else
                        {
                            throw new InvalidOperationException(string.Format("Path '{0}' is invalid and cannot be normalized.", _builder));
                        }
                    }
                    dotCount = 0;
                    charCount = 0;
                }
                else if (ch == '.')
                {
                    dotCount++;
                    if (dotCount > 2)
                        throw new InvalidOperationException(string.Format("Path '{0}' is invalid and cannot be normalized.", _builder));
                }
                else
                {
                    dotCount = 0;
                }
            }
            if (anySkip){
                var sb = new StringBuilder();
                for (var i = 0; i <= partIndex; i++){
                    if (parts[i].Skip)
                        continue;
                    sb.Append(_builder.ToString(parts[i].Start, parts[i].Length));
                }
                _builder.Clear();
                _builder.Append(sb.ToString());
            }
            if (_builder.Length > 1)
                RemoveTrailingSlash();
            return this;
        }
        public virtual VirtualPathBuilder CombineWith(params string[] parts)
        {
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                var index = 0;
                foreach (var ch in part)
                {
                    if (ch == ':') throw new ArgumentException("{0} does not support schemas or drive names: '{1}'. You should handle schemas and drive names outside {0}.".FormatWith(GetType().Name, part), "parts");
                    if (ch == '?') throw new ArgumentException("{0} does not support query strings / arguments: '{1}'. You should handle query strings outside {0}.".FormatWith(GetType().Name, part), "parts");
                    if (ch == '#') throw new ArgumentException("{0} does not support bookmarks: '{1}'. You should handle bookmarks outside {0}.".FormatWith(GetType().Name, part), "parts");
                    if (ch == '\\') throw new ArgumentException("{0} does not support filenames nor a backslash '\\' in general: '{1}'. You should handle such paths and characters outside {0}.".FormatWith(GetType().Name, part), "parts");
                    if (ch == '~' && index > 0) throw new ArgumentException("Path '{0}' is invalid. The root operator '~' is allowed as start character only.".FormatWith(part));
                    index++;
                }

                if (part[0] == '/' || part[0] == '~')
                {
                    // new root
                    _builder.Clear();
                    _builder.Append(part);
                }
                else
                {
                    if (_builder.Length > 0)
                        AppendTrailingSlash();
                    _builder.Append(part);
                }
            }
            return this;
        }
        public virtual bool HasExtension()
        {
            if (_builder.Length > 0 && _builder[_builder.Length - 1] == '.')
                return false;
            for(var i = _builder.Length - 1; i >= 0; i--)
            {
                if (_builder[i] == '.') return true;
                if (_builder[i] == '/') return false;
            }
            return false;
        }
        public virtual string GetExtension(bool remove = false)
        {
            for (var i = _builder.Length - 1; i >= 0; i--)
            {
                if (_builder[i] == '.')
                {
                    var extension =  _builder.ToString(i+1,_builder.Length - i - 1);
                    if (remove)
                        _builder.Length = i;
                    return extension.Length == 0 ? null : extension;
                }
                if (_builder[i] == '/') return null;
            }
            return null;
        }
        /// <summary>
        /// Adds the extension, or replaces the existing extension if there is an extension yet.
        /// </summary>
        /// <param name="extension">The extension.</param>
        /// <returns></returns>
        public virtual VirtualPathBuilder AddOrReplaceExtension(string extension)
        {
            GetExtension(true);
            if (!string.IsNullOrEmpty(extension))
            {
                if (extension[0] != '.')
                    _builder.Append('.');
                _builder.Append(extension);
            }
            return this;
        }
        /// <summary>
        /// Adds the extension only if there is no extension yet.
        /// </summary>
        /// <param name="extension">The extension.</param>
        /// <returns></returns>
        public virtual VirtualPathBuilder AddOrKeepExtension(string extension)
        {
            if (extension.NullOrEmpty() || HasExtension()) return this;
            _builder.Append('.').Append(extension.TrimStart('.'));
            return this;
        }
        public virtual VirtualPathBuilder RemoveExtension()
        {
            return AddOrReplaceExtension(null);
        }
        /// <summary>
        /// Gets the first part of a path.
        /// examples:
        /// ~/app/path returns "~"
        /// /app/path returns "/app"
        /// </summary>
        /// <param name="remove">if set to <c>true</c> [remove].</param>
        /// <returns></returns>
        public virtual string GetFirstPart(bool remove = false){
            if (_builder.Length == 0) return string.Empty;
            if (_builder[0] == '~'){
                if (remove) _builder.Remove(0, 1);
                return "~";
            }
            string result;
            for(var i = 1; i < _builder.Length;i++){
                if (_builder[i] != '/') continue;
                result = _builder.ToString(0, i);
                if (remove) _builder.Remove(0, i);
                return result;
            }
            result = _builder.ToString();
            if (remove) _builder.Clear();
            return result;
        }
        public virtual VirtualPathBuilder RemoveFirstPart() {
            GetFirstPart(true);
            return this;
        }
        /// <summary>
        /// Gets the last part of a path.
        /// examples:
        /// ~/app/path/ returns "path"
        /// ~/app/path returns "path"
        /// ~/ returns "~/"
        /// / returns "/"
        /// </summary>
        /// <param name="remove">if set to <c>true</c> [remove].</param>
        /// <returns></returns>
        public virtual string GetLastPart(bool remove = false)
        {
            if (_builder.Length == 0) 
                return string.Empty;

            if (_builder.Length == 1 && _builder[0] == '/'){
                if (remove)
                    _builder.Length = 0;
                return "/";
            }

            var start = (HasTrailingSlash() ? _builder.Length - 1 : _builder.Length) - 1;
            for (var i = start; i >= 0; i--)
            {
                if (_builder[i] == '/' )
                {
                    var part = _builder.ToString(i + 1, start - i);
                    if (remove)
                        _builder.Length = i + 1;
                    return part;
                }
                if (_builder[i] == '~') {
                    var part = _builder.ToString();
                    if (remove)
                        _builder.Length = 0;
                    return part;
                }
            }
            if (remove)
                _builder.Length = 0;
            return _builder.ToString();
        }
        public virtual VirtualPathBuilder RemoveLastPart()
        {
            GetLastPart(true);
            return this;
        }
        public virtual bool HasTrailingSlash(bool ignoreRoot = false)
        {
            if (ignoreRoot && _builder.Length == 1) return false;
            return _builder.Length > 0 && _builder[_builder.Length - 1] == '/';
        }
        public virtual VirtualPathBuilder AppendTrailingSlash()
        {
            if (_builder.Length == 0 || _builder[_builder.Length - 1] != '/')
                _builder.Append('/');
            return this;
        }
        public virtual VirtualPathBuilder RemoveTrailingSlash(bool keepSingleSlash = false)
        {
            if (keepSingleSlash && _builder.Length <= 1)
                return this;
            while (_builder.Length > 0 && _builder[_builder.Length - 1] == '/')
                _builder.Length--;
            return this;
        }
        public virtual VirtualPathBuilder New() {
            return new VirtualPathBuilder(ApplicationRoot);
        }

        #region Equality and Operators
        public static bool operator ==(VirtualPathBuilder pb, string s)
        {
            try{
                if (Equals(s, null) || Equals(pb, null))
                    return Equals(s, null) && Equals(pb, null);
                if (string.Compare(pb._builder.ToString(), s, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
                var first = pb.Clone().Normalize().ToString();
                var second = new VirtualPathBuilder(pb.ApplicationRoot).CombineWith(s).Normalize().ToString();
                return string.Compare(first, second, StringComparison.OrdinalIgnoreCase) == 0;
            }
            catch{
                // on any normalize error return false
                return false;
            }
        }

        public static bool operator !=(VirtualPathBuilder pb, string s)
        {
            return !(pb == s);
        }

        public static implicit operator string(VirtualPathBuilder pb)
        {
            return pb == null ? null : pb.ToString();
        }
        public override bool Equals(object obj)
        {
            var other = obj as VirtualPathBuilder;
            if (other == null) return false;
            var @this = Clone().Normalize();
            other = other.Normalize();
            if (other._builder.Length != @this._builder.Length) return false;
            return string.Compare(other._builder.ToString(), @this._builder.ToString(), StringComparison.OrdinalIgnoreCase) == 0;
        }
        public override int GetHashCode()
        {
            return _builder.ToString().GetHashCode();
        }
        #endregion

        #region Implementation of ICloneable
        object ICloneable.Clone(){
            return Clone();
        }
        public virtual VirtualPathBuilder Clone() {
            return New().CombineWith(this);
        }
        #endregion

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}

#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.IO;

namespace Xipton.Razor.Extension
{
    public static class StringExtension
    {

        public static string FormatWith(this string format, params object[] args)
        {
            return format == null ? null : string.Format(format, args);
        }

        public static bool NullOrEmpty(this string value)
        {
            return string.IsNullOrEmpty(value);
        }

        public static string EmptyAsNull(this string value) {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public static bool HasVirtualRootOperator(this string path)
        {
            return path != null && path.StartsWith("~");
        }

        public static string RemoveRoot(this string path)
        {
            if (path.NullOrEmpty()) return path;
            if (path[0] == '~') path = path.Substring(1);
            return path.TrimStart('\\').TrimStart('/');
        }

        public static bool IsAbsoluteVirtualPath(string path)
        {
            return path != null && (path.Contains(":") || path.StartsWith("/") || path.StartsWith("\\"));
        }

        public static string MakeAbsoluteDirectoryPath(this string path){
            return Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile), path ?? ".");
        }

        public static string HtmlEncode(this string value)
        {
            return value == null ? null : System.Net.WebUtility.HtmlEncode(value);
        }

        internal static bool IsFileName(this string path) {
            return path != null && File.Exists(path);
        }
        internal static bool IsXmlContent(this string content) {
            return content != null && content.TrimStart().StartsWith("<");
        }
        internal static bool IsUrl(this string value) {
            return value != null && (value.HasVirtualRootOperator() || value.StartsWith("/"));
        }

    }
}
#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Reflection;

namespace Xipton.Razor.Extension
{
    public static class AssemblyExtension
    {
        public static string GetFileName(this Assembly assembly)
        {
            return assembly == null 
                ? null 
                : assembly.CodeBase.Replace("file:///", string.Empty).Replace("/", "\\");
        }
    }
}

#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;

namespace Xipton.Razor.Extension
{
    public static class ObjectExtension
    {
        public static T CastTo<T>(this object target)
        {
            return target == null ? default(T) : (T) target;
        }

        public static bool TryDispose(this object target)
        {
            var disposable = target as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
                return true;
            }
            return false;
        }

    }
}

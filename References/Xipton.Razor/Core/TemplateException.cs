#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Runtime.Serialization;

namespace Xipton.Razor.Core
{
    [Serializable]
    public class TemplateException : Exception
    {

        public TemplateException()
        {
        }

        public TemplateException(string message) : base(message)
        {
        }

        public TemplateException(string message, Exception inner) : base(message, inner)
        {
        }

        protected TemplateException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }
}

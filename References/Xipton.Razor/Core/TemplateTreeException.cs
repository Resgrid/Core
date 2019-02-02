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
    // thrown if there are problems detected with the template tree (parent or childs)
    [Serializable]
    public class TemplateTreeException : TemplateException
    {
        public TemplateTreeException()
        {
        }

        public TemplateTreeException(string message)
            : base(message)
        {
        }

        public TemplateTreeException(string message, Exception inner)
            : base(message, inner)
        {
        }

        protected TemplateTreeException(
            SerializationInfo info,
            StreamingContext context)
            : base(info, context)
        {
        }
    }
}
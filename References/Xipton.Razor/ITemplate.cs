#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Collections.Generic;
using Xipton.Razor.Core;

namespace Xipton.Razor
{
    /// <summary>
    /// All templates need to implement this interface. It is implementated by <see cref="TemplateBase"/>.
    /// <remarks>
    /// </remarks>
    /// for interchangeability reasons some operations are (about) the same as MVC view operations.
    /// </summary>
    public interface ITemplate {

        #region MVC complient
        string Layout { get; set; }
        dynamic Model { get; }
        dynamic ViewBag { get; }
        LiteralString RenderBody();

        // you may skip the layout using the parameter skipLayoutwhich can be handy 
        // when rendering template partials (controls)
        LiteralString RenderPage(string name, object model = null, bool skipLayout = false);

        LiteralString RenderSection(string sectionName, bool required = false);
        bool IsSectionDefined(string sectionName);
        #endregion

        // If set true then the Write method HTML encode the output. 
        // The default setting can be configured.
        bool HtmlEncode { get; set; }

        // If generated source code must be included (needs to be configured)
        // then that source code can be accessed by this property
        // for debug purposes
        string GeneratedSourceCode { get; }

        // If this template is rendered by another template 
        // (I am a layout or control) then that other template is 
        // registered as the Parent and can be accessed during execution time 
        ITemplate Parent { get; }

        // Any layout and all controls are registered as childs 
        // The child list should not be accessed during
        // template execution, but only after complete execution.
        IList<ITemplate> Childs { get; }

        // Returns the Root parent, or myself (this) if no Parent exists.
        ITemplate RootOrSelf { get; }
        
        // PathBuilder contains the virtual path as a starting point
        VirtualPathBuilder VirtualLocation { get; }

        // The actual virtual path that resolved this template
        string VirtualPath { get; }

        // Context gives access to the template factory and to the razor configuration
        RazorContext RazorContext { get; }

        // The rendered result
        String Result { get; }

        // Writes encoded output only if HtmlEncode is true
        void Write(object value);

        // Writes a literal string, never encoded
        void WriteLiteral(object value);

        // Ensures the value to be written as a raw string. It always must return null. 
        // Since it returns a string type you can invoke it like: @Raw("a & b")
        LiteralString Raw(string value);

    }
}

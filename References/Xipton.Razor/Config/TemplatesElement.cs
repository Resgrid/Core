#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Web.Razor;
using System.Xml.Linq;
using Xipton.Razor.Core.Generator;
using Xipton.Razor.Core.Generator.CSharp;
using Xipton.Razor.Core.Generator.VB;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Config
{
    public class TemplatesElement : ConfigElement {

        private Type 
            _baseType;
        private RazorCodeLanguage 
            _language;


        #region Properties
        /// <summary>
        /// Gets or sets the template's default base type. This settings can be overridden by the @inherits directive (Razor C#)
        /// </summary>
        /// <value>
        /// The default type of the template base. 
        /// <remarks>
        /// The type set must be an open generic type, that inherits a subclass of <see cref="TemplateBase"/>. Also this type
        /// must have the same name as it's base type. So you need to implement two levels of custom base classes if you want 
        /// to set this attribute.
        /// <example>
        /// If you want to implement a custom base class named MyTemplateBase you need this class to inherit TemplateBase and add your custom implementation.
        /// Next you create a class named MyTemplateBase&lt;TModel> like:
        /// <code>
        ///     public class MyTemplateBase&lt;TModel> : MyTemplateBase, ITemplate&lt;TModel>
        ///    {
        ///        public new TModel Model { get { return GetOrCreateModel&lt;TModel>(); } }
        ///    }
        ///</code>
        /// </example>
        /// </remarks> 
        /// </value>
        public Type BaseType {
            get { return _baseType; }
            internal set {
                if (value == null) throw new ArgumentNullException("value");
                ValidateBaseType(value);
                _baseType = value;
            }
        }
        public RazorCodeLanguage Language {
            get { return _language; }
            internal set {
                _language = value
                    .CastTo<IXiptonCodeLanguage>() // must be a Xipton implementation => else an exception is thrown here
                    .CastTo<RazorCodeLanguage>();
            }
        }
        public string DefaultExtension { get; internal set; }
        public string AutoIncludeName { get; internal set; }
        public string SharedLocation { get; internal set; }
        public bool IncludeGeneratedSourceCode { get; internal set; }
        public bool HtmlEncode { get; internal set; }

        public string NonGenericBaseTypeName {
            get {
                var fullname = BaseType.FullName;
                var index = fullname.IndexOf('`');
                return index < 0 ? fullname : fullname.Substring(0, index);
            }
        }
        #endregion

        #region Overrides
        protected override void Load(XElement e) {
            var typeName = e.GetAttributeValue("language", false);
            if (typeName != null) {
                if (typeName.Equals("C#", StringComparison.OrdinalIgnoreCase))
                    // convenient name for default C# implementation  
                    Language = new XiptonCSharpCodeLanguage();
                else if (typeName.Equals("VB", StringComparison.OrdinalIgnoreCase))
                    // convenient name for default VB implementation  
                    Language = new XiptonVBCodeLanguage();
                else
                    Language = Type.GetType(typeName, true).CreateInstance().CastTo<RazorCodeLanguage>();
            }

            typeName = e.GetAttributeValue("baseType", false);
            BaseType = typeName != null ? Type.GetType(typeName, true) : BaseType;

            DefaultExtension = (e.GetAttributeValue("defaultExtension", false) ?? DefaultExtension).EmptyAsNull();
            AutoIncludeName = (e.GetAttributeValue("autoIncludeName", false) ?? AutoIncludeName).EmptyAsNull();
            SharedLocation = (e.GetAttributeValue("sharedLocation", false) ?? SharedLocation).EmptyAsNull();
            var setting = e.GetAttributeValue("includeGeneratedSourceCode", false);
            if (setting != null)
                IncludeGeneratedSourceCode = bool.Parse(setting);
            setting = e.GetAttributeValue("htmlEncode", false);
            if (setting != null)
                HtmlEncode = bool.Parse(setting);
        }
        protected internal override void SetDefaults() {
            BaseType = typeof(TemplateBase);
            Language = new XiptonCSharpCodeLanguage();
            AutoIncludeName = "_viewStart";
            DefaultExtension = ".cshtml";
            SharedLocation = "~/Shared";
            IncludeGeneratedSourceCode = false;
            HtmlEncode = true;
        }
        #endregion

        private static void ValidateBaseType(Type baseType) {

            if (!baseType.IsGenericType) {
                try {
                    baseType = baseType.Assembly.GetType(baseType.FullName + "`1", true); // => generic type must be defined as well
                }
                catch (Exception ex) {
                    throw new TemplateConfigurationException("BaseType {0} must have a generic sub class with one type parameter.".FormatWith(baseType.FullName), ex);
                }
            }

            if (!baseType.IsGenericTypeDefinition || baseType.GetGenericArguments().Length != 1)
                throw new TemplateConfigurationException("BaseType must be an open generic type (or have a subclass that is a generic type) with one generic parameter (for TModel).");

            if (baseType.BaseType.IsGenericType || !typeof(TemplateBase).IsAssignableFrom(baseType.BaseType))
                throw new TemplateConfigurationException("BaseType must inherit a non generic (custom) basetype that must be a subclass of {0}.".FormatWith(typeof(TemplateBase)));

            if (baseType.BaseType.Name != baseType.Name.Substring(0, baseType.Name.Length - 2))
                throw new TemplateConfigurationException("BaseType must be a generic type and must inherit a non generic basetype with the same name.");
        }

    }
}
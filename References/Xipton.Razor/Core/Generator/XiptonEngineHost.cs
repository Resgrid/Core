#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Razor;
using System.Web.Razor.Generator;
using Xipton.Razor.Config;

namespace Xipton.Razor.Core.Generator
{
    /// <summary>
    /// The engine host is the part where the code rendering takes place.
    /// </summary>
    public class XiptonEngineHost : RazorEngineHost
    {
        #region Fields
        private readonly RazorConfig 
            _config;

        private string
            _defaultBaseClass,
            _defaultNamespace;

        private GeneratedClassContext 
            _generatedClassContext;

        private readonly ISet<string> 
            _namespaceImports;
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="XiptonEngineHost"/> class.
        /// </summary>
        /// <param name="config">The config holds all settings that are needed to initialzie the host.</param>
        public XiptonEngineHost(RazorConfig config)
            : base(config.Templates.Language)
        {
            if (config == null) throw new ArgumentNullException("config");
            _defaultNamespace = "Xipton.Razor.Generated";
            _config = config;
            _defaultBaseClass = _config.Templates.NonGenericBaseTypeName;
            _namespaceImports = new HashSet<string>();
            _config.Namespaces.ToList().ForEach(ns => _namespaceImports.Add(ns));

            // the GeneratedClassContext defines the methods that are generated to handle the template 
            // control like writing the generated output and also handle other control operations like 
            // defining sections inside the template
            _generatedClassContext = new GeneratedClassContext("Execute", "Write", "WriteLiteral", null, null, null, "DefineSection")
            {
                ResolveUrlMethodName = "ResolveUrl"
            };
        }

        public override string DefaultBaseClass
        {
            get { return _defaultBaseClass; }
            set { _defaultBaseClass = value; }
        }

        public override GeneratedClassContext GeneratedClassContext
        {
            get { return _generatedClassContext; }
            set { _generatedClassContext = value; }
        }

        public override ISet<string> NamespaceImports
        {
            get { return _namespaceImports; }
        }

        public override string DefaultNamespace
        {
            get { return _defaultNamespace; }
            set{ _defaultNamespace = value; }
        }

    }
}
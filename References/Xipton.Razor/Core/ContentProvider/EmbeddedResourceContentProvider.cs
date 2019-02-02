// disable warning "Event is never used"
// justification: event is part of interface and therefore must be declared. 
#pragma warning disable 67

#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion


using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xipton.Razor.Config;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Core.ContentProvider
{
    /// <summary>
    /// The EmbeddedResourceContentProvider provides content from razor templates that 
    /// were compiled as embedded resources. It needs to know the assembly that holds the
    /// embedded templates so you must pass that assembly at the constructor together with 
    /// the root namespace for all embedded resources.
    /// </summary>
    public class EmbeddedResourceContentProvider : IContentProvider
    {
        private Assembly _resourceAssembly;
        private string _rootNameSpace;

        protected EmbeddedResourceContentProvider(){}

        public EmbeddedResourceContentProvider(Assembly resourceAssembly,  string rootNameSpace)
        {
            if (resourceAssembly == null) throw new ArgumentNullException("resourceAssembly");
            if (rootNameSpace == null) throw new ArgumentNullException("rootNameSpace");
            _resourceAssembly = resourceAssembly;
            _rootNameSpace = rootNameSpace;
        }

        #region Implementation of IContentProvider

        public event EventHandler<ContentModifiedArgs> ContentModified;

        public string TryGetResourceName(string virtualPath)
        {
            if (virtualPath.NullOrEmpty()) return null;
            var resourceName = _rootNameSpace + "." + virtualPath.RemoveRoot().Replace("/",".").Replace("-","_");
            var resources = _resourceAssembly.GetManifestResourceNames().ToList();
            return resources.Where(r => AreEqualResourceNames(r, resourceName)).FirstOrDefault();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "The stream can be disposed multiple times.")]
        public string TryGetContent(string resourceName)
        {
            using (var stream = _resourceAssembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var sr = new StreamReader(stream))
                    return sr.ReadToEnd();
            }
        }

        public IContentProvider InitFromConfig(XElement element){
            var assembly = element.GetAttributeValue("resourceAssembly", false);
						if (assembly != null)
						{
							string path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase );
							path = path.Replace("file:\\", "");

							_resourceAssembly = Assembly.LoadFrom(string.Format("{0}\\{1}", path, assembly));
						}
	        _rootNameSpace = element.GetAttributeValue("rootNameSpace", false) ?? _rootNameSpace;
            if (_resourceAssembly == null) throw new TemplateConfigurationException("{0}: attribute resourceAssembly is required and not allowed null".FormatWith(element));
            if (_rootNameSpace == null) throw new TemplateConfigurationException("{0}: attribute rootNameSpace is required and not allowed null".FormatWith(element));
            return this;
        }

        #endregion

        private static bool AreEqualResourceNames(string s1, string resourceName)
        {
            if (s1 == null || resourceName == null) return s1 == null && resourceName == null;
            if (s1.Length != resourceName.Length)
                return false;
            s1 = s1.Replace("-", "_");
            return string.Compare(s1, resourceName, true) == 0;
        }



    }
}

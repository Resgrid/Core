#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Razor;
using Xipton.Razor.Core.Generator;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Core
{
    /// <summary>
    /// The TemplateFactory provides template instances by their virtual path names. 
    /// The corresponding template types are cached, the template instances are not cached
    /// The template factory needs a context to be injected at the constructor. The context is passed to each created template instance
    /// </summary>
    public class TemplateFactory : IDisposable {

        #region Types
        private class CacheBucket
        {
            public Type GeneratedTemplateType;
            public string GeneratedSourceCode;

            public override bool Equals(object obj) {
                var other = obj as CacheBucket;
                return other != null && Equals(other.GeneratedTemplateType, GeneratedTemplateType);
            }
            public override int GetHashCode() {
                return GeneratedTemplateType == null ? 0 : GeneratedTemplateType.GetHashCode();
            }
        }
        #endregion

        #region Fields
        private const string 
            _sourceFilenamePrefix = "template: ";

        private readonly ConcurrentDictionary<string, CacheBucket>
            _compiledTemplateTypeCache = new ConcurrentDictionary<string, CacheBucket>(StringComparer.OrdinalIgnoreCase);

        private readonly RazorTemplateEngine
            _razorEngine;

        private readonly RazorContext 
            _razorContext;
        #endregion

        public TemplateFactory(RazorContext razorContext) {
            if (razorContext == null) throw new ArgumentNullException("razorContext");
            _razorContext = razorContext;
            _razorEngine = new RazorTemplateEngine(new XiptonEngineHost(razorContext.Config));
            ContentManager = new ContentManager(razorContext.Config);
            ContentManager.ContentProvider.ContentModified += OnContentModified;
            ContentManager.SharedContentModified += OnSharedContentModified;
        }

        #region Public

        public ContentManager ContentManager { get; private set; }

        public ITemplate CreateTemplateInstance(string requestedVirtualTemplateName, bool throwExceptionOnVirtualPathNotFound = true)
        {
            // all caching is done by the native resource names because any change notification is done with the native resource name as a parameter
            var resourceName = ContentManager.TryGetResourceName(requestedVirtualTemplateName);
            if (resourceName == null){
                if (throwExceptionOnVirtualPathNotFound)
                    throw new TemplateException("Template '{0}' not found.".FormatWith(requestedVirtualTemplateName));
                return null;
            }

            var bucket = _compiledTemplateTypeCache.GetOrAdd(resourceName, key => CreateBucket(requestedVirtualTemplateName));

            // note that the types are cached, and not the template instances
            var template =  bucket
                .GeneratedTemplateType
                .CreateInstance()
                .CastTo<ITemplateInternal>()
                .SetVirtualPath(ContentManager.TryGetVirtualPath(requestedVirtualTemplateName))
                .SetGeneratedSourceCode(bucket.GeneratedSourceCode)
                .SetContext(_razorContext);

            return template;
        }

        public void ClearTypeCache()
        {
            _compiledTemplateTypeCache.Clear();
        }

        #endregion

        #region Implementation of IDisposable

        public void Dispose() {
            Dispose(true);
        }

        private void Dispose(bool disposing) {
            if (!disposing) return;
            var provider = ContentManager;
            ContentManager = null;
            if (provider == null) return;
            provider.ContentProvider.ContentModified -= OnContentModified;
            provider.SharedContentModified -= OnSharedContentModified;
            provider.Dispose();
        }

        #endregion

        #region Private

        private void OnContentModified(object sender, ContentModifiedArgs e)
        {
            CacheBucket value;
            _compiledTemplateTypeCache.TryRemove(e.ModifiedResourceName, out value);
        }

        private void OnSharedContentModified(object sender, ContentModifiedArgs e)
        {
            // clear the whole cache is any shared content has been modified
            ClearTypeCache();
        }

        private CacheBucket CreateBucket(string reqestedPath) {
            var virtualPath = ContentManager.TryGetVirtualPath(reqestedPath);
            var resourceName = ContentManager.TryGetResourceName(virtualPath);
            var content = ContentManager.TryGetContent(virtualPath);
            string className, rootNamespace;
            GetClassName(virtualPath, out rootNamespace, out className);
            string generatedSource;
            var assembly = CreateAssembly(resourceName, @rootNamespace, className, content, out generatedSource);
            return new CacheBucket { GeneratedTemplateType = assembly.GetType(rootNamespace + "." + className, true, false), GeneratedSourceCode = generatedSource };
        }

        private Assembly CreateAssembly(string resourceName, string rootNamespace, string className, string content, out string generatedSource)
        {
            using (var codeProvider = CreateCodeProvider())
            {
                using (var stringReader = new StringReader(content)) {
                    var generatorResult = _razorEngine.GenerateCode(
                        stringReader,
                        className,
                        rootNamespace,
                        "{0}{1}".FormatWith(_sourceFilenamePrefix, resourceName)
                        );
                    generatedSource = _razorContext.Config.Templates.IncludeGeneratedSourceCode ? GenerateSourceCode(generatorResult.GeneratedCode) : null;

                    if (!CheckParseResults(resourceName, generatorResult, content))
                        return null;


                    var compilerParameter = new CompilerParameters(_razorContext.Config.References.ToArray()) { GenerateInMemory = true, CompilerOptions = "/optimize" };

                    var compilerResults = codeProvider.CompileAssemblyFromDom(compilerParameter, generatorResult.GeneratedCode);

                    return !CheckCompileResults(generatorResult, compilerResults, content, generatedSource) ? null : compilerResults.CompiledAssembly;
                }
            }
        }

        private void GetClassName(string virtualTemplatePath, out string @namespace, out string className)
        {
            virtualTemplatePath = new VirtualPathBuilder(_razorContext.Config.RootOperator.Path)
                .CombineWith(virtualTemplatePath)
                .Normalize()
                .WithRootOperator()
                .RemoveExtension()
                .ToString()
                .RemoveRoot();
            var sb = new StringBuilder();
            foreach(var ch in virtualTemplatePath)
            {
                if ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || ch=='_')
                    sb.Append(ch);
                else if (ch == '/')
                    sb.Append('.');
                
            }
            className = sb.ToString();
            var index = className.LastIndexOf(".");
            if (index == -1)
            {
                @namespace = _razorEngine.Host.DefaultNamespace;
            }
            else
            {
                @namespace = className.Substring(0, index);
                className = className.Substring(index + 1);
            }
        }

        private CodeDomProvider CreateCodeProvider()
        {
            return _razorEngine
                .Host
                .CodeLanguage
                .CodeDomProviderType
                .CreateInstance()
                .CastTo<CodeDomProvider>();
        }

        private static bool CheckParseResults(string resourceName, GeneratorResults generatorResults, string templateContent)
        {
            if (!generatorResults.ParserErrors.Any()) 
                return true;

            var contentlines = templateContent
                .Split('\n')
                .Select(line => line.Trim())
                .ToList();
            string parseExceptionMessage;

            try
            {
                parseExceptionMessage = string.Join(
                    Environment.NewLine,
                    generatorResults
                        .ParserErrors
                        .Select(error => "parse error at template: {0}{1}line {2}: {3}{1}parse error: {4}".FormatWith(resourceName, Environment.NewLine, error.Location.LineIndex + 1, contentlines[error.Location.LineIndex], error.Message))
                        .ToArray()
                );
            }
            catch
            {
                // on any error create a raw error message
                parseExceptionMessage = string.Join(
                    Environment.NewLine,
                    generatorResults
                        .ParserErrors
                        .Select(error => error.ToString())
                        .ToArray());
            }

            throw new TemplateParseException(parseExceptionMessage);
        }
        private bool CheckCompileResults(GeneratorResults generatorResults, CompilerResults compilerResults, string templateContent, string generatedSource)
        {
            if (!compilerResults.Errors.HasErrors) 
                return true;

            List<string> sourceLines = null;
            Func<List<string>> generatedSourceLines = () => sourceLines ?? (sourceLines = (generatedSource ?? GenerateSourceCode(generatorResults.GeneratedCode)).Split('\n').Select(line => line.Trim()).ToList());

            var contentlines = templateContent
                .Split('\n')
                .Select(line => line.Trim())
                .ToList();

            string compileExceptionMessage;
            try
            {
                compileExceptionMessage =
                    string.Join(
                        "{0}{0}".FormatWith(Environment.NewLine),
                        compilerResults
                            .Errors
                            .OfType<CompilerError>()
                            .Where(error => !error.IsWarning)
                            .Select(error => "compile error at {0}{1}line {2}: {3}{1}compile error: {4}: {5}".FormatWith(error.FileName, Environment.NewLine, error.Line, (error.FileName ?? "").StartsWith(_sourceFilenamePrefix) ? contentlines[error.Line - 1] : generatedSourceLines()[error.Line - 1], error.ErrorNumber, error.ErrorText))
                            .ToArray()
                    );
            }
            catch
            {
                // on any error create a raw error message
                compileExceptionMessage = string.Join(
                        "{0}{0}".FormatWith(Environment.NewLine),
                        compilerResults
                            .Errors
                            .OfType<CompilerError>()
                            .Where(error => !error.IsWarning)
                            .Select(error => error.ToString())
                            .ToArray());
            }

            throw new TemplateCompileException(compileExceptionMessage);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "StringWriter can be disposed more than once.")]
        private string GenerateSourceCode(CodeCompileUnit compileunit)
        {
            using (var sw = new StringWriter())
            {
                using (var tw = new IndentedTextWriter(sw, "    "))
                    CreateCodeProvider().GenerateCodeFromCompileUnit(compileunit, tw, new CodeGeneratorOptions());
                return sw.ToString();
            }
        }

        #endregion

    }
}

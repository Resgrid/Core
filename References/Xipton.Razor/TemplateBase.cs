#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CSharp.RuntimeBinder;
using Xipton.Razor.Core;
using Xipton.Razor.Extension;

namespace Xipton.Razor
{
    /// <summary>
    /// All templates must inherit this base class. 
    /// <remarks>
    /// If you create your custom template base remember to always create two custom base classes.
    /// First you must create your custom subclass, for example named MyTemplateBase, that inherits TemplateBase
    /// Next you must create the generic type MyTemplateBase&lt;T> (same name, one type parameter!) that inherits MyTemplateBase and impements ITemplate&lt;T>
    /// </remarks>
    /// </summary>
    public abstract class TemplateBase : ITemplateInternal
    {
        #region Types
        private interface IWriteAttributeArg{
            IWriteAttributeArg SetArg(object arg);
        }

        private class WriteAttributeArg<T> : IWriteAttributeArg {
            private Tuple<Tuple<string, int>, Tuple<T, int>, bool>
                _arg;

            public IWriteAttributeArg SetArg(object arg){
                _arg = (Tuple<Tuple<string, int>, Tuple<T, int>, bool>) arg;
                return this;
            }

            public override string ToString()
            {
                return "{0}".FormatWith(_arg != null ? _arg.Item2.Item1 : default(T));
            }
        }

        #endregion

        #region Fields

        private const int
            _maxHierarchicalTreeDepth = 5;
        private int
            _hierarchicalTreeDepth;


        private static readonly ConcurrentDictionary<Type, Type>
            _cachedTypes = new ConcurrentDictionary<Type, Type>();

        private readonly ConcurrentDictionary<string,Action>
            _sections = new ConcurrentDictionary<string, Action>();

        private StringBuilder
            _outputTarget = new StringBuilder();

        private string 
            _finalResult; // once the _finalResult is set the redered result is completed and locked.

        private dynamic
            _model,
            _viewBag;

        private List<ITemplate>
            _childs;

        #endregion

        #region Methods invoked by code generator
        /// <summary>
        /// Handles markups like href="@Model.Entry.Id" and href="~/". 
        /// If the attribute value results into a null or an empty value, then the attribute is not rendered at all.
        /// This call comes in from the Razor compiler and is not intended to be used at your template.
        /// </summary>
        protected virtual void WriteAttribute(string attr, Tuple<string, int> startTag, Tuple<string, int> endTag, params object[] args){
            var writtenArgCount = 0;
            foreach (var arg in args){
                if (arg == null)
                    continue;

                var type = arg.GetType().GetGenericArguments()[1].GetGenericArguments()[0];

                var value = _cachedTypes
                    .GetOrAdd(type, t => typeof (WriteAttributeArg<>).MakeGenericType(type))
                    .CreateInstance()
                    .CastTo<IWriteAttributeArg>()
                    .SetArg(arg).ToString();

                if (value.NullOrEmpty())
                    continue;

                if (writtenArgCount++ == 0)
                    WriteLiteral(startTag.Item1);
                Write(value);
            }
            if (writtenArgCount > 0)
                WriteLiteral(endTag.Item1);
        }

        /// <summary>
        /// Resolves the URL.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="normalize">if set to <c>true</c> normalize the url, i.e., replace the root operator with the application root and resolve relative path jumps (like '..').</param>
        /// <returns></returns>
        public virtual string ResolveUrl(string path, bool normalize = true)
        {
            if (path.NullOrEmpty() || path[0] == '/' || path.Contains(":")) return path;
            var args = string.Empty;
            var argIndex = path.IndexOf('?');
            if (argIndex >= 0)
            {
                args = path.Substring(argIndex, path.Length - argIndex);
                path = path.Substring(0, argIndex);
            }
            else{
                argIndex = path.IndexOf('#');
                if (argIndex >= 0) {
                    args = path.Substring(argIndex, path.Length - argIndex);
                    path = path.Substring(0, argIndex);
                }
            }
            var builder = VirtualLocation.CombineWith(path);
            if (normalize)
                builder.Normalize();
            return builder + args;
        }

        /// <summary>
        /// This call comes in from the Razor runtime parser and is not intended to be used at your template.
        /// </summary>
        public virtual void Execute()
        {
            // overridden by Razor
        }

        public virtual void Write(object value)
        {
            if (value == null) return;

            if (!HtmlEncode) {
                WriteLiteral(value);
                return;
            }

            var literal = value as LiteralString;
            if (literal != null){
                WriteLiteral(literal);
                return;
            }
            var text = value as string ?? value.ToString();
            WriteLiteral(text.HtmlEncode());
        }
        public virtual void WriteLiteral(object value)
        {
            _outputTarget.Append(value);
        }

        protected virtual void DefineSection(string name, Action action)
        {
            Action value;
            _sections.TryRemove(name, out value);
            _sections.TryAdd(name, action);
        }
        #endregion

        #region ITemplateInternal
        ITemplateInternal ITemplateInternal.SetModel(object model)
        {
            Model = TryWrapModel(model);
            return this;
        }
        ITemplateInternal ITemplateInternal.SetViewBag(object viewBag)
        {
            if (viewBag == null || viewBag is IDynamicMetaObjectProvider)
                _viewBag = viewBag;
            else if (viewBag is IEnumerable<KeyValuePair<string, object>>)
                _viewBag = new DynamicData(viewBag.CastTo<IEnumerable<KeyValuePair<string, object>>>());
            else
                _viewBag = new DynamicData(viewBag);
            return this;
        }

        ITemplateInternal ITemplateInternal.AddChild(ITemplateInternal child)
        {
            _childs = _childs ?? new List<ITemplate>();
            _childs.Add(child);
            return this;
        }
        ITemplateInternal ITemplateInternal.ApplyLayout(ITemplateInternal layoutTemplate)
        {
            if (layoutTemplate.Parent != null)
                throw new TemplateException("You cannot apply a layout more than once. The layout in argument 'layoutTemplate' already has been applied before. You must create a new layout instance on each assignment.");

            if (_finalResult != null)
                throw new TemplateException("You cannot apply more than one layout to a template. This template already had a layout applied.");

            layoutTemplate.SetParent(this);
            _finalResult = _outputTarget.ToString();
            _outputTarget.Clear();
            layoutTemplate.Execute();
            layoutTemplate.TryApplyLayout();
            _finalResult = layoutTemplate.Result;
            return this;
        }
        ITemplateInternal ITemplateInternal.TryApplyLayout()
        {
            if (Layout.NullOrEmpty())
                return this;

            var layoutPath = VirtualLocation.CombineWith(Layout).Normalize();

            var layoutTemplate = RazorContext
                .TemplateFactory
                .CreateTemplateInstance(layoutPath)
                .CastTo<ITemplateInternal>();

            this.CastTo<ITemplateInternal>().ApplyLayout(layoutTemplate);

            return this;
        }
        ITemplateInternal ITemplateInternal.SetVirtualPath(string virtualPath)
        {
            VirtualPath = virtualPath;
            return this;
        }

        ITemplateInternal ITemplateInternal.SetGeneratedSourceCode(string generatedSourceCode){
            GeneratedSourceCode = generatedSourceCode;
            return this;
        }

        ITemplateInternal ITemplateInternal.SetParent(ITemplateInternal parent)
        {
            Parent = parent;
            if (parent != null){
                _hierarchicalTreeDepth = parent.CastTo<TemplateBase>()._hierarchicalTreeDepth + 1;
                parent.AddChild(this);
            }
            else{
                _hierarchicalTreeDepth = 0;
            }
            if (_hierarchicalTreeDepth > _maxHierarchicalTreeDepth){
                throw new TemplateTreeException("The hierarchical tree depth overflows the MaxHierarchicalTreeDepth of {0}. Did you configure recursive layout settings? Did you set a layout (at a _viewStart template) that is not at the shared folder resulting in a recursive loop? ".FormatWith(_maxHierarchicalTreeDepth));
            }
            return this;
        }
        string ITemplateInternal.RenderSectionByChildRequest(string sectionName)
        {
            ITemplateInternal @this = this;

            Action renderAction;
            if (!_sections.TryGetValue(sectionName, out renderAction))
                return @this.Parent == null ? string.Empty : @this.Parent.CastTo<ITemplateInternal>().RenderSectionByChildRequest(sectionName);

            var previous = _outputTarget;
            var output = _outputTarget = new StringBuilder();
            renderAction();
            _outputTarget = previous;
            return output.ToString();
        }
        ITemplateInternal ITemplateInternal.SetContext(RazorContext context)
        {
            RazorContext = context;
            OnContextSet();
            return this;
        }
        ITemplateInternal ITemplateInternal.Execute()
        {
            try{
                Execute();
            }
            catch(RuntimeBinderException ex){
                switch(ex.Message){
                    case "Cannot perform runtime binding on a null reference":
                        throw new TemplateBindingException("Runtime binding error: {0}. Did you forget to initialize the Model?".FormatWith(ex.Message));
                    default:
                        throw new TemplateBindingException("Runtime binding error: {0}".FormatWith(MakeFriendlyMessage(ex.Message)), ex);
                }
            }
            catch(Exception ex){
                throw new TemplateBindingException("Execute error: {0}".FormatWith(ex.Message), ex);
            }
            return this;
        }

        /// <summary>
        /// Attempt to make runtime binder exception message a bit more descriptive.
        /// </summary>
        private static string MakeFriendlyMessage(string runtimeBinderMessage){
            if (runtimeBinderMessage.NullOrEmpty()) return runtimeBinderMessage;
            runtimeBinderMessage = runtimeBinderMessage.Replace("'{0}'".FormatWith(typeof(DynamicData).FullName), "ViewBag or Model");
            if (runtimeBinderMessage.StartsWith("'"))
                runtimeBinderMessage = "Model type " + runtimeBinderMessage;
            return runtimeBinderMessage;
        }

        protected virtual void OnContextSet(){
            if (Parent == null)
                HtmlEncode = RazorContext.Config.Templates.HtmlEncode;
        }
        #endregion

        #region ITemplate

        #region MVC complient
        /// <summary>
        /// Gets or sets the virtual path for the layout. If this property is set then
        /// the layout is attempted to be applied during execution of this template. 
        /// If then the layout can not be resolved an exception is thrown.
        /// </summary>
        public virtual string Layout { get; set; }

        /// <summary>
        /// Gets the model or the Parent's model (recursively) if the current model is null.
        /// </summary>
        public virtual dynamic Model
        {
            get
            {
                ITemplateInternal @this = this;
                return _model ?? (@this.Parent != null ? @this.Parent.Model : null);
            }
            private set { _model = value; }
        }

        /// <summary>
        /// Gets the view bag or the Parent's view bag (recursively) if the current view bag is null.
        /// This property never returns null. It the root's view bag is null, then a new 
        /// instance is created at the root.
        /// </summary>
        public virtual dynamic ViewBag
        {
            get
            {
                ITemplateInternal @this = this;
                var viewBag = _viewBag ?? (@this.Parent != null ? @this.Parent.ViewBag : null);
                return viewBag ?? (_viewBag = new DynamicData());
            }
        }

        /// <summary>
        /// Renders the parent's body (invokable at a layout template only). If no parent exists an exception is thrown.
        /// </summary>
        /// <returns></returns>
        public virtual LiteralString RenderBody()
        {
            ITemplateInternal @this = this;
            if (@this.Parent == null)
            {
                throw new TemplateException("Can only invoke RenderBody() at a child (layout) template.");
            }
            return new LiteralString(@this.Parent.Result);
        }

        /// <summary>
        /// Renders any parent's section (i.e., the first parent's section with the rquested name).
        /// </summary>
        /// <param name="sectionName">Name of the section.</param>
        /// <param name="required">if set to <c>true</c> an exception is throw if the section was not found.</param>
        /// <returns></returns>
        public virtual LiteralString RenderSection(string sectionName, bool required = true)
        {
            ITemplateInternal @this = this;
            if (@this.Parent == null)
            {
                throw new TemplateException("Can only invoke RenderSection() at child templates (layouts).");
            }
            if (!@this.Parent.IsSectionDefined(sectionName))
            {
                if (required)
                    throw new TemplateException("Required section '{0}' not found at any parent template of layout '{1}'. If you want to render this section conditionally invoke RederSection with argument required=false".FormatWith(sectionName,@this.VirtualPath));
                return string.Empty;
            }
            return new LiteralString(@this.Parent.CastTo<ITemplateInternal>().RenderSectionByChildRequest(sectionName));
        }

        /// <summary>
        /// Renders the requested page. Optionally you may pass a specific model. By default the current model is used by the rendered page
        /// </summary>
        /// <param name="virtualPath">The virtual path of the page that must be rendered.</param>
        /// <param name="model">The model.</param>
        /// <param name="skipLayout">if set to <c>true</c> the any layout setting is ignored</param>
        /// <returns></returns>
        public virtual LiteralString RenderPage(string virtualPath, object model = null, bool skipLayout=false)
        {
            ITemplateInternal @this = this;
            var page = @this.RazorContext.TemplateFactory.CreateTemplateInstance(VirtualLocation.CombineWith(virtualPath)).CastTo<ITemplateInternal>();
            page.SetModel(model ?? Model);
            page.SetParent(@this);
            page.Execute();
            if (!skipLayout)
                page.TryApplyLayout();
            return new LiteralString(page.Result);
        }

        /// <summary>
        /// Determines whether a section is defined with the specified section name at any parent.
        /// </summary>
        /// <param name="sectionName">Name of the section.</param>
        /// <returns>
        ///   <c>true</c> if any parent defines a section with the specified section name; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool IsSectionDefined(string sectionName)
        {
            ITemplateInternal @this = this;
            return _sections.ContainsKey(sectionName) || (@this.Parent != null && @this.Parent.CastTo<ITemplate>().IsSectionDefined(sectionName));
        }

        /// <summary>
        /// Gets or sets a value indicating whether the method Write wil HTML encode the written output.
        /// The whole template tree uses the same root setting
        /// </summary>
        /// <value>
        ///   <c>true</c> if HTML encoding must be used; otherwise, <c>false</c>.
        /// </value>
        public bool HtmlEncode{
            get { return RootOrSelf.CastTo<TemplateBase>()._htmlEncode; }
            set { RootOrSelf.CastTo<TemplateBase>()._htmlEncode = value; }
        }
        private bool _htmlEncode;

        public virtual LiteralString Raw(string value) {
            return new LiteralString(value);
        }


        #endregion

        /// <summary>
        /// Gets the generated source. The generated source is set (at debug mode only) at <see cref="TemplateFactory"/>. 
        /// </summary>
        public string GeneratedSourceCode { get; private set; }

        /// <summary>
        /// Gets the parent if any. Else return null.
        /// This template has a parent if, for example, this template is a layout template. Then the
        /// parent holds the template that currently applies this layout. Also if this template
        /// is rendered by a parent using <see cref="RenderPage"/> the Parent holds the invoking template.
        /// </summary>
        public virtual ITemplate Parent { get; private set; }

        /// <summary>
        /// Gets the childs, i.e., the layouts or other rendered pages. This collection is filled on execution
        /// and can be used after complete execution.
        /// </summary>
        public IList<ITemplate> Childs {
            get { return (_childs ?? (_childs = new List<ITemplate>())).AsReadOnly(); }
        }

        /// <summary>
        /// Gets the root if it exists. Else return this instance.
        /// This property never returns null.
        /// </summary>
        public ITemplate RootOrSelf {
            get { return Parent == null ? this : Parent.RootOrSelf; }
        }

        /// <summary>
        /// Gets this template's path builder. It always returns a fresh new instance.
        /// </summary>
        public virtual VirtualPathBuilder VirtualLocation {
            get {
                return new VirtualPathBuilder(RazorContext.Config.RootOperator.Path)
                    .CombineWith(VirtualPath)
                    .RemoveLastPart(); // remove actual resource target
            }
        }
        /// <summary>
        /// Gets the virtual path for this template.
        /// </summary>
        public virtual string VirtualPath { get; private set; }

        /// <summary>
        /// Gets the generator context which brings you access to the Config and TemplateFactory.
        /// </summary>
        public virtual RazorContext RazorContext { get; private set; }

        /// <summary>
        /// Gets the currently rendered result.
        /// </summary>
        public virtual String Result
        {
            get
            {
                return _finalResult ?? _outputTarget.ToString();
            }
        }
        #endregion

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents this instance's Result.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String"/> that represents this instance's Result.
        /// </returns>
        public override string ToString() {
            return Result;
        }


        /// <summary>
        /// Gets the or creates the model at RootOrSelf. 
        /// If the model is null at the first request it is attempted to get instantiated. 
        /// Then if the model type cannot be instantiated an exception is thrown.
        /// </summary>
        /// <typeparam name="TModel">The type of the model.</typeparam>
        /// <returns></returns>
        protected virtual TModel GetOrCreateModel<TModel>()
        {
            if (Model == null)
            {
                try{
                    ((ITemplateInternal) RootOrSelf).SetModel(typeof (TModel).CreateInstance());
                }
                catch(Exception ex){
                    throw new TemplateException("Unable to automatically create a new instance for Model type {0}. Type {0} probably does not have a default constructor. You need to pass a model instance on template execution.".FormatWith(typeof(TModel)),ex);
                }
            }
            return (TModel)Model;
        }

        /// <summary>
        /// The model only needs to be wrapped into a IDynamicMetaObjectProvider if it is an emitted anonymous type.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <returns></returns>
        private static dynamic TryWrapModel(object model)
        {
            return model == null || !Attribute.IsDefined(model.GetType(), typeof(CompilerGeneratedAttribute))
                       ? model
                       : (model is IDynamicMetaObjectProvider
                              ? model
                              : new DynamicData(model));
        }
    }



}
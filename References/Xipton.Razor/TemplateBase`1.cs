#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

namespace Xipton.Razor
{
    /// <summary>
    /// Redefines the Model property, making it typed
    /// </summary>
    /// <typeparam name="TModel">The type of the model.</typeparam>
    public abstract class TemplateBase<TModel> : TemplateBase, ITemplate<TModel>
    {
        /// <summary>
        /// Gets the model or the Parent's model (recursively) if the current model is null.
        /// If no model exists a new model is instantiated at RootOrSelf.
        /// </summary>
        public new TModel Model{
            get{return GetOrCreateModel<TModel>();}
        }

    }

}

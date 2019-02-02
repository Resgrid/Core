#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

namespace Xipton.Razor
{
    /// <summary>
    /// This interface redefines the Model property making it typed
    /// </summary>
    /// <typeparam name="TModel">The type of the model.</typeparam>
    public interface ITemplate<out TModel> : ITemplate
    {
        new TModel Model { get; }
    }
}
#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

namespace Xipton.Razor.Core
{
    internal interface ITemplateInternal : ITemplate
    {
        ITemplateInternal SetModel(object model);
        ITemplateInternal SetViewBag(object viewBag);
        ITemplateInternal SetVirtualPath(string virualPath);
        ITemplateInternal SetGeneratedSourceCode(string generatedSourceCode);
        ITemplateInternal SetContext(RazorContext context);
        ITemplateInternal SetParent(ITemplateInternal parent);
        ITemplateInternal AddChild(ITemplateInternal child);
        ITemplateInternal ApplyLayout(ITemplateInternal layoutTemplate);
        ITemplateInternal Execute();
        ITemplateInternal TryApplyLayout();
        string RenderSectionByChildRequest(string sectionName);
    }
}
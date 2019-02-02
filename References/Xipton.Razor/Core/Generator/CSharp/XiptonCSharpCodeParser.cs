#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Web.Razor.Parser;

namespace Xipton.Razor.Core.Generator.CSharp
{
    /// <summary>
    /// This C# CodeParser is extended for being able to handle the @model directive
    /// </summary>
    public class XiptonCSharpCodeParser : CSharpCodeParser
    {

        private static class Constants
        {
            public const string ModelKeyword = "model";
            public const string ParseErrorInheritsKeywordMustBeFollowedByTypeName = "The 'model' keyword must be followed by a type name on the same line.";
        }
        public XiptonCSharpCodeParser()
        {
            MapDirectives(ModelDirective, new[]{Constants.ModelKeyword});
        }

        protected virtual void ModelDirective()
        {
	        AcceptAndMoveNext();
	        ModelDirectiveCore();
        }

        protected void ModelDirectiveCore()
        {
            BaseTypeDirective(Constants.ParseErrorInheritsKeywordMustBeFollowedByTypeName, modelType => new SetModelCodeGenerator(modelType));
        }

    }
}
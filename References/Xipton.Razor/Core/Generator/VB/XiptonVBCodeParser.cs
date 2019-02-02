#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System.Web.Razor.Generator;
using System.Web.Razor.Parser;
using System.Web.Razor.Parser.SyntaxTree;
using System.Web.Razor.Tokenizer.Symbols;

namespace Xipton.Razor.Core.Generator.VB
{
    /// <summary>
    /// This VB CodeParser is extended for being able to handle the @ModelType directive
    /// </summary>
    public class XiptonVBCodeParser : VBCodeParser
    {

        private static class Constants
        {
            public const string ModelKeyword = "ModelType";
            public const string ParseErrorInheritsKeywordMustBeFollowedByTypeName = "The 'ModelType' keyword must be followed by a type name on the same line.";
        }
        public XiptonVBCodeParser()
        {
            MapDirective(Constants.ModelKeyword, ModelStatement);
        }

        protected virtual bool ModelStatement()
        {
            Span.CodeGenerator = SpanCodeGenerator.Null;
            Context.CurrentBlock.Type = new BlockType?(BlockType.Directive);
            AcceptAndMoveNext();
            var currentLocation = CurrentLocation;
            if (At(VBSymbolType.WhiteSpace))
            {
                Span.EditHandler.AcceptedCharacters = AcceptedCharacters.None;
            }
            AcceptWhile(VBSymbolType.WhiteSpace);
            Output(SpanKind.MetaCode);
            if (EndOfFile || At(VBSymbolType.WhiteSpace) || At(VBSymbolType.NewLine))
            {
                Context.OnError(currentLocation, Constants.ParseErrorInheritsKeywordMustBeFollowedByTypeName);
            }
            AcceptUntil(VBSymbolType.NewLine);
            if (!Context.DesignTimeMode)
            {
                Optional(VBSymbolType.NewLine);
            }
            string model = Span.GetContent();
            Span.CodeGenerator = new SetModelCodeGenerator(model);
            Output(SpanKind.Code);
            return false;
        }

    }

}
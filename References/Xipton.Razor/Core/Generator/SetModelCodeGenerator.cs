#region  Microsoft Public License
/* This code is part of Xipton.Razor v2.2
 * (c) Jaap Lamfers, 2012 - jaap.lamfers@xipton.net
 * Licensed under the Microsoft Public License (MS-PL) http://www.microsoft.com/en-us/openness/licenses.aspx#MPL
 */
#endregion

using System;
using System.CodeDom;
using System.Web.Razor;
using System.Web.Razor.Generator;
using System.Web.Razor.Parser.SyntaxTree;
using Xipton.Razor.Extension;

namespace Xipton.Razor.Core.Generator
{
    /// <summary>
    /// SpanCodeGenerator for the model directive
    /// </summary>
    public class SetModelCodeGenerator : SpanCodeGenerator
    {
		private static readonly char[] _newLineChars = { '\r', '\n' };

        public SetModelCodeGenerator(string modelType){
            ModelType = modelType;
        }

        public override void GenerateCode(Span target, CodeGeneratorContext context)
        {
            context.GeneratedClass.BaseTypes.Clear();
            context.GeneratedClass.BaseTypes.Add(new CodeTypeReference(ResolveType(context)));

            #region Work Around
            if (!(context.Host.CodeLanguage is VBRazorCodeLanguage)) 
                context.GeneratedClass.LinePragma = context.GenerateLinePragma(target, CalculatePadding(context.Host, target, 0));
            //else
                // exclude VBRazorCodeLanguage
                // with VB I found a problem with the #End ExternalSource directive rendered at the GeneratedClass's end while it should not be rendered
                // this only effects the compile error report

            #endregion
        }

		internal static int CalculatePadding(RazorEngineHost host, Span target, int generatedStart)
		{
			if (host == null)
			{
				throw new ArgumentNullException("host");
			}

			if (target == null)
			{
				throw new ArgumentNullException("target");
			}

			int padding;

			padding = CollectSpacesAndTabs(target, host.TabSize) - generatedStart;

			// if we add generated text that is longer than the padding we wanted to insert we have no recourse and we have to skip padding
			// example:
			// Razor code at column zero: @somecode()
			// Generated code will be:
			// In design time: __o = somecode();
			// In Run time: Write(somecode());
			//
			// In both cases the padding would have been 1 space to remote the space the @ symbol takes, which will be smaller than the 6 chars the hidden generated code takes.
			if (padding < 0)
			{
				padding = 0;
			}

			return padding;
		}

	    private static int CollectSpacesAndTabs(Span target, int tabSize)
		{
			Span firstSpanInLine = target;

			string currentContent = null;

			while (firstSpanInLine.Previous != null)
			{
				// When scanning previous spans we need to be break down the spans with spaces.
				// Because the parser doesn't so for example a span looking like \n\n\t needs to be broken down, and we should just grab the \t.
				String previousContent = firstSpanInLine.Previous.Content ?? String.Empty;

				int lastNewLineIndex = previousContent.LastIndexOfAny(_newLineChars);

				if (lastNewLineIndex < 0)
				{
					firstSpanInLine = firstSpanInLine.Previous;
				}
				else
				{
					if (lastNewLineIndex != previousContent.Length - 1)
					{
						firstSpanInLine = firstSpanInLine.Previous;
						currentContent = previousContent.Substring(lastNewLineIndex + 1);
					}

					break;
				}
			}

			// We need to walk from the beginning of the line, because space + tab(tabSize) = tabSize columns, but tab(tabSize) + space = tabSize+1 columns.
			Span currentSpanInLine = firstSpanInLine;

			if (currentContent == null)
			{
				currentContent = currentSpanInLine.Content;
			}

			int padding = 0;
			while (currentSpanInLine != target)
			{
				if (currentContent != null)
				{
					for (int i = 0; i < currentContent.Length; i++)
					{
						if (currentContent[i] == '\t')
						{
							// Example:
							// <space><space><tab><tab>:
							// iter 1) 1
							// iter 2) 2
							// iter 3) 4 = 2 + (4 - 2)
							// iter 4) 8 = 4 + (4 - 0)
							padding = padding + (tabSize - (padding % tabSize));
						}
						else
						{
							padding++;
						}
					}
				}

				currentSpanInLine = currentSpanInLine.Next;
				currentContent = currentSpanInLine.Content;
			}

			return padding;
		}

	    protected virtual string ResolveType(CodeGeneratorContext context)
        {
            var modelType = ModelType.Trim();
            if (context.Host.CodeLanguage is VBRazorCodeLanguage)
                return "{0}(Of {1})".FormatWith(context.Host.DefaultBaseClass, modelType);
            if (context.Host.CodeLanguage is CSharpRazorCodeLanguage)
                return "{0}<{1}>".FormatWith(context.Host.DefaultBaseClass, modelType);
            throw new TemplateException("Code language {0} is not supported.".FormatWith(context.Host.CodeLanguage));
        }

        public string ModelType { get; private set; }

        public override string ToString()
        {
            return "Model:" + ModelType;
        }
        public override bool Equals(object obj)
        {
            var other = obj as SetModelCodeGenerator;
            return other != null && string.Equals(ModelType, other.ModelType, StringComparison.Ordinal);
        }
        public override int GetHashCode()
        {
            return ModelType.GetHashCode();
        }
    }
}
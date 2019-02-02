using System;
using System.Collections.Generic;

namespace OpenPop.Mime.Traverse
{
	/// <summary>
	/// Finds all text/[something] versions in a Message hierarchy
	/// </summary>
	internal class TextVersionFinder : MultipleMessagePartFinder
	{
		protected override List<MessagePart> CaseLeaf(MessagePart messagePart)
		{
			if(messagePart == null)
				throw new ArgumentNullException("messagePart");

			// Maximum space needed is one
			List<MessagePart> leafAnswer = new List<MessagePart>(1);

			if (messagePart.IsText)
				leafAnswer.Add(messagePart);

			return leafAnswer;
		}
	}
}
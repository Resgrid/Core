using System;
using System.Collections.Generic;

namespace OpenPop.Mime.Traverse
{
	///<summary>
	/// Finds all the <see cref="MessagePart"/>s which have a given MediaType using a depth first traversal.
	///</summary>
	internal class FindAllMessagePartsWithMediaType : IQuestionAnswerMessageTraverser<string, List<MessagePart>>
	{
		/// <summary>
		/// Finds all the <see cref="MessagePart"/>s with the given MediaType
		/// </summary>
		/// <param name="message">The <see cref="Message"/> to start looking in</param>
		/// <param name="question">The MediaType to look for. Case is ignored.</param>
		/// <returns>
		/// A List of <see cref="MessagePart"/>s with the given MediaType.<br/>
		/// <br/>
		/// The List might be empty if no such <see cref="MessagePart"/>s were found.<br/>
		/// The order of the elements in the list is the order which they are found using
		/// a depth first traversal of the <see cref="Message"/> hierarchy.
		/// </returns>
		public List<MessagePart> VisitMessage(Message message, string question)
		{
			if(message == null)
				throw new ArgumentNullException("message");

			return VisitMessagePart(message.MessagePart, question);
		}

		/// <summary>
		/// Finds all the <see cref="MessagePart"/>s with the given MediaType
		/// </summary>
		/// <param name="messagePart">The <see cref="MessagePart"/> to start looking in</param>
		/// <param name="question">The MediaType to look for. Case is ignored.</param>
		/// <returns>
		/// A List of <see cref="MessagePart"/>s with the given MediaType.<br/>
		/// <br/>
		/// The List might be empty if no such <see cref="MessagePart"/>s were found.<br/>
		/// The order of the elements in the list is the order which they are found using
		/// a depth first traversal of the <see cref="Message"/> hierarchy.
		/// </returns>
		public List<MessagePart> VisitMessagePart(MessagePart messagePart, string question)
		{
			if(messagePart == null)
				throw new ArgumentNullException("messagePart");

			List<MessagePart> results = new List<MessagePart>();

			if (messagePart.ContentType.MediaType.Equals(question, StringComparison.OrdinalIgnoreCase))
				results.Add(messagePart);

			if (messagePart.IsMultiPart)
			{
				foreach (MessagePart part in messagePart.MessageParts)
				{
					List<MessagePart> result = VisitMessagePart(part, question);
					results.AddRange(result);
				}
			}

			return results;
		}
	}
}
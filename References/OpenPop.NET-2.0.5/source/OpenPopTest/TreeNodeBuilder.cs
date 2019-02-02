using System;
using System.Windows.Forms;
using OpenPop.Mime;
using OpenPop.Mime.Traverse;
using Message = OpenPop.Mime.Message;

namespace OpenPop.TestApplication
{
	/// <summary>
	/// Builds up a <see cref="TreeNode"/> with the same hierarchy as
	/// a <see cref="Message"/>.
	/// 
	/// The root <see cref="TreeNode"/> has the subject as text and has one one child.
	/// The root has no Tag attribute set.
	/// 
	/// The children of the root has the MediaType of the <see cref="MessagePart"/> as text and the
	/// MessagePart's children as <see cref="TreeNode"/> children.
	/// Each <see cref="MessagePart"/> <see cref="TreeNode"/> has a Tag property set to that <see cref="MessagePart"/>
	/// </summary>
	internal class TreeNodeBuilder : IAnswerMessageTraverser<TreeNode>
	{
		public TreeNode VisitMessage(Message message)
		{
			if(message == null)
				throw new ArgumentNullException("message");

			// First build up the child TreeNode
			TreeNode child = VisitMessagePart(message.MessagePart);

			// Then create the topmost root node with the subject as text
			TreeNode topNode = new TreeNode(message.Headers.Subject, new[] { child });

			return topNode;
		}

		public TreeNode VisitMessagePart(MessagePart messagePart)
		{
			if(messagePart == null)
				throw new ArgumentNullException("messagePart");

			// Default is that this MessagePart TreeNode has no children
			TreeNode[] children = new TreeNode[0];

			if(messagePart.IsMultiPart)
			{
				// If the MessagePart has children, in which case it is a MultiPart, then
				// we create the child TreeNodes here
				children = new TreeNode[messagePart.MessageParts.Count];

				for(int i = 0; i<messagePart.MessageParts.Count; i++)
				{
					children[i] = VisitMessagePart(messagePart.MessageParts[i]);
				}
			}

			// Create the current MessagePart TreeNode with the found children
			TreeNode currentNode = new TreeNode(messagePart.ContentType.MediaType, children);

			// Set the Tag attribute to point to the MessagePart.
			currentNode.Tag = messagePart;

			return currentNode;
		}
	}
}
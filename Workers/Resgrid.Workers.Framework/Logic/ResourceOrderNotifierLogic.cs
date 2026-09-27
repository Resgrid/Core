using Resgrid.Model.Services;
using Resgrid.Workers.Framework.Workers.TrainingNotifier;
using System;
using System.Linq;
using Autofac;
using Resgrid.Model.Events;

namespace Resgrid.Workers.Framework.Logic
{
	public class ResourceOrderNotifierLogic
	{
		public Tuple<bool, string> Process(ResourceOrderAddedEvent item)
		{
			bool success = true;
			string result = "";

			if (item != null && item.Order != null && item.Order.Items != null && item.Order.Items.Count > 0)
			{
				
			}

			return new Tuple<bool, string>(success, result);
		}
	}
}

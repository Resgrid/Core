using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Resgrid.Model.Events
{
	public class EventWrapper
	{
		public EventWrapper(object data)
		{
			if (data == null)
				throw new ArgumentNullException("data");
			Data = data;

			Name = Data.GetType().Name;
		}

		public string Name { get; private set; }
		public object Data { get; private set; }
	}


	public interface IDepartmentEvent
	{
		int DepartmentId { get; }
	}


	/*
	 * Event rules.
	 * 
	 * - Use public getters and private setters for data
	 * - Use constructor properties for all the properties (possibly allowing optional arguments for non-required properties)
	 * - Keep them simple (basic data bags) no logic.
	 * 
	 */


	public class StatusUpdateEvent : IDepartmentEvent
	{
		public StatusUpdateEvent(
			int departmentId,
			string name,
			ActionTypes actionType,
			DateTime timestamp
			)
		{
			DepartmentId = departmentId;
			Name = name;
			ActionType = actionType;
			Timestamp = timestamp;
		}

		public int DepartmentId { get; private set; }

		public string Name { get; private set; }
		public ActionTypes ActionType { get; private set; }
		public DateTime Timestamp { get; private set; }
	}
}

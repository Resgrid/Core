using ProtoBuf;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Helpers
{
	public static class SerializerHelper
	{
		public static void WarmUpProtobufSerializer()
		{
			Serializer.PrepareSerializer<Department>();
			Serializer.PrepareSerializer<Address>();
			Serializer.PrepareSerializer<DepartmentMember>();
			Serializer.PrepareSerializer<Payment>();
			Serializer.PrepareSerializer<Plan>();
			Serializer.PrepareSerializer<PlanLimit>();
			Serializer.PrepareSerializer<IdentityUser>();
			Serializer.PrepareSerializer<UserProfile>();
		}
	}
}

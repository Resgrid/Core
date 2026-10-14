using System;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Framework
{
	/// <summary>Messages meant for the user (a Records module not enabled for the department) never become Sentry issues.</summary>
	[TestFixture]
	public class UserFacingExceptionSentryFilterTests
	{
		[Test]
		public void Filter_RecordsModuleDisabled_IsDropped()
		{
			foreach (var module in RecordsPreventionModules.All)
				new UserFacingExceptionSentryFilter().Filter(new RecordsModuleDisabledException(module)).Should().BeTrue(module.ToString());
		}

		[Test]
		public void Filter_NerisSubmissionDisabled_IsDropped()
		{
			var exception = new NerisSubmissionDisabledException();

			new UserFacingExceptionSentryFilter().Filter(exception).Should().BeTrue();
			// Still an InvalidOperationException, so the Web and API catches keep showing its message.
			exception.Should().BeAssignableTo<InvalidOperationException>();
			exception.Message.Should().Be("NERIS submission is not enabled for this department.");
		}

		[Test]
		public void Filter_NerisWorkflowsOff_IsDropped()
		{
			var exception = new NerisWorkflowsOffException();

			new UserFacingExceptionSentryFilter().Filter(exception).Should().BeTrue();
			exception.Should().BeAssignableTo<InvalidOperationException>();
			exception.Message.Should().Be(NerisWorkflows.OffMessage);
		}

		[Test]
		public void Filter_OtherInvalidOperation_IsKept()
		{
			new UserFacingExceptionSentryFilter().Filter(new InvalidOperationException("Sequence contains no elements")).Should().BeFalse();
		}

		[Test]
		public void Filter_Null_IsKept()
		{
			UserFacingExceptionSentryFilter.IsUserFacing(null).Should().BeFalse();
		}

		[Test]
		public void Filter_AggregateOfOnlyUserFacing_IsDropped()
		{
			var aggregate = new AggregateException(new RecordsModuleDisabledException(RecordsPreventionModule.Hydrants),
				new AggregateException(new RecordsModuleDisabledException(RecordsPreventionModule.Permits)));

			UserFacingExceptionSentryFilter.IsUserFacing(aggregate).Should().BeTrue();
		}

		[Test]
		public void Filter_AggregateWithARealFault_IsKept()
		{
			var aggregate = new AggregateException(new RecordsModuleDisabledException(RecordsPreventionModule.Hydrants), new NullReferenceException());

			UserFacingExceptionSentryFilter.IsUserFacing(aggregate).Should().BeFalse();
		}

		[Test]
		public void Filter_EmptyAggregate_IsKept()
		{
			UserFacingExceptionSentryFilter.IsUserFacing(new AggregateException()).Should().BeFalse();
		}

		[Test]
		public void Filter_ReflectionWrapped_IsDropped()
		{
			var wrapped = new TargetInvocationException(new RecordsModuleDisabledException(RecordsPreventionModule.Inspections));

			UserFacingExceptionSentryFilter.IsUserFacing(wrapped).Should().BeTrue();
		}

		[Test]
		public void Filter_RealFaultWrappingAUserFacingOne_IsKept()
		{
			// Only transparent wrappers are looked through; an exception that chose to wrap one is its own fault.
			var wrapped = new ApplicationException("Hydrant import failed", new RecordsModuleDisabledException(RecordsPreventionModule.Hydrants));

			UserFacingExceptionSentryFilter.IsUserFacing(wrapped).Should().BeFalse();
		}
	}
}

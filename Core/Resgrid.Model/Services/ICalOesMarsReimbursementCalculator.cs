using Resgrid.Model.CostRecovery.CalOesMars;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Pure expected-reimbursement arithmetic for a prepared F-42 / expense claim (plan C4, decision 37): personnel
	/// straight/overtime under the agreement's compensation method, official apparatus / support vehicle / POV mileage
	/// / special-equipment lines from the effective rate lines, evidence-gated expenses, and the administrative line.
	/// Every line carries its rate line, eligibility and reason; nothing here reads a database or a Phase E cost.
	/// </summary>
	public interface ICalOesMarsReimbursementCalculator
	{
		CalOesMarsReimbursementResult Calculate(CalOesMarsReimbursementInput input);
	}

	/// <summary>
	/// The seam a future authorized Cal OES connector would implement (plan C4 "no connector fiction"). P0 ships
	/// <c>ManualCalOesMarsGateway</c>: it exposes the reviewed portal address, stores no credential and performs
	/// zero external writes; every external fact is a manual observation recorded by a MARS manager.
	/// </summary>
	public interface ICalOesMarsExternalGateway
	{
		string Name { get; }
		bool SupportsExternalWrites { get; }
		string GetPortalUrl(CalOesMarsWorkItem workItem);
	}
}

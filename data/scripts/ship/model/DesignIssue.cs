namespace Higherfleet.Ships.Model;

public enum DesignIssueSeverity
{
	Warning,
	Error
}

public sealed record DesignIssue(
	DesignIssueSeverity Severity,
	string Code,
	string Message,
	string PartInstanceId = "");

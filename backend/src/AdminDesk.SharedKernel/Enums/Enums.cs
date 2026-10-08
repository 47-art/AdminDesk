namespace AdminDesk.SharedKernel.Enums;

public enum RequestStatus { InProgress, Closed, Rejected, Cancelled }

public enum ApprovalStatus { Pending, Approved, Rejected }

public enum StepState { Pending, Upcoming, NotRequired, Done, Rejected }

public enum RequestAction { Approve, Reject, Complete, Cancel }

public enum Priority { Low, Medium, High, Critical }

public enum FieldType { Text, LongText, Number, Money, Date, DateTime, YesNo, Select, MultiSelect, Lookup }

// Where a form field gets its starting value from. The user can still change it.
public enum FieldDefaultSource { RequesterName }

// The common request fields a module can insist on. Written in definition files as location, project and costCentre.
public enum CommonFieldKey { Location, Project, CostCentre }

public enum StepType { Approval, Task }

public enum RuleOperator { Eq, Neq, Gt, Gte, Lt, Lte, In, NotIn, IsEmpty, IsNotEmpty }

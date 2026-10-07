namespace AdminDesk.SharedKernel.Enums;

public enum RequestStatus { InProgress, Closed, Rejected, Cancelled }

public enum ApprovalStatus { Pending, Approved, Rejected }

public enum StepState { Pending, Upcoming, NotRequired, Done, Rejected }

public enum RequestAction { Approve, Reject, Complete, Cancel }

public enum Priority { Low, Medium, High, Critical }

public enum FieldType { Text, LongText, Number, Money, Date, DateTime, YesNo, Select, MultiSelect, Lookup }

public enum StepType { Approval, Task }

public enum RuleOperator { Eq, Neq, Gt, Gte, Lt, Lte, In, NotIn, IsEmpty, IsNotEmpty }

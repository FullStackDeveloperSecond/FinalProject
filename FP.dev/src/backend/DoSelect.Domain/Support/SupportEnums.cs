namespace DoSelect.Domain.Support;

public enum SupportTicketCategory { Order, Payment, Logistics, ProductWarranty, ReturnHelp, Account, Other }
public enum CasePriority { Low, Normal, High, Urgent }
public enum SupportTicketStatus { Open, Assigned, InProgress, WaitingForCustomer, WaitingForInternal, Resolved, Closed, Cancelled }
public enum SupportSenderType { Member, Admin, System }
public enum AssignmentAction { Claim, Assign, Reassign, Unassign }
public enum SupportSlaEventType { Started, Paused, Resumed, Warning80, Overdue100, Reopened, Closed }
public enum SupportSlaTargetType { FirstResponse, Resolution }
public enum SupportSummaryStatus { Pending, Completed, Failed, Obsolete }
public enum PrivateAttachmentScanStatus { Pending, Clean, Rejected, Failed }

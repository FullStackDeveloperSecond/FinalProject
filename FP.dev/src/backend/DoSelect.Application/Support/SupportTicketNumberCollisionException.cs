namespace DoSelect.Application.Support;

/// <summary>
/// Signals that a ticket insert failed the TicketNumber unique constraint. ISupportTicketStore
/// implementations throw this (never a raw DbUpdateException/SqlException) so Application can
/// retry with a fresh candidate number without knowing about SQL Server error numbers or index
/// names. Never allowed to reach the Api layer directly — it is always either retried or
/// translated into a DomainProblemException once retries are exhausted.
/// </summary>
public sealed class SupportTicketNumberCollisionException : Exception
{
    public SupportTicketNumberCollisionException(string ticketNumber, Exception innerException)
        : base($"TicketNumber '{ticketNumber}' collided with an existing ticket.", innerException)
    {
        TicketNumber = ticketNumber;
    }

    public string TicketNumber { get; }
}

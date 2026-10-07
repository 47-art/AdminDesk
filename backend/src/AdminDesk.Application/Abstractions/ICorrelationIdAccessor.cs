namespace AdminDesk.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}

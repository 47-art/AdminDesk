namespace AdminDesk.Application.Jobs;

// The only way application code schedules background work. The implementation is
// chosen by configuration, so handlers do not depend on a particular job library.
public interface IJobScheduler
{
    // Registers or replaces a recurring job. The cron text uses five fields, or six
    // when the first field is seconds; providers may accept only some forms.
    void AddOrUpdateRecurring<THandler>(string id, string cron) where THandler : IJobHandler;

    // Runs the handler once after the delay. Returns the job id.
    string Schedule<THandler>(TimeSpan delay, object? args = null) where THandler : IJobHandler;

    // Runs the handler once as soon as a worker is free. Returns the job id.
    string Enqueue<THandler>(object? args = null) where THandler : IJobHandler;
}

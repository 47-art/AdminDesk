using Microsoft.Extensions.Configuration;
using Serilog;

namespace AdminDesk.Infrastructure.Logging;

// Seam for attaching extra Serilog sinks without the host knowing them.
// Implementations need a public parameterless constructor (they are created before
// dependency injection exists), must not throw out of Configure, and use
// reportFailure to tell the console and file log about their own problems. The host
// routes that callback to the main logger with the skip-persistence property set.
public interface ILogSinkContributor
{
    int Order { get; }

    void Configure(LoggerConfiguration logger, IConfiguration configuration, Action<string, Exception?> reportFailure);
}

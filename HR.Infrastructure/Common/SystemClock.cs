using HR.Application.Abstractions;

namespace HR.Infrastructure.Common;

/// <summary>Real UTC clock behind the application's IClock port.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

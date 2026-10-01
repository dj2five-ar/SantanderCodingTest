namespace CodingTest.Infrastructure.Configuration;

public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>Timeout (seconds) for a single HTTP attempt before it is abandoned.</summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Hard budget (seconds) covering the initial attempt + all retries + backoff delays
    /// Should be at least <see cref="AttemptTimeoutSeconds"/> × (<see cref="RetryMaxAttempts"/> + 1)
    /// </summary>
    public int TotalTimeoutSeconds { get; set; } = 30;

    /// <summary>Number of additional attempts after the initial one.</summary>
    public int RetryMaxAttempts { get; set; } = 2;

    /// <summary>Base delay (milliseconds) for exponential back-off between retries.</summary>
    public int RetryBaseDelayMs { get; set; } = 200;

    /// <summary>
    /// Fraction of calls (0–1) that must fail within the sampling window to trip the circuit
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Rolling window (seconds) over which the failure ratio is measured.</summary>
    public int CircuitBreakerSamplingWindowSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum number of calls in the sampling window required before the circuit can trip
    /// Prevents false positives during startup when call volume is low
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>
    /// How long (seconds) the circuit stays open before allowing a single probe request
    /// through (half-open state)
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;
}

namespace Mud.HttpUtils;

public class MudHttpClientApplicationOptions
{
    public const string SectionName = "MudHttpClients";

    public Dictionary<string, MudHttpClientOptions> Clients { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? DefaultClientName { get; set; }
}

public class MudHttpClientOptions
{
    public string? BaseAddress { get; set; }

    public int? TimeoutSeconds { get; set; }

    public Dictionary<string, string>? DefaultHeaders { get; set; }

    public string? TokenManagerKey { get; set; }

    public string? TokenInjectionMode { get; set; }

    public string? TokenScopes { get; set; }

    public bool? AllowAnyStatusCode { get; set; }

    public string? SerializationMethod { get; set; }

    public MudHttpClientResilienceOptions? Resilience { get; set; }
}

public class MudHttpClientResilienceOptions
{
    public MudHttpClientRetryOptions? Retry { get; set; }

    public MudHttpClientCircuitBreakerOptions? CircuitBreaker { get; set; }

    public MudHttpClientTimeoutOptions? Timeout { get; set; }
}

public class MudHttpClientRetryOptions
{
    public bool Enabled { get; set; }

    public int MaxRetries { get; set; } = 3;

    public int DelayMilliseconds { get; set; } = 1000;

    public bool UseExponentialBackoff { get; set; } = true;
}

public class MudHttpClientCircuitBreakerOptions
{
    public bool Enabled { get; set; }

    public int FailureThreshold { get; set; } = 5;

    public int BreakDurationSeconds { get; set; } = 30;
}

public class MudHttpClientTimeoutOptions
{
    public bool Enabled { get; set; }

    public int TimeoutMilliseconds { get; set; } = 30000;
}

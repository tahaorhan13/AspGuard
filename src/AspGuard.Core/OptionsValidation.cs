namespace AspGuard;

/// <summary>Option checks shared by the DI registration and the .NET Framework entry point.</summary>
internal static class OptionsValidation
{
    /// <summary>Returns the first problem, or <c>null</c> when the options are valid.</summary>
    public static string? Validate(AspGuardOptions options)
    {
        if (options.NPlusOneThreshold < 2)
        {
            return "NPlusOneThreshold must be at least 2.";
        }

        if (options.MaxQueriesPerScope < 1)
        {
            return "MaxQueriesPerScope must be at least 1.";
        }

        if (options.UpdateBaseline && string.IsNullOrEmpty(options.BaselineFile))
        {
            return "UpdateBaseline requires BaselineFile.";
        }

        if (options.Security.RateLimit.PermitLimit < 1)
        {
            return "Security.RateLimit.PermitLimit must be at least 1.";
        }

        if (options.Security.RateLimit.Window <= TimeSpan.Zero)
        {
            return "Security.RateLimit.Window must be positive.";
        }

        return options.Security.Hsts.MaxAge < TimeSpan.Zero ? "Security.Hsts.MaxAge must not be negative." : null;
    }
}

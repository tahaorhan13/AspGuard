using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace AspGuard;

internal readonly record struct CallSite(string? File, int Line, string Method)
{
    public string Display => File is null ? Method : $"{File}:{Line} ({Method})";
}

/// <summary>Finds the first frame in user code (skipping EF Core, BCL, Microsoft and AspGuard frames).</summary>
internal static class CallSiteResolver
{
    private static readonly ConcurrentDictionary<Assembly, bool> _infrastructure = new();
    private static readonly ConcurrentDictionary<MethodBase, bool> _attributed = new();

    public static CallSite? Capture(bool checkAttributes, out bool attributed)
    {
        var trace = new StackTrace(fNeedFileInfo: true);
        CallSite? site = null;
        CallSite? getterSite = null;
        attributed = false;

        foreach (var frame in trace.GetFrames())
        {
            var method = frame.GetMethod();
            var assembly = method?.DeclaringType?.Assembly;
            if (method is null || assembly is null || IsInfrastructure(assembly))
            {
                continue;
            }

            if (site is null)
            {
                var candidate = new CallSite(frame.GetFileName(), frame.GetFileLineNumber(), FormatMethod(method));

                // Lazy loading goes through entity property getters; the code that touched the property is more useful.
                if (method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal))
                {
                    getterSite ??= candidate;
                }
                else
                {
                    site = candidate;
                }
            }

            if (!checkAttributes)
            {
                if (site is not null)
                {
                    break;
                }

                continue;
            }

            if (IsAttributed(method))
            {
                attributed = true;
                break;
            }
        }

        return site ?? getterSite;
    }

    private static bool IsInfrastructure(Assembly assembly) =>
        _infrastructure.GetOrAdd(assembly, static a =>
        {
            var name = a.GetName().Name ?? string.Empty;
            return name is "AspGuard.Core" or "AspGuard" or "System" or "mscorlib" or "netstandard"
                or "DynamicProxyGenAssembly2"
                || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.", StringComparison.Ordinal)
                || name.StartsWith("Castle.", StringComparison.Ordinal);
        });

    private static bool IsAttributed(MethodBase method) =>
        _attributed.GetOrAdd(method, static m =>
        {
            var (type, name) = Resolve(m);
            for (var t = type; t is not null; t = t.DeclaringType)
            {
                if (t.IsDefined(typeof(GuardQueriesAttribute), inherit: true))
                {
                    return true;
                }
            }

            if (type is null)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var candidate in type.GetMethods(flags))
            {
                if (candidate.Name == name && candidate.IsDefined(typeof(GuardQueriesAttribute), inherit: true))
                {
                    return true;
                }
            }

            return false;
        });

    private static string FormatMethod(MethodBase method)
    {
        var (type, name) = Resolve(method);
        return type is null ? name : $"{type.FullName ?? type.Name}.{name}";
    }

    /// <summary>Maps compiler-generated frames (async state machines, lambdas, local functions) back to the user-written type and method.</summary>
    private static (Type? Type, string Name) Resolve(MethodBase method)
    {
        var type = method.DeclaringType;
        var name = method.Name;

        if (name.StartsWith('<'))
        {
            name = Between(name, name);
        }

        while (type is not null && type.Name.StartsWith('<'))
        {
            if (type.Name.Contains(">d__", StringComparison.Ordinal))
            {
                name = Between(type.Name, name);
            }

            type = type.DeclaringType;
        }

        return (type, name);
    }

    private static string Between(string value, string fallback)
    {
        var end = value.IndexOf('>', StringComparison.Ordinal);
        return end > 1 ? value.Substring(1, end - 1) : fallback;
    }
}

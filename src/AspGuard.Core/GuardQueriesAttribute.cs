namespace AspGuard;

/// <summary>
/// Marks a class or method whose queries should be monitored when
/// <see cref="AspGuardOptions.Scope"/> is <see cref="GuardScopeMode.AttributedOnly"/>.
/// The Roslyn analyzers honor it as well (<c>aspguard.scope = attributed_only</c> in .editorconfig).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class GuardQueriesAttribute : Attribute
{
}

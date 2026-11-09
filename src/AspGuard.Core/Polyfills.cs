// Compiler support types that .NET Framework does not ship. Internal, so they never clash with the consumer's.
namespace System.Runtime.CompilerServices;

#if NETFRAMEWORK
internal static class IsExternalInit
{
}
#endif

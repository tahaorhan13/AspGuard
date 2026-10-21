namespace NPlusGuard.Analyzers.Tests;

public class SmokeTests
{
    [Fact]
    public void Analyzers_assembly_is_referenced()
    {
        Assert.Equal("NPlusGuard.Analyzers", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}

namespace NPlusGuard.AspNetCore.Tests;

public class SmokeTests
{
    [Fact]
    public void AspNetCore_assembly_is_referenced()
    {
        Assert.Equal("NPlusGuard.AspNetCore", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}

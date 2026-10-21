namespace NPlusGuard.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_is_referenced()
    {
        Assert.Equal("NPlusGuard", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}

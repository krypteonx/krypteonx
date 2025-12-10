using Krypteonx.Core.Config;
using Xunit;

namespace Krypteonx.Tests;

public class ChainParametersTests
{
    [Fact]
    public void BlockTargetTime_IsThreeSeconds()
    {
        Assert.Equal(3, (int)ChainParameters.BlockTargetTime.TotalSeconds);
    }
}


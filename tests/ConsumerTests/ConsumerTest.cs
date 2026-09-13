using System;
using Xunit;

namespace TestMod;

public class ConsumerTest
{
    static ConsumerTest()
    {
        var game = typeof(Verse.Mod);
        Console.Error.WriteLine("Fake game type present: " + game.Assembly.GetName().Name);
    }

    [Fact]
    public void Can_Resolve_Types_From_Steam_Mod()
    {
        var assm = typeof(AllIdeoligionsAreFluid.AllIdeoligionsAreFluidMod).Assembly;
        Assert.NotNull(assm);
    }
}
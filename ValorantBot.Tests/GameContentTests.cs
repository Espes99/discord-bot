using ValorantBot.Services;

namespace ValorantBot.Tests;

public class GameContentTests
{
    [Fact]
    public void Apply_adds_new_content_and_keeps_fallback_aliases()
    {
        GameContent.Apply([("Newagent", "Initiator")], [("Newgun", "Shotgun"), ("Newrifle", "Rifle")]);

        Assert.Equal("Initiator", GameContent.RoleOf("Newagent"));
        Assert.Equal("Duelist", GameContent.RoleOf("Jett"));
        Assert.True(GameContent.IsNonPrecision("Newgun"));
        Assert.True(GameContent.IsNonPrecision("Tactical Knife"));
        Assert.False(GameContent.IsNonPrecision("Vandal"));
        Assert.False(GameContent.IsNonPrecision("Unknownpistol"));
        Assert.True(GameContent.IsRifle("Newrifle"));
        Assert.False(GameContent.IsRifle("Spectre"));
    }
}

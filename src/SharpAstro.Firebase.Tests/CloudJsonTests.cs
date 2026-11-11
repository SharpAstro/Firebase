using Shouldly;
using Xunit;

namespace SharpAstro.Firebase.Tests;

/// <summary>
/// The readers answer a fallback instead of throwing, and the two folds (<see cref="CloudJson.Merge"/>
/// and <see cref="CloudJson.SetMember"/>) say null for a shape they cannot fold, which is the event
/// stream's signal to re-read the path.
/// </summary>
public class CloudJsonTests
{
    [Fact]
    public void Quote_Escapes()
    {
        CloudJson.Quote("a\"b").ShouldBe("\"a\\u0022b\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>not json</html>")]
    public void TryParse_NotJson_IsNull(string? json)
    {
        CloudJson.TryParse(json).ShouldBeNull();
    }

    [Fact]
    public void Readers_FallBack_OnAMissingOrMistypedMember()
    {
        using var doc = CloudJson.TryParse("""{"s":"x","n":"7","b":1,"o":{"t":true},"big":1700000000000}""")!;
        var root = doc.RootElement;

        CloudJson.StringOr(root, "s").ShouldBe("x");
        CloudJson.StringOr(root, "missing", "fb").ShouldBe("fb");
        CloudJson.IntOr(root, "n", -1).ShouldBe(-1);            // a string is not a number
        CloudJson.IntOr(root, "big", -1).ShouldBe(-1);          // overflows int
        CloudJson.LongOr(root, "big").ShouldBe(1700000000000);
        CloudJson.IsTrue(root, "b").ShouldBeFalse();            // 1 is not true
        CloudJson.IsTrue(CloudJson.Child(root, "o"), "t").ShouldBeTrue();
        CloudJson.StringOr(CloudJson.Child(root, "absent"), "t", "fb").ShouldBe("fb");
    }

    [Fact]
    public void Merge_ReplacesMembers_AndANullMemberDeletes()
    {
        CloudJson.Merge("""{"a":1,"b":2}""", """{"b":3,"a":null,"c":4}""").ShouldBe("""{"b":3,"c":4}""");
    }

    [Fact]
    public void Merge_IntoNothing_IsThePatch()
    {
        CloudJson.Merge(null, """{"a":1}""").ShouldBe("""{"a":1}""");
    }

    [Theory]
    [InlineData("5", """{"a":1}""")]       // cached is a scalar
    [InlineData("""{"a":1}""", "5")]       // patch is a scalar
    public void Merge_AShapeItCannotFold_IsNull(string cached, string patch)
    {
        CloudJson.Merge(cached, patch).ShouldBeNull();
    }

    [Fact]
    public void SetMember_SetsAndRemoves()
    {
        CloudJson.SetMember("""{"a":1}""", "b", "2").ShouldBe("""{"a":1,"b":2}""");
        CloudJson.SetMember("""{"a":1,"b":2}""", "a", "null").ShouldBe("""{"b":2}""");
        CloudJson.SetMember("5", "a", "1").ShouldBeNull();
    }
}

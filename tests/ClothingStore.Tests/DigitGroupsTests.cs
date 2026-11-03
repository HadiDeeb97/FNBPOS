using ClothingStore.Core.Text;

namespace ClothingStore.Tests;

/// <summary>Pounds typed with their thousands grouped.</summary>
public class DigitGroupsTests
{
    [Theory]
    [InlineData("10000000", "10,000,000")]
    [InlineData("1,0000", "10,000")]           // a digit typed into a grouped number
    [InlineData("999", "999")]
    [InlineData("1234.5", "1,234.5")]           // decimals kept as typed
    [InlineData("1234.", "1,234.")]
    [InlineData("٤٥٠٠٠", "45,000")]             // Arabic digits
    [InlineData("12 500", "12,500")]
    [InlineData("000", "0")]                    // what is left of 1,000 after deleting the 1
    [InlineData("0500", "500")]
    [InlineData("0.5", "0.5")]
    public void Thousands_are_grouped_while_typing(string typed, string expected) =>
        Assert.Equal(expected, DigitGroups.Format(typed, ",", "."));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-500")]
    [InlineData("1.2.3")]
    public void Anything_else_is_left_alone(string typed) => Assert.Null(DigitGroups.Format(typed, ",", "."));

    [Fact]
    public void A_pc_that_groups_with_dots_gets_dots() => Assert.Equal("1.500.000", DigitGroups.Format("1500000", ".", ","));
}

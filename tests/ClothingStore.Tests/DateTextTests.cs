using System.Globalization;
using ClothingStore.Core.Text;

namespace ClothingStore.Tests;

/// <summary>Dates written the way the PC writes them.</summary>
public class DateTextTests
{
    [Theory]
    [InlineData("dd/MM/yyyy", "dd/MM")]
    [InlineData("M/d/yyyy", "M/d")]
    [InlineData("yyyy-MM-dd", "MM-dd")]
    [InlineData("dd.MM.yyyy", "dd.MM")]
    [InlineData("yyyy/MM/dd", "MM/dd")]
    public void The_day_and_month_keep_the_pcs_order(string shortDate, string expected) =>
        Assert.Equal(expected, DateText.DayMonthPattern(shortDate));

    [Fact]
    public void Dates_follow_the_pcs_region_settings()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            var day = new DateTime(2026, 10, 26, 14, 5, 0);
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            Assert.Equal("26/10/2026", DateText.Date(day));
            Assert.Equal("26/10", DateText.DayMonth(day));
            Assert.Equal("26/10/2026 14:05", DateText.DateTime(day));

            var us = (CultureInfo)new CultureInfo("en-US").Clone();
            us.DateTimeFormat.ShortDatePattern = "dd-MMM-yyyy"; // a PC with its own format
            CultureInfo.CurrentCulture = us;
            Assert.Equal("26-Oct-2026", DateText.Date(day));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

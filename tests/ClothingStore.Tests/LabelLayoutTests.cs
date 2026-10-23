using ClothingStore.Desktop.Services;

namespace ClothingStore.Tests;

public class LabelLayoutTests
{
    [Theory]
    [InlineData("L7160", 3, 7)]     // 21 per A4
    [InlineData("L7159", 3, 8)]     // 24 per A4
    [InlineData("L7651", 5, 13)]    // 65 per A4
    [InlineData("A4-70x37", 3, 8)]  // 24 per A4, edge to edge
    public void Sheet_presets_fit_the_number_of_labels_printed_on_the_box(string id, int columns, int rows)
    {
        var options = new LabelOptions();
        LabelPreset.All.Single(p => p.Id == id).ApplyTo(options);
        Assert.Equal((columns, rows), options.Grid(LabelOptions.A4.Width, LabelOptions.A4.Height));
    }

    [Fact]
    public void Label_printer_prints_one_label_per_page()
    {
        var options = new LabelOptions();
        LabelPreset.All.Single(p => p.Id == "Roll-50x30").ApplyTo(options);
        Assert.False(options.Sheet);
        Assert.Equal((1, 1), options.Grid(LabelOptions.A4.Width, LabelOptions.A4.Height));
        Assert.Equal(50 * 96 / 25.4, options.WidthPx, 6);
    }

    [Fact]
    public void Custom_keeps_the_current_size()
    {
        var options = new LabelOptions { WidthMm = 45, HeightMm = 28 };
        LabelPreset.All.Single(p => p.Id == "Custom").ApplyTo(options);
        Assert.Equal(("Custom", 45d, 28d), (options.Preset, options.WidthMm, options.HeightMm));
    }
}

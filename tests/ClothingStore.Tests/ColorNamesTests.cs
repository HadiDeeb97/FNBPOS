using ClothingStore.Core.Entities;
using ClothingStore.Core.Text;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

/// <summary>Colour buttons and suggestions in the product editor.</summary>
public class ColorNamesTests
{
    private static readonly string[] Shop = ["Black", "Navy", "Olive", "Blush", "Blue", "Light blue", "أسود", "زيتي"];

    [Theory]
    [InlineData("bl", new[] { "Black", "Blush", "Blue" })]          // starting with it, most used first
    [InlineData("BLU", new[] { "Blush", "Blue", "Light blue" })]
    [InlineData("blue", new string[0])]                             // already a whole colour
    [InlineData("ue", new[] { "Blue", "Light blue" })]              // containing it, after none start with it
    [InlineData("اس", new[] { "أسود" })]                            // Arabic, whatever the alef
    [InlineData("", new string[0])]
    public void Suggests_the_shops_colours_while_typing(string typed, string[] expected) =>
        Assert.Equal(expected, ColorNames.Suggest(typed, Shop, max: 3));

    [Fact]
    public void Colours_already_in_the_list_are_not_suggested_again() =>
        Assert.Equal(["Blush", "Blue", "Light blue"], ColorNames.Suggest("bl", Shop, exclude: ["black"]));

    [Theory]
    [InlineData("black", "Black")]
    [InlineData("  NAVY ", "Navy")]
    [InlineData("اسود", "أسود")]
    [InlineData("Teal", "Teal")] // new to the shop: as typed
    public void A_colour_is_saved_as_the_shop_already_spells_it(string typed, string expected) =>
        Assert.Equal(expected, ColorNames.Canonical(typed, Shop));

    [Fact]
    public void A_typed_list_splits_on_english_and_arabic_commas_without_repeats() =>
        Assert.Equal(["Black", "Navy", "أسود", "زيتي"], ColorNames.Split("Black, Navy; black،navy\nأسود، زيتي"));

    [Theory]
    [InlineData("Black", "#111827")]
    [InlineData("black", "#111827")]
    [InlineData("أسود", "#111827")]
    [InlineData("Sky blue", "#7DD3FC")]
    [InlineData("Rami's special", null)]
    public void Well_known_colours_have_a_swatch(string name, string? hex) => Assert.Equal(hex, ColorNames.Swatch(name));

    [Fact]
    public void A_new_shop_starts_with_the_common_colours_in_its_language()
    {
        Assert.Equal(10, ColorNames.Common(arabic: false).Count);
        Assert.Equal("Black", ColorNames.Common(arabic: false)[0]);
        Assert.Equal("أسود", ColorNames.Common(arabic: true)[0]);
    }

    [Fact]
    public async Task The_shops_colours_come_most_used_first_in_their_usual_spelling()
    {
        await using var db = await TestDatabase.CreateAsync();
        var category = await db.Categories.SaveAsync(new Category { Name = "Tops" });
        ProductVariant V(string size, string color) => new() { Size = size, Color = color };
        await db.Products.SaveAsync(new Product
        {
            Name = "Tee", CategoryId = category.Id, Price = 10,
            Variants = [V("S", "Black"), V("M", "Black"), V("L", "black "), V("S", "Navy"), V("M", "Navy"), V("S", "Olive"), V("XL", "")],
        }, db.Admin.Id);

        var colors = await db.Products.GetColorsAsync();

        Assert.Equal(["Black", "Navy", "Olive"], colors);
    }
}

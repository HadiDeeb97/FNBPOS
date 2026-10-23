using ClothingStore.Core.Entities;
using ClothingStore.Core.Text;

namespace ClothingStore.Tests;

public class SmartSearchTests
{
    [Fact]
    public void Text_is_split_into_clean_words_and_phone_numbers_are_recognised()
    {
        Assert.Equal(["blue", "oxford", "m"], SmartSearch.Terms("  blue   oxford m blue "));
        Assert.Equal(["123"], SmartSearch.Terms("١٢٣"));                    // Arabic-Indic digits
        Assert.Equal(["محمد"], SmartSearch.Terms("مُحَمّـد"));              // vowel marks and tatweel dropped
        Assert.Equal("70123456", SmartSearch.PhoneDigits("70 123-456"));
        Assert.Equal("96170123456", SmartSearch.PhoneDigits("+961 70 123 456"));
        Assert.Null(SmartSearch.PhoneDigits("R2026"));
        Assert.Null(SmartSearch.PhoneDigits("123"));
    }

    [Fact]
    public void Patterns_escape_wildcards_and_let_spelling_variants_match()
    {
        Assert.Equal("%[5٥۵][0٠۰]\\%\\_[xX]%", SmartSearch.LikePattern("50%_x"));
        Assert.Contains("[اأإآٱ]", SmartSearch.LikePattern("أحمد"));
        Assert.Contains("[ةه]", SmartSearch.LikePattern("فاطمة"));
        Assert.Contains("[eèéêëEÈÉÊË]", SmartSearch.LikePattern("é"));
        Assert.Equal("%[oòóôöOÒÓÔÖ][xX][fF]%", SmartSearch.LikePattern("Oxf")); // case-sensitive databases too
    }

    [Fact]
    public void In_memory_matching_follows_the_same_rules()
    {
        Assert.True(SmartSearch.Matches("oxford blue", "Oxford shirt", "Blue"));
        Assert.False(SmartSearch.Matches("oxford red", "Oxford shirt", "Blue"));
        Assert.True(SmartSearch.Matches("احمد", "أحمد"));
        Assert.True(SmartSearch.Matches("فاطمه", "فاطمة"));
        Assert.True(SmartSearch.Matches("cafe", "Café"));
        Assert.True(SmartSearch.Matches("70 123 456", "Rana", "70-123456"));
        Assert.True(SmartSearch.Matches("", "anything"));
    }

    [Fact]
    public async Task Customers_are_found_by_arabic_spelling_variants_and_phone_in_any_format()
    {
        await using var db = await TestDatabase.CreateAsync();
        var ahmad = await db.Customers.SaveAsync(new Customer { FirstName = "أحمد", LastName = "الخطيب", Phone = "70-123 456" });
        await db.Customers.SaveAsync(new Customer { FirstName = "Rana", LastName = "Haddad", Phone = "03111222" });

        Assert.Equal(ahmad.Id, Assert.Single(await db.Customers.SearchAsync("احمد")).Id);
        Assert.Equal(ahmad.Id, Assert.Single(await db.Customers.SearchAsync("الخطيب احمد")).Id); // any order
        Assert.Equal(ahmad.Id, Assert.Single(await db.Customers.SearchAsync("70123456")).Id);
        Assert.Equal(ahmad.Id, Assert.Single(await db.Customers.SearchAsync("٧٠ ١٢٣ ٤٥٦")).Id);
        Assert.Single(await db.Customers.SearchAsync("haddad RANA"));
        Assert.Empty(await db.Customers.SearchAsync("rana khoury"));
    }

    [Fact]
    public async Task Register_search_matches_words_in_any_order_and_puts_an_exact_code_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var black = await db.Sales.CompleteSaleAsync(new Data.Services.CheckoutRequest
        {
            UserId = db.Cashier.Id, Lines = [new Data.Services.CheckoutLine(tee.Variants[0].Id, 1)],
            Payments = [new Data.Services.PaymentInput(Core.PaymentMethod.Cash, 22m)],
        });
        Assert.NotNull(black);

        Assert.Equal(3, (await db.Products.SearchVariantsAsync("black tee")).Count);
        Assert.Single(await db.Products.SearchVariantsAsync("tee black m"));
        var code = tee.Variants[2].Sku;
        Assert.Equal(tee.Variants[2].Id, (await db.Products.SearchVariantsAsync(code))[0].Id);
    }
}

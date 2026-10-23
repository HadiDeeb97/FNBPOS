using ClothingStore.Core;
using ClothingStore.Data.Services;

namespace ClothingStore.Tests;

public class SizePresetTests
{
    [Fact]
    public async Task Store_size_buttons_are_saved_tidied_shared_and_removed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var presets = new SizePresetService(db.Factory);
        Assert.Empty(await presets.GetAllAsync());

        var saved = await presets.AddAsync("  Abaya 52–60 ", "52,54 ; 56,58,\n60");
        Assert.Equal(new CustomSizePreset("Abaya 52–60", "52, 54, 56, 58, 60"), Assert.Single(saved));
        // Another till (another service instance) sees it.
        Assert.Single(await new SizePresetService(db.Factory).GetAllAsync());

        await Assert.ThrowsAsync<BusinessRuleException>(() => presets.AddAsync("abaya 52–60", "1, 2")); // same name
        await Assert.ThrowsAsync<BusinessRuleException>(() => presets.AddAsync("", "1, 2"));
        await Assert.ThrowsAsync<BusinessRuleException>(() => presets.AddAsync("Empty", " , ; "));
        await Assert.ThrowsAsync<BusinessRuleException>(() => presets.AddAsync(new string('x', SizePresetService.MaxNameLength + 1), "1"));

        await presets.AddAsync("Jeans 28–36", "28, 30, 32, 34, 36");
        Assert.Equal(["Abaya 52–60", "Jeans 28–36"], (await presets.GetAllAsync()).Select(p => p.Name));

        var left = await presets.RemoveAsync("ABAYA 52–60");
        Assert.Equal("Jeans 28–36", Assert.Single(left).Name);
        Assert.Single(await presets.RemoveAsync("not there"));
    }

    [Fact]
    public async Task There_is_a_limit_on_how_many_buttons_are_kept()
    {
        await using var db = await TestDatabase.CreateAsync();
        var presets = new SizePresetService(db.Factory);
        for (var i = 0; i < SizePresetService.MaxPresets; i++) await presets.AddAsync($"Set {i}", "S, M, L, XL");
        await Assert.ThrowsAsync<BusinessRuleException>(() => presets.AddAsync("One more", "S, M"));
        Assert.Equal(SizePresetService.MaxPresets, (await presets.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Tills_adding_buttons_at_the_same_moment_keep_them_all()
    {
        await using var db = await TestDatabase.CreateAsync();
        // Separate service instances, as on separate tills, all starting from an empty list (the first save inserts the row).
        await Task.WhenAll(Enumerable.Range(1, 3).Select(i => new SizePresetService(db.Factory).AddAsync($"Till {i}", "S, M, L")));
        Assert.Equal(["Till 1", "Till 2", "Till 3"], (await new SizePresetService(db.Factory).GetAllAsync()).Select(p => p.Name).Order());

        await new SizePresetService(db.Factory).AddAsync("عباية ٥٢–٦٠", "52, 54, 56, 58, 60"); // Arabic stored as is
        Assert.Contains(await new SizePresetService(db.Factory).GetAllAsync(), p => p.Name == "عباية ٥٢–٦٠");
    }
}

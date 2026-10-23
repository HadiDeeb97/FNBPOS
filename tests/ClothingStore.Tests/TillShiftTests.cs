using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

/// <summary>The cash drawer belongs to the till (PC): one open drawer per till, handed over or counted and closed.</summary>
public class TillShiftTests
{
    private const string Till1 = "AAAA-BBBB-CCCC-DDDD";
    private const string Till2 = "EEEE-FFFF-GGGG-HHHH";

    [Fact]
    public async Task The_next_cashier_sees_the_open_drawer_and_cannot_open_a_second_one_on_the_same_pc()
    {
        await using var db = await TestDatabase.CreateAsync();
        var drawer = await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m, 0, Till1, "Till 1");

        var forManager = await db.Shifts.GetTillShiftAsync(db.Manager.Id, Till1);
        Assert.True(forManager.IsOtherCashiers);
        Assert.Equal(drawer.Id, forManager.Shift!.Id);
        Assert.True((await db.Shifts.GetTillShiftAsync(db.Cashier.Id, Till1)).IsMine);
        Assert.Null((await db.Shifts.GetTillShiftAsync(db.Manager.Id, Till2)).Shift);

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.Shifts.OpenShiftAsync(db.Manager.Id, 50m, 0, Till1, "Till 1"));
        // Another PC has its own drawer.
        await db.Shifts.OpenShiftAsync(db.Manager.Id, 50m, 0, Till2, "Till 2");
    }

    [Fact]
    public async Task Continuing_a_drawer_records_the_hand_over_and_the_sales_go_into_the_same_shift()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tee = await db.CreateTeeAsync();
        var drawer = await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m, 0, Till1, "Till 1");
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Cashier.Id, ShiftId = drawer.Id, Lines = [new CheckoutLine(tee.Variants[0].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Cash, 22m)],
        });

        var taken = await db.Shifts.TakeOverAsync(drawer.Id, db.Manager.Id);
        Assert.Equal(db.Manager.Id, taken.CurrentUserId);
        Assert.True((await db.Shifts.GetTillShiftAsync(db.Manager.Id, Till1)).IsMine);
        Assert.True((await db.Shifts.GetTillShiftAsync(db.Cashier.Id, Till1)).IsOtherCashiers);
        await db.Sales.CompleteSaleAsync(new CheckoutRequest
        {
            UserId = db.Manager.Id, ShiftId = drawer.Id, Lines = [new CheckoutLine(tee.Variants[1].Id, 1)], Payments = [new PaymentInput(PaymentMethod.Cash, 22m)],
        });

        var closed = await db.Shifts.CloseShiftAsync(drawer.Id, 144m, null, 0m, db.Manager.Id);
        Assert.Equal((2, 144m, 0m), (closed.SalesCount, closed.ExpectedCash, closed.Variance));
        var handover = Assert.Single(closed.Handovers);
        Assert.Equal((db.Cashier.FullName, db.Manager.FullName), (handover.From, handover.To));
        Assert.Equal(db.Manager.FullName, closed.ClosedBy);
        Assert.Equal("Till 1", closed.Till);
    }

    [Fact]
    public async Task Counting_and_closing_the_previous_drawer_frees_the_pc_for_the_next_cashier()
    {
        await using var db = await TestDatabase.CreateAsync();
        var drawer = await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m, 0, Till1, "Till 1");
        var closed = await db.Shifts.CloseShiftAsync(drawer.Id, 95m, "5 short", 0m, db.Manager.Id);
        Assert.Equal(-5m, closed.Variance); // on the cashier's shift, closed by the manager

        var mine = await db.Shifts.OpenShiftAsync(db.Manager.Id, 95m, 0, Till1, "Till 1");
        Assert.True((await db.Shifts.GetTillShiftAsync(db.Manager.Id, Till1)).IsMine);
        Assert.Equal(mine.Id, (await db.Shifts.GetTillShiftAsync(db.Cashier.Id, Till1)).Shift!.Id);
    }

    [Fact]
    public async Task A_cashier_runs_one_drawer_at_a_time()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m, 0, Till1, "Till 1");
        var other = await db.Shifts.OpenShiftAsync(db.Manager.Id, 100m, 0, Till2, "Till 2");
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => db.Shifts.TakeOverAsync(other.Id, db.Cashier.Id));
        Assert.Contains("Till 1", ex.Message);
        Assert.Equal(2, (await db.Shifts.GetOpenShiftsAsync()).Count);
    }

    [Fact]
    public async Task A_drawer_opened_before_tills_were_tracked_is_attached_to_the_pc_its_cashier_signs_in_on()
    {
        await using var db = await TestDatabase.CreateAsync();
        var old = await db.Shifts.OpenShiftAsync(db.Cashier.Id, 100m); // no till: as before this version
        var till = await db.Shifts.GetTillShiftAsync(db.Cashier.Id, Till1, "Till 1");
        Assert.True(till.IsMine);
        Assert.Equal(old.Id, till.Shift!.Id);
        await using var ctx = await db.Factory.CreateDbContextAsync();
        Assert.Equal(Till1, (await ctx.Shifts.SingleAsync(s => s.Id == old.Id)).TillId);
    }
}

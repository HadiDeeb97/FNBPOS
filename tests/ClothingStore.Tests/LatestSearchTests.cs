using ClothingStore.Desktop.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClothingStore.Tests;

public class LatestSearchTests
{
    [Fact]
    public async Task A_slow_older_search_never_overwrites_a_newer_one()
    {
        var search = new LatestSearch(TimeSpan.Zero);
        var slow = new TaskCompletionSource<string>();
        var shown = new List<string>();

        var first = search.RunAsync(_ => slow.Task, shown.Add);              // "je" — slow query
        var second = search.RunAsync(_ => Task.FromResult("jeans"), shown.Add); // "jeans" — fast query
        slow.SetResult("je");                                                // the old query finishes last

        Assert.False(await first);
        Assert.True(await second);
        Assert.Equal(["jeans"], shown);
    }

    [Fact]
    public async Task Typing_quickly_only_runs_the_last_search()
    {
        var search = new LatestSearch(TimeSpan.FromMilliseconds(50));
        var queried = new List<string>();
        var shown = new List<string>();

        var runs = new[] { "j", "je", "jea", "jeans" }
            .Select(text => search.RunAsync(_ => { queried.Add(text); return Task.FromResult(text); }, shown.Add))
            .ToList();
        await Task.WhenAll(runs);

        Assert.Equal(["jeans"], queried);
        Assert.Equal(["jeans"], shown);
    }

    [Fact]
    public async Task Run_now_skips_the_typing_pause_and_cancel_discards_a_running_search()
    {
        var search = new LatestSearch(TimeSpan.FromHours(1));
        var shown = new List<string>();

        Assert.True(await search.RunNowAsync(_ => Task.FromResult("scan"), shown.Add));

        var pending = new TaskCompletionSource<string>();
        var running = search.RunNowAsync(_ => pending.Task, shown.Add);
        search.Cancel();
        pending.SetResult("late");

        Assert.False(await running);
        Assert.Equal(["scan"], shown);
    }

    [Fact]
    public async Task The_query_receives_a_token_that_is_cancelled_when_overtaken()
    {
        var search = new LatestSearch(TimeSpan.Zero);
        CancellationToken firstToken = default;
        var gate = new TaskCompletionSource<string>();

        var first = search.RunAsync(ct => { firstToken = ct; return gate.Task; }, _ => { });
        _ = search.RunAsync(_ => Task.FromResult("newer"), _ => { });

        Assert.True(firstToken.IsCancellationRequested);
        gate.SetResult("old");
        Assert.False(await first);
    }

    /// <summary>
    /// SQL Server reports a query cancelled mid-flight as a SqlException ("Operation cancelled by user"), not an
    /// OperationCanceledException. A search overtaken by typing must still end quietly instead of showing that error.
    /// </summary>
    [Fact]
    public async Task A_sql_query_cancelled_by_a_newer_search_is_not_an_error()
    {
        await using var db = await TestDatabase.CreateAsync();
        var search = new LatestSearch(TimeSpan.Zero);
        var started = new TaskCompletionSource();

        var slow = search.RunNowAsync(async ct =>
        {
            await using var ctx = await db.Factory.CreateDbContextAsync(ct);
            started.SetResult();
            await ctx.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:05'", ct);
            return "old";
        }, _ => { });
        await started.Task;
        await Task.Delay(300); // the query is now running on the server

        var newer = search.RunNowAsync(_ => Task.FromResult("new"), _ => { });

        Assert.False(await slow);
        Assert.True(await newer);
    }

    [Fact]
    public async Task A_real_failure_of_the_current_search_is_still_reported()
    {
        var search = new LatestSearch(TimeSpan.Zero);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            search.RunNowAsync<string>(_ => throw new InvalidOperationException("database down"), _ => { }));
    }

    /// <summary>A busy server (timeout, dropped connection) gets one quiet retry before anything is shown to the cashier.</summary>
    [Fact]
    public async Task A_search_that_times_out_once_is_retried_and_shown()
    {
        var search = new LatestSearch(TimeSpan.Zero);
        var attempts = 0;
        var shown = new List<string>();

        var ok = await search.RunNowAsync(_ => ++attempts == 1
            ? Task.FromException<string>(new TimeoutException("The wait operation timed out."))
            : Task.FromResult("jeans"), shown.Add);

        Assert.True(ok);
        Assert.Equal(2, attempts);
        Assert.Equal(["jeans"], shown);
    }

    [Fact]
    public async Task A_failure_that_is_not_the_database_being_busy_is_not_retried()
    {
        var search = new LatestSearch(TimeSpan.Zero);
        var attempts = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => search.RunNowAsync<string>(_ =>
        {
            attempts++;
            throw new ArgumentException("bad filter");
        }, _ => { }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Busy_database_errors_are_recognised_through_wrapping_exceptions()
    {
        Assert.True(DbErrors.IsTransient(new TimeoutException()));
        Assert.True(DbErrors.IsTransient(new InvalidOperationException("outer", new InvalidOperationException("Invalid operation. The connection is closed."))));
        Assert.False(DbErrors.IsTransient(new InvalidOperationException("Sequence contains no elements")));
        Assert.False(DbErrors.IsTransient(null));
    }
}

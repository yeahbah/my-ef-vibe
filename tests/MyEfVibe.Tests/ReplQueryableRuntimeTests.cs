using Microsoft.EntityFrameworkCore;

namespace MyEfVibe.Tests;

public sealed class ReplQueryableRuntimeTests
{
    [Fact]
    public void First_on_DbSet_does_not_throw_invalid_cast()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.Add(new FakeRewriterUser { Id = 42 });
        context.SaveChanges();

        var result = ReplQueryableRuntime.First(context.Users);

        Assert.IsType<FakeRewriterUser>(result);
        Assert.Equal(42, ((FakeRewriterUser)result).Id);
    }

    [Fact]
    public void Take_on_DbSet_returns_limited_queryable()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.AddRange(
            new FakeRewriterUser { Id = 1 },
            new FakeRewriterUser { Id = 2 });

        context.SaveChanges();

        var query = ReplQueryableRuntime.Take(context.Users, 1);

        Assert.Equal(1, ((IQueryable<FakeRewriterUser>)query).Count());
    }
}

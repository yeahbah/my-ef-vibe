using System.Linq.Expressions;
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
    public void ToArray_on_Take_returns_materialized_rows()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.AddRange(
            new FakeRewriterUser { Id = 1 },
            new FakeRewriterUser { Id = 2 },
            new FakeRewriterUser { Id = 3 });

        context.SaveChanges();

        var query = ReplQueryableRuntime.Take(context.Users, 2);
        var rows = (FakeRewriterUser[])ReplQueryableRuntime.ToArray(query)!;

        Assert.Equal(2, rows.Length);
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

    [Fact]
    public void Where_generic_on_DbSet_returns_translatable_queryable()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.AddRange(
            new FakeRewriterUser { Id = 1 },
            new FakeRewriterUser { Id = 2 });

        context.SaveChanges();

        Expression<Func<FakeRewriterUser, bool>> predicate = user => user.Id == 1;

        var query = ReplQueryableRuntime.Where<FakeRewriterUser>(context.Users, predicate);

        Assert.IsAssignableFrom<IQueryable<FakeRewriterUser>>(query);
        Assert.Single((IQueryable<FakeRewriterUser>)query);
    }

    [Fact]
    public void FirstOrDefault_generic_with_predicate_returns_entity()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.Add(new FakeRewriterUser { Id = 99 });
        context.SaveChanges();

        Expression<Func<FakeRewriterUser, bool>> predicate = user => user.Id == 99;

        var entity = ReplQueryableRuntime.FirstOrDefault<FakeRewriterUser>(context.Users, predicate);

        Assert.IsType<FakeRewriterUser>(entity);
        Assert.Equal(99, ((FakeRewriterUser)entity!).Id);
    }

    [Fact]
    public void Select_generic_projects_property()
    {
        using var context = new FakeGuidNoteDbContext(
            new DbContextOptionsBuilder<FakeGuidNoteDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.Add(new FakeGuidUser { Id = Guid.NewGuid(), Username = "ada" });
        context.SaveChanges();

        Expression<Func<FakeGuidUser, string>> selector = user => user.Username;

        var query = ReplQueryableRuntime.Select<FakeGuidUser, string>(context.Users, selector);

        Assert.Equal(["ada"], ((IQueryable<string>)query).ToArray());
    }
}

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
    public void OrderBy_after_Select_orders_projected_values()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.AddRange(
            new FakeRewriterUser { Id = 1, Name = "zoe" },
            new FakeRewriterUser { Id = 2, Name = "ada" });

        context.SaveChanges();

        var projected = ReplQueryableRuntime.Select<FakeRewriterUser, string>(
            context.Users,
            user => user.Name);
        var ordered = (IQueryable<string>)ReplQueryableRuntime.OrderBy<string, string>(projected, name => name);

        Assert.Equal(["ada", "zoe"], ordered.ToArray());
    }

    [Fact]
    public void Select_after_Take_projects_with_expression_tree()
    {
        using var context = new FakeRewriterDbContext(
            new DbContextOptionsBuilder<FakeRewriterDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        context.Users.AddRange(
            new FakeRewriterUser { Id = 1, Name = "ada" },
            new FakeRewriterUser { Id = 2, Name = "bob" });

        context.SaveChanges();

        var taken = ReplQueryableRuntime.Take(context.Users, 10);
        Expression selector = (Expression<Func<FakeRewriterUser, string>>)(user => user.Name);
        var projected = (IQueryable<string>)ReplQueryableRuntime.Select(taken, selector);

        Assert.Equal(["ada", "bob"], projected.OrderBy(name => name).ToArray());
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

    [Fact]
    public void Count_uses_query_provider_execute_instead_of_client_enumeration()
    {
        var query = new ExecuteOnlyQueryable<FakeRewriterUser>(expectedResult: 7);

        var count = ReplQueryableRuntime.Count(query);

        Assert.Equal(7, count);
        Assert.Equal(1, query.ExecuteCount);
        Assert.Equal(0, query.EnumerationCount);
    }

    [Fact]
    public void Count_with_predicate_uses_query_provider_execute_instead_of_client_enumeration()
    {
        var query = new ExecuteOnlyQueryable<FakeRewriterUser>(expectedResult: 3);
        Expression<Func<FakeRewriterUser, bool>> predicate = user => user.Id == 1;

        var count = ReplQueryableRuntime.Count<FakeRewriterUser>(query, predicate);

        Assert.Equal(3, count);
        Assert.Equal(1, query.ExecuteCount);
        Assert.Equal(0, query.EnumerationCount);
    }

    private sealed class ExecuteOnlyQueryable<T> : IQueryable<T>, IQueryProvider
    {
        private readonly int _expectedResult;

        internal ExecuteOnlyQueryable(int expectedResult)
        {
            _expectedResult = expectedResult;
            Expression = Expression.Constant(this);
            Provider = this;
        }

        internal int EnumerationCount { get; private set; }

        internal int ExecuteCount { get; private set; }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("Client-side enumeration should not be used.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public IQueryable CreateQuery(Expression expression) => new ExecuteOnlyQueryable<T>(_expectedResult);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new ExecuteOnlyQueryable<TElement>(_expectedResult);

        public object? Execute(Expression expression)
        {
            ExecuteCount++;
            return _expectedResult;
        }

        public TResult Execute<TResult>(Expression expression)
        {
            ExecuteCount++;
            return (TResult)(object)_expectedResult;
        }
    }
}

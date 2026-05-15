using System.Linq.Expressions;
using AgentBlazor.Core.Data;
using AgentBlazor.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AgentBlazor.EntityFrameworkCore;

public static class AgentBlazorEntityFrameworkCoreBuilderExtensions
{
    public static AgentBlazorBuilder AddEntitySchema<TContext>(
        this AgentBlazorBuilder builder,
        string name,
        Action<EfDataSchemaBuilder<TContext>> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var schemaBuilder = new EfDataSchemaBuilder<TContext>(name);
        configure(schemaBuilder);

        return builder.AddDataSchema(serviceProvider =>
            schemaBuilder.Build(serviceProvider));
    }
}

public sealed class EfDataSchemaBuilder<TContext>
    where TContext : DbContext
{
    private readonly List<IEfEntitySchemaBuilder<TContext>> _entities = [];

    internal EfDataSchemaBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public string? Description { get; private set; }

    public EfDataSchemaBuilder<TContext> WithDescription(string description)
    {
        Description = description;
        return this;
    }

    public EfDataSchemaBuilder<TContext> Entity<TEntity>(
        string name,
        Action<EfEntitySchemaBuilder<TContext, TEntity>> configure)
        where TEntity : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var entityBuilder = new EfEntitySchemaBuilder<TContext, TEntity>(name);
        configure(entityBuilder);
        _entities.Add(entityBuilder);
        return this;
    }

    internal AgentDataSchemaSet Build(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var context = CreateContext(serviceProvider);
        return new AgentDataSchemaSet
        {
            Name = Name,
            Description = Description,
            Entities = _entities.Select(entity => entity.Build(context.Model)).ToArray()
        };
    }

    private static TContext CreateContext(IServiceProvider serviceProvider)
    {
        var factory = serviceProvider.GetService<IDbContextFactory<TContext>>();
        if (factory is not null)
        {
            return factory.CreateDbContext();
        }

        throw new InvalidOperationException(
            $"AgentBlazor.EntityFrameworkCore requires IDbContextFactory<{typeof(TContext).Name}>. " +
            $"Register it with services.AddDbContextFactory<{typeof(TContext).Name}>(...).");
    }
}

public sealed class EfEntitySchemaBuilder<TContext, TEntity> : IEfEntitySchemaBuilder<TContext>
    where TContext : DbContext
    where TEntity : class
{
    private readonly List<EfEntityPropertySchemaBuilder<TEntity>> _properties = [];

    internal EfEntitySchemaBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    public string? Description { get; private set; }

    public EfEntitySchemaBuilder<TContext, TEntity> WithDescription(string description)
    {
        Description = description;
        return this;
    }

    public EfEntitySchemaBuilder<TContext, TEntity> Property<TProperty>(
        Expression<Func<TEntity, TProperty>> propertyExpression,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var propertyName = GetPropertyName(propertyExpression);
        _properties.Add(new EfEntityPropertySchemaBuilder<TEntity>(
            propertyName,
            typeof(TProperty),
            description));
        return this;
    }

    AgentEntitySchema IEfEntitySchemaBuilder<TContext>.Build(IModel model)
    {
        var entityType = model.FindEntityType(typeof(TEntity));
        if (entityType is null)
        {
            throw new InvalidOperationException(
                $"Entity type '{typeof(TEntity).FullName}' is not part of DbContext '{typeof(TContext).FullName}'.");
        }

        return new AgentEntitySchema
        {
            Name = Name,
            ClrTypeName = typeof(TEntity).FullName,
            Description = Description,
            Properties = _properties.Select(property => property.Build(entityType)).ToArray()
        };
    }

    private static string GetPropertyName<TProperty>(Expression<Func<TEntity, TProperty>> expression)
    {
        var body = expression.Body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert
            ? unary.Operand
            : expression.Body;

        if (body is MemberExpression member)
        {
            return member.Member.Name;
        }

        throw new ArgumentException(
            "Entity schema properties must be simple member access expressions such as x => x.Id.",
            nameof(expression));
    }
}

internal interface IEfEntitySchemaBuilder<TContext>
    where TContext : DbContext
{
    AgentEntitySchema Build(IModel model);
}

internal sealed class EfEntityPropertySchemaBuilder<TEntity>(
    string propertyName,
    Type declaredType,
    string? description)
    where TEntity : class
{
    public AgentEntityPropertySchema Build(IEntityType entityType)
    {
        var property = entityType.FindProperty(propertyName);
        if (property is null)
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is not mapped on entity type '{typeof(TEntity).FullName}'.");
        }

        return new AgentEntityPropertySchema
        {
            Name = propertyName,
            Type = GetTypeName(property.ClrType ?? declaredType),
            IsNullable = property.IsNullable,
            IsKey = property.IsPrimaryKey(),
            Description = description
        };
    }

    private static string GetTypeName(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return "string";
        }

        if (target == typeof(int))
        {
            return "integer";
        }

        if (target == typeof(long))
        {
            return "long";
        }

        if (target == typeof(decimal))
        {
            return "decimal";
        }

        if (target == typeof(double) || target == typeof(float))
        {
            return "number";
        }

        if (target == typeof(bool))
        {
            return "boolean";
        }

        if (target == typeof(DateTime) || target == typeof(DateTimeOffset))
        {
            return "datetime";
        }

        if (target == typeof(DateOnly))
        {
            return "date";
        }

        if (target.IsEnum)
        {
            return $"enum:{target.Name}";
        }

        return target.Name;
    }
}

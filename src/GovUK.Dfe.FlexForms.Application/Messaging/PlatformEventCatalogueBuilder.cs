using GovUK.Dfe.CoreLibs.Contracts.ExternalApplications.Models.Response;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Entities.Topics;
using GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events;
using System.Reflection;

namespace GovUK.Dfe.FlexForms.Application.Messaging;

/// <summary>
/// Discovers CoreLibs Messaging.Contracts events and builds catalogue metadata (topic + CLR property schema).
/// </summary>
public static class PlatformEventCatalogueBuilder
{
    private static readonly Lazy<GetEventCatalogueResponse> Cached =
        new(BuildCore, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly IReadOnlyDictionary<string, string> TopicOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(ScanRequestedEvent)] = TopicNames.ScanRequests
        };

    private static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(TransferApplicationSubmittedEvent)] =
                "Published when a transfer application is submitted.",
            [nameof(ScanRequestedEvent)] =
                "Published when a file is uploaded and queued for virus scanning.",
            [nameof(ScanResultEvent)] =
                "Consumed when the file scanner returns a scan result."
        };

    public static GetEventCatalogueResponse Build() => Cached.Value;

    private static GetEventCatalogueResponse BuildCore()
    {
        var assembly = typeof(TransferApplicationSubmittedEvent).Assembly;
        var eventsNamespace = typeof(TransferApplicationSubmittedEvent).Namespace
            ?? "GovUK.Dfe.CoreLibs.Messaging.Contracts.Messages.Events";

        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "1.0";

        var topicByName = typeof(TopicNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToDictionary(
                f => f.Name,
                f => (string)f.GetRawConstantValue()!,
                StringComparer.OrdinalIgnoreCase);

        var items = new List<EventCatalogueItemDto>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.Namespace is null
                || !string.Equals(type.Namespace, eventsNamespace, StringComparison.Ordinal)
                || type.IsAbstract
                || type.IsInterface
                || (!type.IsClass && !type.IsValueType)
                || !type.Name.EndsWith("Event", StringComparison.Ordinal))
            {
                continue;
            }

            Descriptions.TryGetValue(type.Name, out var description);
            var xmlSummary = type.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description;

            items.Add(new EventCatalogueItemDto
            {
                EventTypeName = type.Name,
                TopicName = ResolveTopicName(type.Name, topicByName),
                ClrTypeName = type.FullName ?? type.Name,
                Description = description ?? xmlSummary,
                Version = version,
                Kind = "Typed",
                Properties = BuildProperties(type, depth: 0, visited: new HashSet<Type>())
            });
        }

        return new GetEventCatalogueResponse
        {
            Events = items.OrderBy(e => e.EventTypeName, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static string? ResolveTopicName(
        string eventTypeName,
        IReadOnlyDictionary<string, string> topicByName)
    {
        if (TopicOverrides.TryGetValue(eventTypeName, out var overridden))
            return overridden;

        var withoutSuffix = eventTypeName.EndsWith("Event", StringComparison.Ordinal)
            ? eventTypeName[..^"Event".Length]
            : eventTypeName;

        if (topicByName.TryGetValue(withoutSuffix, out var exact))
            return exact;

        if (topicByName.TryGetValue(withoutSuffix + "s", out var plural))
            return plural;

        return null;
    }

    private static IReadOnlyList<EventCataloguePropertyDto> BuildProperties(
        Type type,
        int depth,
        HashSet<Type> visited)
    {
        if (depth > 3 || !visited.Add(type))
            return [];

        var props = new List<EventCataloguePropertyDto>();
        var ctor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        IEnumerable<(string Name, Type Type)> members;
        if (ctor is { } && ctor.GetParameters().Length > 0)
        {
            members = ctor.GetParameters().Select(p => (p.Name!, p.ParameterType));
        }
        else
        {
            members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (p.Name, p.PropertyType));
        }

        foreach (var (name, memberType) in members)
        {
            var (clrName, nullable, elementOrUnderlying) = DescribeType(memberType);
            IReadOnlyList<EventCataloguePropertyDto>? nested = null;

            if (ShouldExpand(elementOrUnderlying))
            {
                nested = BuildProperties(elementOrUnderlying, depth + 1, visited);
            }

            props.Add(new EventCataloguePropertyDto
            {
                Name = name,
                ClrType = clrName,
                IsNullable = nullable,
                Properties = nested is { Count: > 0 } ? nested : null
            });
        }

        visited.Remove(type);
        return props;
    }

    private static bool ShouldExpand(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(object))
        {
            return false;
        }

        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            return false;

        return type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum);
    }

    private static (string ClrName, bool IsNullable, Type ElementOrUnderlying) DescribeType(Type type)
    {
        var nullable = false;
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
        {
            nullable = true;
            type = underlying;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            nullable = true;
            type = type.GetGenericArguments()[0];
        }

        // Reference types are nullable in C# nullable context; treat class as nullable-capable.
        if (type.IsClass && type != typeof(string))
            nullable = true;

        if (type.IsArray)
        {
            var elem = type.GetElementType()!;
            return ($"{GetSimpleName(elem)}[]", true, elem);
        }

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>)
                || def == typeof(IEnumerable<>) || def == typeof(ICollection<>))
            {
                return ($"List<{GetSimpleName(args[0])}>", true, args[0]);
            }

            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                return ($"Dictionary<{GetSimpleName(args[0])},{GetSimpleName(args[1])}>", true, args[1]);
            }
        }

        return (GetSimpleName(type), nullable || !type.IsValueType, type);
    }

    private static string GetSimpleName(Type type) => type.Name;
}

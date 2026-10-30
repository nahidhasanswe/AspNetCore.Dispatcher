using System;
using System.Collections.Generic;

namespace AspNetCore.Dispatcher.Internal;

/// <summary>
/// Describes an instance so exception handlers and actions can be ordered by how closely
/// they sit to the request being handled.
/// </summary>
internal class ObjectDetails : IComparer<ObjectDetails>
{
    public string Name { get; }

    public string? Location { get; }

    public object Value { get; }

    public Type Type { get; }

    public bool IsOverridden { get; set; }

    public ObjectDetails(object value)
    {
        Value = value;
        Type = value.GetType();

        var type = Type;
        Name = type.Name;
        Location = type.Namespace;
    }

    public int Compare(ObjectDetails? x, ObjectDetails? y)
    {
        if (x == null)
        {
            return 1;
        }

        if (y == null)
        {
            return -1;
        }

        return CompareByAssignability(x, y) ?? CompareByLocation(x, y);
    }

    /// <summary>
    /// The more derived handler wins, so a handler for a specific exception runs before one
    /// written against a base exception type.
    /// </summary>
    private static int? CompareByAssignability(ObjectDetails x, ObjectDetails y)
    {
        if (x.Type == y.Type)
        {
            return null;
        }

        if (x.Type.IsAssignableFrom(y.Type))
        {
            return 1;
        }

        if (y.Type.IsAssignableFrom(x.Type))
        {
            return -1;
        }

        return null;
    }

    /// <summary>
    /// Handlers living closer to the request in the namespace hierarchy win, which keeps a
    /// feature-local handler ahead of a shared catch-all handler.
    /// </summary>
    private int CompareByLocation(ObjectDetails x, ObjectDetails y)
    {
        var xDistance = GetNamespaceDistance(x.Location);
        var yDistance = GetNamespaceDistance(y.Location);

        if (xDistance != yDistance)
        {
            return yDistance.CompareTo(xDistance);
        }

        return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
    }

    private int GetNamespaceDistance(string? candidate)
    {
        if (Location is null || candidate is null)
        {
            return 0;
        }

        var requestParts = Location.Split('.');
        var candidateParts = candidate.Split('.');
        var shared = 0;

        while (shared < requestParts.Length
               && shared < candidateParts.Length
               && string.Equals(requestParts[shared], candidateParts[shared], StringComparison.Ordinal))
        {
            shared++;
        }

        return shared;
    }
}

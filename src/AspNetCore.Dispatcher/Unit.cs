using System;
using System.Threading.Tasks;

namespace AspNetCore.Dispatcher;

/// <summary>
/// Represents a void type, since <see cref="System.Void" /> is not a valid return type in C#.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>, IComparable
{
    private static readonly Unit _value = new Unit();

    /// <summary>
    /// Default and only value of the <see cref="Unit"/> type.
    /// </summary>
    public static ref readonly Unit Value => ref _value;

    /// <summary>
    /// Task from a <see cref="Unit"/> type.
    /// </summary>
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(_value);

    /// <summary>
    /// Compares the current object with another object of the same type. Always 0.
    /// </summary>
    public int CompareTo(Unit other) => 0;

    int IComparable.CompareTo(object? obj) => 0;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <summary>
    /// Determines whether the specified <see cref="Unit"/> is equal to the current <see cref="Unit"/>. Always true.
    /// </summary>
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <summary>Determines whether two <see cref="Unit"/> values are equal. Always true.</summary>
    public static bool operator ==(Unit first, Unit second) => true;

    /// <summary>Determines whether two <see cref="Unit"/> values are unequal. Always false.</summary>
    public static bool operator !=(Unit first, Unit second) => false;

    /// <inheritdoc />
    public override string ToString() => "()";
}

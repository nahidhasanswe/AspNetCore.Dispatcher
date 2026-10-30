using System;
using System.Threading.Tasks;
using Xunit;

namespace AspNetCore.Dispatcher.Tests;

public class UnitTests
{
    [Fact]
    public void All_units_are_equal()
    {
        var first = Unit.Value;
        var second = new Unit();

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.Equal(0, first.CompareTo(second));
        Assert.Equal(0, ((IComparable)first).CompareTo(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Has_a_readable_string_representation()
    {
        Assert.Equal("()", Unit.Value.ToString());
    }

    [Fact]
    public async Task Exposes_a_completed_task()
    {
        Assert.Equal(Unit.Value, await Unit.Task);
    }

    [Fact]
    public void A_unit_does_not_equal_another_type()
    {
        Assert.False(Unit.Value.Equals("not a unit"));
    }
}

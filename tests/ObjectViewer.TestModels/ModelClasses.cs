using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using ObjectViewer.Core.Members;

namespace ObjectViewer.TestModels;

public interface IAnimal
{
    string Name { get; }
}

public sealed class Dog : IAnimal
{
    public string Name { get; set; } = "Lucky";

    public int Age { get; set; } = 3;

    public Owner? Owner { get; set; }

    [DisplayName("Bark Volume")]
    public BarkVolume BarkVolume { get; set; } = BarkVolume.Loud;
}

public sealed class Owner
{
    public string Name { get; set; } = "Alice";

    public Dog? Pet { get; set; }
}

public enum BarkVolume
{
    Silent = 0,
    Quiet = 1,
    Loud = 2,
    VeryLoud = 3,
}

public sealed class Order
{
    public long OrderId { get; set; } = 20261112001;

    public Customer? Customer { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    public Order? Parent { get; set; }

    public DateTime CreatedAt { get; set; } = new(2026, 11, 12, 10, 30, 0, DateTimeKind.Local);

    public Guid TrackingId { get; set; } = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    public decimal Total { get; set; } = 1234.56m;
}

public sealed class Customer
{
    public string Name { get; set; } = "ACME";

    public Address? Address { get; set; }
}

public sealed class Address
{
    public string Street { get; set; } = "No.1 Test Road";

    public string City { get; set; } = "Shanghai";
}

public sealed class OrderItem
{
    public int ItemId { get; set; }

    public string SerialNumber { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Quantity { get; set; } = 1;
}

public sealed class Department
{
    public string Name { get; set; } = "R&D";

    public List<Employee> Employees { get; set; } = [];
}

public sealed class Employee
{
    public string Name { get; set; } = "Bob";

    public Department? Department { get; set; }
}

public sealed class Device
{
    public string OkProp { get; set; } = "ok";

    public string ThrowingProp
    {
        get => throw new InvalidOperationException("Device is offline");
    }

    public string LongStringProp { get; set; } = new('x', 5_000);

    public int? NullableProp { get; set; }
}

public sealed class Credentials
{
    public string UserName { get; set; } = "alice";

    public string Password { get; set; } = "P@ssw0rd!";

    public string ApiToken { get; set; } = "eyJhbGciOiJIUzI1NiJ9";

    public string ConnectionString { get; set; } = "Server=db;User=sa;Password=secret";

    [ObjectViewerSensitive]
    public string CustomSecret { get; set; } = "masked-by-attribute";
}

public sealed class LinkedNode
{
    public int Value { get; set; }

    public LinkedNode? Next { get; set; }
}

/// <summary>Overrides Equals but instances stay distinct by reference (PRD §16.3).</summary>
public sealed class EqualsOverridden
{
    public string Key { get; set; } = "same";

    public override bool Equals(object? obj) => obj is EqualsOverridden other && other.Key == Key;

    public override int GetHashCode() => Key.GetHashCode();
}

public sealed class MetadataDemo
{
    [Browsable(false)]
    public string Hidden { get; set; } = "should not appear";

    [Display(Name = "Shown Name", Order = 1)]
    public string RawName { get; set; } = "raw";

    public string this[int index] => "indexer";

    public static string StaticProp { get; set; } = "static";

    private string PrivateField = "private";

    public string ExposesPrivateField => PrivateField;

    public string NormalField = "normal";
}

public sealed class EmptyObject
{
}

/// <summary>Edit matrix model: one member per convertible kind plus policy edge cases (PRD §10.13).</summary>
public sealed class EditableMachine
{
    public int Speed { get; set; } = 1200;

    public double Temperature { get; set; } = 36.5;

    public decimal Price { get; set; } = 99.95m;

    public bool Enabled { get; set; } = true;

    public char Grade { get; set; } = 'A';

    public string Label { get; set; } = "machine-01";

    public BarkVolume Volume { get; set; } = BarkVolume.Quiet;

    public int? Timeout { get; set; } = 30;

    public DateTime StartedAt { get; set; } = new(2026, 11, 12, 10, 30, 0);

    public Guid BatchId { get; set; } = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    public MachinePoint Home { get; set; } = new(10, 20);

    public string ReadOnlyLabel => Label;

    public string ArchiveTag { get; private set; } = "archived";

    public string ApiKey { get; set; } = "sk-12345";

    [ObjectViewerEditable]
    public int Whitelisted { get; set; } = 7;

    /// <summary>Setters that throw must stay inspectable (FR-124 demo) but can never round-trip JSON.</summary>
    [JsonIgnore]
    public string ThrowingSetter
    {
        get => "x";
        set => throw new InvalidOperationException("Setter rejected: machine locked");
    }

    private int _counter = 5;

    public int ExposedCounter => _counter;
}

/// <summary>Custom struct with a string TypeConverter (FR-123 TypeConverter path).</summary>
[TypeConverter(typeof(MachinePointConverter))]
public struct MachinePoint(int x, int y)
{
    public int X { get; set; } = x;

    public int Y { get; set; } = y;

    public override string ToString() => $"{X}, {Y}";
}

public sealed class MachinePointConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            var parts = text.Split(',');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out var x)
                && int.TryParse(parts[1].Trim(), out var y))
                return new MachinePoint(x, y);
            throw new FormatException($"\"{text}\" is not a valid MachinePoint (expected \"X, Y\").");
        }
        return base.ConvertFrom(context, culture, value)!;
    }
}

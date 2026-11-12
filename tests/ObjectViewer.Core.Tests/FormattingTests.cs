using System.Globalization;
using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class FormattingTests
{
    private static readonly FormatterRegistry Registry = new(ObjectViewerDefaults.CreateDefaultFormatters());

    private static ValueFormattingContext Context(ValueDisplayFormat format = ValueDisplayFormat.Default, int maxLength = 2_000)
        => new(CultureInfo.InvariantCulture, format, maxLength);

    [Fact]
    public void String_IsQuotedAndEscaped()
    {
        var display = Registry.Format(typeof(string), "a\"b\nc", Context());
        Assert.Equal("\"a\\\"b\\nc\"", display.Text);
        Assert.Equal("string", display.TypeText);
    }

    [Fact]
    public void String_LongerThanLimit_IsTruncated()
    {
        var display = Registry.Format(typeof(string), new string('x', 3_000), Context(maxLength: 100));
        Assert.True(display.IsTruncated);
        Assert.EndsWith("…\"", display.Text);
        Assert.Equal(103, display.Text.Length); // quote + 100 chars + ellipsis + closing quote
        Assert.Contains("Length = 3000", display.ToolTip);
    }

    [Fact]
    public void Enum_ShowsNameAndNumericValue()
    {
        var display = Registry.Format(typeof(BarkVolume), BarkVolume.VeryLoud, Context());
        Assert.Equal("VeryLoud (3)", display.Text);
    }

    [Fact]
    public void Integer_SupportsHexAndBinaryModes()
    {
        Assert.Equal("42", Registry.Format(typeof(int), 42, Context()).Text);
        Assert.Equal("0x2a", Registry.Format(typeof(int), 42, Context(ValueDisplayFormat.Hex)).Text);
        Assert.Equal("0b101010", Registry.Format(typeof(int), 42, Context(ValueDisplayFormat.Binary)).Text);
    }

    [Fact]
    public void ByteArray_ShowsLengthAndHexPrefix()
    {
        var display = Registry.Format(typeof(byte[]), new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, Context());
        Assert.StartsWith("byte[4] 4D 5A 90 00", display.Text);
    }

    [Fact]
    public void DateTime_IncludesKind()
    {
        var utc = new DateTime(2026, 11, 12, 10, 30, 0, DateTimeKind.Utc);
        var display = Registry.Format(typeof(DateTime), utc, Context());
        Assert.EndsWith("(Utc)", display.Text);
        Assert.StartsWith("2026-11-12T10:30:00", display.Text);
    }

    [Fact]
    public void Decimal_PreservesPrecision()
    {
        var display = Registry.Format(typeof(decimal), 1234.56m, Context());
        Assert.Equal("1234.56", display.Text);
    }

    [Fact]
    public void UnknownObject_ShowsTypeNameWithoutToString()
    {
        var display = Registry.Format(typeof(Order), new Order(), Context());
        Assert.Equal("{Order}", display.Text);
    }

    [Fact]
    public void FailingFormatter_FallsBackGracefully()
    {
        var registry = new FormatterRegistry(new IObjectValueFormatter[]
        {
            new ThrowingFormatter(),
            new NumericValueFormatter(),
            new DefaultValueFormatter(),
        });
        var display = registry.Format(typeof(int), 7, Context());
        Assert.Equal("7", display.Text);
    }

    private sealed class ThrowingFormatter : IObjectValueFormatter
    {
        public int Priority => 500;
        public string Name => "Throwing";

        public bool CanFormat(Type? declaredType, Type? runtimeType, object? value)
            => true;

        public ValueDisplay Format(Type? declaredType, object? value, ValueFormattingContext context)
            => throw new InvalidOperationException("formatter boom");
    }
}

using System.Globalization;
using ObjectViewer.Core.Editing;
using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class EditTests
{
    private static InspectionOptions Editable(
        EditPolicyMode policy = EditPolicyMode.PublicSetters,
        Func<InspectionOptions, InspectionOptions>? configure = null)
    {
        var options = Viewer.Invariant(o => o with { EditPolicy = policy });
        return configure?.Invoke(options) ?? options;
    }

    // ---------- Policy gating (FR-120/121) ----------

    [Fact]
    public async Task DefaultPolicy_IsReadOnly_NothingEditable()
    {
        await using var session = Viewer.Start(new EditableMachine());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        Assert.All(root.Children, child => Assert.False(child.CanEdit));
    }

    [Fact]
    public async Task PublicSetters_OnlyPublicSettersEditable()
    {
        await using var session = Viewer.Start(new EditableMachine(), options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        Assert.True(Viewer.Child(root, "Speed").CanEdit);
        Assert.True(Viewer.Child(root, "Timeout").CanEdit);
        Assert.True(Viewer.Child(root, "Volume").CanEdit);
        Assert.True(Viewer.Child(root, "Home").CanEdit);

        Assert.False(Viewer.Child(root, "ReadOnlyLabel").CanEdit);      // getter-only
        Assert.False(Viewer.Child(root, "ArchiveTag").CanEdit);         // private setter
        Assert.False(Viewer.Child(root, "ApiKey").CanEdit);             // sensitive
        Assert.True(Viewer.Child(root, "ThrowingSetter").CanEdit);      // public setter that throws
    }

    [Fact]
    public async Task ExplicitlyAllowed_OnlyAnnotatedMembersEditable()
    {
        await using var session = Viewer.Start(
            new EditableMachine(), options: Editable(EditPolicyMode.ExplicitlyAllowedMembers));
        var root = session.Root!;
        await Viewer.Expand(session, root);

        Assert.True(Viewer.Child(root, "Whitelisted").CanEdit);
        Assert.False(Viewer.Child(root, "Speed").CanEdit);
        Assert.False(Viewer.Child(root, "ArchiveTag").CanEdit);
    }

    [Fact]
    public async Task FullTrust_AllowsPrivateSetters()
    {
        await using var session = Viewer.Start(
            new EditableMachine(), options: Editable(EditPolicyMode.FullTrust));
        var root = session.Root!;
        await Viewer.Expand(session, root);

        Assert.True(Viewer.Child(root, "ArchiveTag").CanEdit);      // private setter, FullTrust
        Assert.True(Viewer.Child(root, "Speed").CanEdit);
        Assert.False(Viewer.Child(root, "ApiKey").CanEdit);         // sensitive is never editable
    }

    [Fact]
    public async Task FullTrust_WithNonPublicMembers_PrivateFieldEditable()
    {
        await using var session = Viewer.Start(
            new EditableMachine(),
            options: Editable(EditPolicyMode.FullTrust, o => o with { ShowNonPublicMembers = true }));
        var root = session.Root!;
        await Viewer.Expand(session, root);

        Assert.True(Viewer.Child(root, "_counter").CanEdit);
    }

    // ---------- Write path (FR-122/124/125) ----------

    [Fact]
    public async Task WriteAsync_Int_AppliesAndReReads()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);
        var speed = Viewer.Child(root, "Speed");

        var result = await session.Explorer.WriteAsync(speed, "2400", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2400, machine.Speed);
        Assert.True(speed.HasChanged);
        Assert.Contains("2400", speed.Display.Text);
    }

    [Fact]
    public async Task WriteAsync_ReadOnlyPolicy_RejectsAndLeavesObjectUntouched()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine);
        var root = session.Root!;
        await Viewer.Expand(session, root);
        var speed = Viewer.Child(root, "Speed");

        var result = await session.Explorer.WriteAsync(speed, "2400", CancellationToken.None);

        Assert.Equal(NodeEditOutcome.NotEditable, result.Outcome);
        Assert.Equal(1200, machine.Speed);
    }

    [Fact]
    public async Task WriteAsync_ConversionFailed_ValueUntouched()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);
        var speed = Viewer.Child(root, "Speed");

        var result = await session.Explorer.WriteAsync(speed, "abc", CancellationToken.None);

        Assert.Equal(NodeEditOutcome.ConversionFailed, result.Outcome);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal(1200, machine.Speed);
    }

    [Fact]
    public async Task WriteAsync_SetterThrows_ErrorIsolatedOnNodeOnly()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);
        var throwing = Viewer.Child(root, "ThrowingSetter");

        var result = await session.Explorer.WriteAsync(throwing, "value", CancellationToken.None);

        Assert.Equal(NodeEditOutcome.SetterFailed, result.Outcome);
        Assert.Contains("machine locked", result.Error);
        Assert.NotNull(throwing.Error);
        Assert.Equal("EditFailed", throwing.Error.ErrorCode);

        // The viewer itself must stay fully usable after the failure (FR-124).
        var refresh = await session.Explorer.RefreshAsync(root, RefreshScope.Node, CancellationToken.None);
        Assert.True(refresh.RefreshedCount > 0);
    }

    [Fact]
    public async Task WriteAsync_SensitiveNode_RejectedEvenWithFullTrust()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable(EditPolicyMode.FullTrust));
        var root = session.Root!;
        await Viewer.Expand(session, root);
        var apiKey = Viewer.Child(root, "ApiKey");

        var result = await session.Explorer.WriteAsync(apiKey, "new-key", CancellationToken.None);

        Assert.Equal(NodeEditOutcome.NotEditable, result.Outcome);
        Assert.Equal("sk-12345", machine.ApiKey);
    }

    // ---------- Conversions (FR-123) ----------

    [Fact]
    public async Task WriteAsync_NullKeyword_ClearsNullableAndReferenceValues()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var timeout = Viewer.Child(root, "Timeout");
        Assert.True((await session.Explorer.WriteAsync(timeout, "null", CancellationToken.None)).Success);
        Assert.Null(machine.Timeout);
        Assert.Equal("null", timeout.Display.Text);

        Assert.True((await session.Explorer.WriteAsync(timeout, "45", CancellationToken.None)).Success);
        Assert.Equal(45, machine.Timeout);

        var label = Viewer.Child(root, "Label");
        Assert.True((await session.Explorer.WriteAsync(label, "null", CancellationToken.None)).Success);
        Assert.Null(machine.Label);
    }

    [Fact]
    public async Task WriteAsync_NullOnNonNullableType_Fails()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var result = await session.Explorer.WriteAsync(Viewer.Child(root, "Speed"), "null", CancellationToken.None);

        Assert.Equal(NodeEditOutcome.ConversionFailed, result.Outcome);
        Assert.Equal(1200, machine.Speed);
    }

    [Fact]
    public async Task WriteAsync_EnumByNameAndStringRoundTrip()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var volume = Viewer.Child(root, "Volume");
        Assert.True((await session.Explorer.WriteAsync(volume, "veryloud", CancellationToken.None)).Success);
        Assert.Equal(BarkVolume.VeryLoud, machine.Volume);

        var label = Viewer.Child(root, "Label");
        Assert.True((await session.Explorer.WriteAsync(label, "  spaced  ", CancellationToken.None)).Success);
        Assert.Equal("  spaced  ", machine.Label); // strings keep their raw text
    }

    [Fact]
    public async Task WriteAsync_CultureAwareDecimal()
    {
        var machine = new EditableMachine();
        var options = InspectionOptions.SafeDefault with
        {
            EditPolicy = EditPolicyMode.PublicSetters,
            Culture = new CultureInfo("de-DE"),
        };
        await using var session = Viewer.Start(machine, options: options);
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var result = await session.Explorer.WriteAsync(Viewer.Child(root, "Price"), "1234,56", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1234.56m, machine.Price);
    }

    [Fact]
    public async Task WriteAsync_TypeConverterStruct()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var result = await session.Explorer.WriteAsync(Viewer.Child(root, "Home"), "30, 40", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(new MachinePoint(30, 40), machine.Home);
    }

    [Fact]
    public async Task WriteAsync_CustomConverterTakesPrecedence()
    {
        var machine = new EditableMachine();
        await using var session = InspectionSessionFactory.Start(new InspectionRequest
        {
            RootName = "root",
            Value = machine,
            Options = Editable(),
            CustomEditConverters = [new UpperCaseLabelConverter()],
        });
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var result = await session.Explorer.WriteAsync(Viewer.Child(root, "Label"), "anything", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("ANYTHING", machine.Label);
    }

    [Fact]
    public async Task WriteAsync_ReplacedStructuralValue_InvalidatesLoadedChildren()
    {
        var machine = new EditableMachine();
        await using var session = Viewer.Start(machine, options: Editable());
        var root = session.Root!;
        await Viewer.Expand(session, root);
        var home = Viewer.Child(root, "Home");
        await Viewer.Expand(session, home);
        Assert.True(home.Children.Count > 0);

        var result = await session.Explorer.WriteAsync(home, "30, 40", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(home.Children);                                  // old children dropped
        Assert.Equal(NodeLoadState.NotLoaded, home.LoadState);      // next expansion reloads
    }

    // ---------- Edit text pre-fill ----------

    [Theory]
    [InlineData(null, "null")]
    [InlineData(true, "true")]
    [InlineData(BarkVolume.VeryLoud, "VeryLoud")]
    public void ToEditString_ProducesEditableText(object? value, string expected)
        => Assert.Equal(expected, EditValueParser.ToEditString(value, CultureInfo.InvariantCulture));

    [Fact]
    public void ToEditString_DateTimeUsesIsoLikeFormat()
    {
        var text = EditValueParser.ToEditString(
            new DateTime(2026, 11, 12, 10, 30, 0), CultureInfo.InvariantCulture);
        Assert.Equal("2026-11-12T10:30:00", text);
    }

    [Fact]
    public void TryParse_NumericInvariantRoundTrip()
    {
        Assert.True(EditValueParser.TryParse(typeof(int), " -42 ", CultureInfo.InvariantCulture, out var i, out _));
        Assert.Equal(-42, i);

        Assert.True(EditValueParser.TryParse(typeof(decimal), "1234.56", CultureInfo.InvariantCulture, out var d, out _));
        Assert.Equal(1234.56m, d);

        Assert.False(EditValueParser.TryParse(typeof(int), "1.5", CultureInfo.InvariantCulture, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>Custom converter that upper-cases every string member value (FR-123/131).</summary>
    private sealed class UpperCaseLabelConverter : IEditValueConverter
    {
        public int Priority => 100;

        public string Name => "UpperCaseLabel";

        public bool TryConvert(string text, Type targetType, CultureInfo culture, out object? value, out string? error)
        {
            if (targetType != typeof(string))
            {
                value = null;
                error = null;
                return false;
            }
            value = text.ToUpperInvariant();
            error = null;
            return true;
        }
    }
}

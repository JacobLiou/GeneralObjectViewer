using ObjectViewer.Core.Graph;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class IdentityAndCycleTests
{
    [Fact]
    public async Task DogOwnerCycle_PetBecomesReferenceToRoot()
    {
        await using var session = Viewer.Start(SampleData.CreateDog());
        var root = await Viewer.Expand(session, session.Root!);

        var owner = await Viewer.Expand(session, Viewer.Child(root, "Owner"));
        var pet = Viewer.Child(owner, "Pet");

        Assert.Equal(ObjectValueKind.Reference, pet.ValueKind);
        Assert.False(pet.CanExpand);
        Assert.Equal("↩ reference to $", pet.Display.Text);
        Assert.Equal("Dog", pet.Display.TypeText);
    }

    [Fact]
    public async Task SelfReferencingOrder_ParentPointsToRoot()
    {
        await using var session = Viewer.Start(SampleData.CreateOrder(itemCount: 1));
        var root = await Viewer.Expand(session, session.Root!);

        var parent = Viewer.Child(root, "Parent");
        Assert.Equal(ObjectValueKind.Reference, parent.ValueKind);
        Assert.Equal("↩ reference to $", parent.Display.Text);
    }

    [Fact]
    public async Task DepartmentEmployeeCycle_DoesNotRecurseInfinitely()
    {
        await using var session = Viewer.Start(SampleData.CreateCyclicDepartment());
        var root = await Viewer.Expand(session, session.Root!);

        var employees = await Viewer.Expand(session, Viewer.Child(root, "Employees"));
        var employee = Viewer.Item(employees, 0);
        var expandedEmployee = await Viewer.Expand(session, employee);

        var department = Viewer.Child(expandedEmployee, "Department");
        Assert.Equal(ObjectValueKind.Reference, department.ValueKind);
        Assert.Equal("↩ reference to $", department.Display.Text);
    }

    [Fact]
    public async Task DetectCyclesOnly_SameObjectInTwoBranchesExpandsIndependently()
    {
        await using var session = Viewer.Start(SampleData.CreateSharedReferenceGraph());
        var root = await Viewer.Expand(session, session.Root!);

        var first = await Viewer.Expand(session, Viewer.Item(root, 0));
        var second = await Viewer.Expand(session, Viewer.Item(root, 1));

        var addressA = Viewer.Child(first, "Address");
        var addressB = Viewer.Child(second, "Address");

        Assert.Equal(ObjectValueKind.Object, addressA.ValueKind);
        Assert.Equal(ObjectValueKind.Object, addressB.ValueKind);
        Assert.True(addressA.CanExpand);
        Assert.True(addressB.CanExpand);
    }

    [Fact]
    public async Task ShowSharedReferences_SecondOccurrenceBecomesReference()
    {
        await using var session = Viewer.Start(
            SampleData.CreateSharedReferenceGraph(),
            options: Viewer.Invariant(o => o with { ReferenceMode = ReferenceMode.ShowSharedReferences }));
        var root = await Viewer.Expand(session, session.Root!);

        var first = await Viewer.Expand(session, Viewer.Item(root, 0));
        var second = await Viewer.Expand(session, Viewer.Item(root, 1));

        var addressA = Viewer.Child(first, "Address");
        Assert.Equal(ObjectValueKind.Object, addressA.ValueKind);

        var addressB = Viewer.Child(second, "Address");
        Assert.Equal(ObjectValueKind.Reference, addressB.ValueKind);
        Assert.Equal("↩ reference to $[0].Address", addressB.Display.Text);
    }

    [Fact]
    public async Task EqualsOverridden_ObjectsStayDistinct()
    {
        var list = new List<EqualsOverridden> { new(), new() };
        await using var session = Viewer.Start(list);
        var root = await Viewer.Expand(session, session.Root!);

        var first = await Viewer.Expand(session, Viewer.Item(root, 0));
        var second = await Viewer.Expand(session, Viewer.Item(root, 1));

        // Business Equals() returns true, but identity is reference-based: both items
        // remain ordinary expandable objects, never references of each other.
        Assert.Equal(ObjectValueKind.Object, first.ValueKind);
        Assert.Equal(ObjectValueKind.Object, second.ValueKind);
        Assert.True(first.CanExpand);
        Assert.True(second.CanExpand);
        Assert.Equal(ObjectValueKind.String, Viewer.Child(first, "Key").ValueKind);
        Assert.Equal(ObjectValueKind.String, Viewer.Child(second, "Key").ValueKind);
    }

    [Fact]
    public async Task DeepChain_StopsAtMaximumDepthWithTruncatedNode()
    {
        await using var session = Viewer.Start(SampleData.CreateDeepChain(depth: 40));
        var current = session.Root!;
        Assert.True(current.CanExpand);

        // Walk to depth 32 (the default MaximumDepth).
        for (var depth = 0; depth < 32; depth++)
        {
            await Viewer.Expand(session, current);
            current = Viewer.Child(current, "Next");
            Assert.Equal(depth + 1, current.Depth);
        }

        Assert.Equal(32, current.Depth);
        await Viewer.Expand(session, current);
        var marker = Assert.Single(current.Children);
        Assert.Equal(ObjectNodeKind.Truncated, marker.Kind);
        Assert.Contains("Maximum depth (32)", marker.Display.Text);
    }
}

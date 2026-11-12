using ObjectViewer.Core.Formatting;
using ObjectViewer.Core.Graph;
using ObjectViewer.Core.Members;
using ObjectViewer.Core.Metadata;
using ObjectViewer.TestModels;
using Xunit;

namespace ObjectViewer.Core.Tests;

public sealed class MemberAndPolicyTests
{
    [Fact]
    public async Task Dog_ExposesPublicPropertiesAndFields()
    {
        await using var session = Viewer.Start(SampleData.CreateDog());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var names = root.Children.Select(c => c.Name).ToArray();
        Assert.Contains("Name", names);
        Assert.Contains("Age", names);
        Assert.Contains("Owner", names);
        Assert.Contains("Bark Volume", names); // [DisplayName]
        Assert.DoesNotContain(names, n => n.StartsWith('<')); // no backing fields
    }

    [Fact]
    public async Task MetadataDemo_RespectsBrowsableDisplayAndDefaultScope()
    {
        await using var session = Viewer.Start(new MetadataDemo());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        var names = root.Children.Select(c => c.Name).ToArray();

        Assert.DoesNotContain("Hidden", names);                      // [Browsable(false)]
        Assert.DoesNotContain("this[]", names);                      // indexers skipped
        Assert.DoesNotContain("StaticProp", names);                  // static hidden by default
        Assert.DoesNotContain("PrivateField", names);                // non-public hidden by default
        Assert.Contains("NormalField", names);                       // public field included (D-02)
        Assert.Equal(ObjectNodeKind.Field, Viewer.Child(root, "NormalField").Kind);

        var shown = Viewer.Child(root, "Shown Name");
        Assert.Equal("Shown Name", shown.Name);                       // [Display(Name)]
        Assert.Equal("$.RawName", shown.Path.ToDisplayPath());        // path keeps member name (FR-072)
    }

    [Fact]
    public async Task Credentials_DefaultSensitiveNamesAreMasked()
    {
        await using var session = Viewer.Start(SampleData.CreateCredentials());
        var root = session.Root!;
        await Viewer.Expand(session, root);

        foreach (var name in new[] { "Password", "ApiToken", "ConnectionString", "CustomSecret" })
        {
            var child = Viewer.Child(root, name);
            Assert.True(child.IsSensitive);
            Assert.True(child.Display.IsMasked);
            Assert.False(child.CanExpand);
            Assert.Equal(ValueDisplay.MaskedText, child.Display.Text);
            Assert.DoesNotContain("secret", child.Display.Text, StringComparison.OrdinalIgnoreCase);
        }

        var userName = Viewer.Child(root, "UserName");
        Assert.False(userName.IsSensitive);
        Assert.Equal("\"alice\"", userName.Display.Text);
    }

    [Fact]
    public void SensitivePolicy_MatchesNamePartsCaseInsensitively()
    {
        var policy = new DefaultSensitiveDataPolicy();
        Assert.True(policy.IsSensitive("MyApiKey"));
        Assert.True(policy.IsSensitive("ACCESS_TOKEN"));
        Assert.True(policy.IsSensitive("DbConnectionString"));
        Assert.True(policy.IsSensitive("Tokenize")); // "token" is contained; matches by design
        Assert.False(policy.IsSensitive("Name"));
        Assert.False(policy.IsSensitive("Description"));
    }

    [Fact]
    public void TypeMetadataCache_SkipsIndexersAndBackingFields()
    {
        var metadata = TypeMetadataCache.Shared.Get(typeof(MetadataDemo));
        var names = metadata.Members.Select(m => m.Name).ToArray();

        Assert.DoesNotContain(names, n => n.StartsWith('<'));
        Assert.DoesNotContain("Item", names);
        Assert.Contains("Hidden", names);        // present in metadata, filtered by policy
        Assert.Contains("StaticProp", names);
    }
}

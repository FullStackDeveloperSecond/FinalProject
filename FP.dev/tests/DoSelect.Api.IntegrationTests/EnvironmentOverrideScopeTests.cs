namespace DoSelect.Api.IntegrationTests;

/// <summary>
/// 組長 PR #34 round-5 review, item 1: direct proof that <see cref="EnvironmentOverrideScope"/>
/// restores each key's actual prior value on Dispose instead of blanking it — the bug that let a
/// later fixture in this (non-parallel) assembly lose CI's job-level
/// ConnectionStrings__DefaultConnection outright. No SQL Server required; this only exercises
/// process environment variables.
/// </summary>
public sealed class EnvironmentOverrideScopeTests
{
    [Fact]
    public void Dispose_RestoresAPreviouslySetValue_InsteadOfClearingIt()
    {
        const string key = "DoSelect__EnvironmentOverrideScopeTests__PreviouslySet";
        Environment.SetEnvironmentVariable(key, "ci-original-value");
        try
        {
            using (new EnvironmentOverrideScope(new Dictionary<string, string> { [key] = "test-override-value" }))
            {
                Assert.Equal("test-override-value", Environment.GetEnvironmentVariable(key));
            }

            Assert.Equal("ci-original-value", Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void Dispose_RestoresAnUnsetKey_ToNull()
    {
        const string key = "DoSelect__EnvironmentOverrideScopeTests__PreviouslyUnset";
        Environment.SetEnvironmentVariable(key, null);

        using (new EnvironmentOverrideScope(new Dictionary<string, string> { [key] = "test-override-value" }))
        {
            Assert.Equal("test-override-value", Environment.GetEnvironmentVariable(key));
        }

        Assert.Null(Environment.GetEnvironmentVariable(key));
    }

    [Fact]
    public void Dispose_OfANestedScope_DoesNotDisturbTheOuterScopesOverride()
    {
        const string key = "DoSelect__EnvironmentOverrideScopeTests__Nested";
        Environment.SetEnvironmentVariable(key, null);
        try
        {
            using (new EnvironmentOverrideScope(new Dictionary<string, string> { [key] = "outer-value" }))
            {
                using (new EnvironmentOverrideScope(new Dictionary<string, string> { [key] = "inner-value" }))
                {
                    Assert.Equal("inner-value", Environment.GetEnvironmentVariable(key));
                }

                Assert.Equal("outer-value", Environment.GetEnvironmentVariable(key));
            }

            Assert.Null(Environment.GetEnvironmentVariable(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}

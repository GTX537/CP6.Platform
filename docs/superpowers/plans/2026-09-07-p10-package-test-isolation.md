# P10 Package Test Isolation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline; the user selected no subagents.

**Goal:** Remove the verified Windows package-build race without changing published package bytes or weakening any gate.

**Architecture:** Two Release test classes invoke dotnet pack against the same repository project and its shared bin/obj paths. Assign both to one named xUnit collection with DisableParallelization=true, also preventing these builds from racing other Release CLI readers. Other collections retain parallel execution. Do not change package sources, workflow permissions, package version or publication identity.

**Tech Stack:** .NET 8, xUnit 2, PowerShell, GitHub Actions.

## Evidence and boundary

Exact-main run [34128917890](https://github.com/GTX537/CP6.Platform/actions/runs/34128917890) at `421951a44f1dcc05b7aeb51c24a0ccaf2f03ef5f` failed only the Windows contract gate: the formal-verifier pack process could not access shared `src/CP6.Platform.Release/obj/Release/CP6.Platform.Release.0.10.0.nuspec`, already held by another process. Release tests: 213 passed, 1 failed. Inspection found P10PackageTests and FormalPackageVerifierTests run repository-local pack under separate default collections. P09 tests run in a separate sequential gate. New-P10TestPackageSet already isolates its full solution build via ArtifactsPath; it does not fix the two direct pack callers.

Published seven-package `0.10.1` run `34126521193`, source `3ff27e26962dcfd722887afb80a4306010dd9ee1`, and every package/evidence byte remain unchanged. Do not rerun publication or treat the failed main CI as downstream acceptance.

Reference: [xUnit test collections](https://xunit.net/docs/running-tests-in-parallel).

## Task 1: Deterministic failing regression

Create `tests/CP6.Platform.ReleaseTests/PackageBuildIsolationTests.cs`:

```csharp
namespace CP6.Platform.ReleaseTests;

public sealed class PackageBuildIsolationTests
{
    [Theory]
    [InlineData(typeof(P10PackageTests))]
    [InlineData(typeof(FormalPackageVerifierTests))]
    public void Shared_repository_pack_tests_use_one_nonparallel_collection(Type testClass)
    {
        const string collectionName = "PackageBuildCollection";
        var collection = Assert.Single(
            testClass.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(CollectionAttribute));
        Assert.Equal(collectionName, collection.ConstructorArguments[0].Value);

        var definition = Assert.Single(
            testClass.Assembly.GetTypes().SelectMany(type => type.GetCustomAttributesData()),
            attribute => attribute.AttributeType == typeof(CollectionDefinitionAttribute)
                && Equals(attribute.ConstructorArguments[0].Value, collectionName));
        var parallelization = Assert.Single(
            definition.NamedArguments,
            argument => argument.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization));
        Assert.Equal(true, parallelization.TypedValue.Value);
    }
}
```

- [x] Add the regression only.
- [x] Run `dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~PackageBuildIsolationTests`.
  Expected: two assertion failures at Assert.Single because collection metadata is absent; no compilation error. The CI log is the real race evidence; metadata assertions make regression deterministic.

## Task 2: Minimal fix

Create `tests/CP6.Platform.ReleaseTests/PackageBuildCollection.cs`:

```csharp
namespace CP6.Platform.ReleaseTests;

// Repository-local pack writes shared bin/obj files; isolate it from all other test collections.
[CollectionDefinition(nameof(PackageBuildCollection), DisableParallelization = true)]
public sealed class PackageBuildCollection;
```

Add this exact attribute immediately before the existing public class declaration in both `tests/CP6.Platform.ReleaseTests/P10PackageTests.cs` and `tests/CP6.Platform.ReleaseTests/FormalPackageVerifierTests.cs`:

```csharp
[Collection(nameof(PackageBuildCollection))]
```

- [x] Apply collection and both attributes.
- [x] Run the same filtered command; expected two passed.
- [x] Run `dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --no-build`; expected 216 passed, no failures.
- [x] Run `dotnet format CP6.Platform.sln --verify-no-changes --no-restore` and `dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --no-restore`; expected success.

## Task 3: Auditable delivery

Add this changelog bullet under 0.10.1.0:

> Isolate repository-local Release package-building tests in one nonparallel xUnit collection after exact-main Windows CI exposed a shared nuspec lock race; deterministic regression covers both classes. Published 0.10.1 packages and evidence are unchanged.

Add this governance note before the corrective publication evidence section:

> Post-publication CI follow-up: exact-main run 34128917890 at 421951a44f1dcc05b7aeb51c24a0ccaf2f03ef5f failed Windows contract tests because two test classes concurrently wrote the same repository-local nuspec. The package-build isolation repair changes tests only and requires a new fully green PR/main validation before downstream handoff. Formal publication run 34126521193 and all immutable 0.10.1 bytes remain unchanged; the failed CI is not acceptance evidence.

- [x] Update the two documents with these exact paragraphs.
- [x] Review all six changed files plus this plan against main; run `git diff --check`.
- [ ] Stage only the four test files, two documents and this plan; commit `test(p10): isolate repository-local package builds`.
- [ ] Push the task branch and create a PR. Require all five existing checks before normal merge, with no admin bypass.
- [ ] Verify remote main contains the task commit and its exact-main five-job run succeeds. Keep failed run as history, not acceptance.

## Local verification (2026-09-07)

- The initial regression had an xUnit-v2 metadata API compilation error; after correcting the test API, both cases failed at the expected missing collection assertion. That compilation error is not counted as RED evidence.
- With only the collection and class attributes added, both cases passed. The complete Windows Release suite passed 216/216 with zero failed or skipped tests in 5m13s, including actual package builds and RFC3161 verification.
- Format verification and all 98 architecture tests passed. The complete diff leaves src, contracts, eng and .github unchanged. No publication workflow is rerun by this repair.
- PR and exact-main acceptance remain pending; downstream consumers must wait for observed successful checks.

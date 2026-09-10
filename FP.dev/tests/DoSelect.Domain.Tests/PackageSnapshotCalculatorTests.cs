using DoSelect.Domain.Shipping;

namespace DoSelect.Domain.Tests;

public sealed class PackageSnapshotCalculatorTests
{
    [Fact]
    public void Calculate_MultipleSkus_NormalizesAndStacksShortestSides()
    {
        var result = PackageSnapshotCalculator.Calculate(
        [
            new PackageItemDimensions("SKU-A", 2, 1.5m, 40m, 30m, 20m, 1_000m),
            new PackageItemDimensions("SKU-B", 1, 2m, 35m, 25m, 15m, 2_000m),
        ]);

        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingItemKeys);
        Assert.NotNull(result.Package);
        Assert.Equal(5m, result.Package.WeightKg);
        Assert.Equal(55m, result.Package.LengthCm);
        Assert.Equal(40m, result.Package.WidthCm);
        Assert.Equal(30m, result.Package.HeightCm);
        Assert.Equal(125m, result.Package.TotalCm);
        Assert.Equal(4_000m, result.Package.DeclaredValue);
    }

    [Fact]
    public void Calculate_WhenAnyPhysicalValueIsMissing_ReturnsIncompleteWithoutGuessing()
    {
        var result = PackageSnapshotCalculator.Calculate(
        [
            new PackageItemDimensions("SKU-A", 1, null, 40m, 30m, 20m, 1_000m),
        ]);

        Assert.False(result.IsComplete);
        Assert.Null(result.Package);
        Assert.Equal(["SKU-A"], result.MissingItemKeys);
    }

    [Fact]
    public void Calculate_AssemblyGroup_UsesEnclosureDimensionsAndAllComponentWeights()
    {
        var assemblyGroupKey = Guid.NewGuid();
        var result = PackageSnapshotCalculator.Calculate(
        [
            new PackageItemDimensions("CASE", 1, 8m, 50m, 30m, 50m, 5_000m, assemblyGroupKey, true),
            new PackageItemDimensions("GPU", 1, 3m, null, null, null, 20_000m, assemblyGroupKey),
            new PackageItemDimensions("CPU", 1, 1m, null, null, null, 10_000m, assemblyGroupKey),
            new PackageItemDimensions("PSU", 1, 3m, null, null, null, 4_000m, assemblyGroupKey),
        ]);

        Assert.True(result.IsComplete);
        Assert.NotNull(result.Package);
        Assert.Equal(15m, result.Package.WeightKg);
        Assert.Equal(50m, result.Package.LengthCm);
        Assert.Equal(50m, result.Package.WidthCm);
        Assert.Equal(30m, result.Package.HeightCm);
        Assert.Equal(130m, result.Package.TotalCm);
        Assert.Equal(39_000m, result.Package.DeclaredValue);
    }

    [Fact]
    public void Calculate_AssemblyGroupWithoutAnEnclosure_ReturnsIncomplete()
    {
        var assemblyGroupKey = Guid.NewGuid();
        var result = PackageSnapshotCalculator.Calculate(
        [
            new PackageItemDimensions("GPU", 1, 3m, 60m, 40m, 20m, 20_000m, assemblyGroupKey),
        ]);

        Assert.False(result.IsComplete);
        Assert.Null(result.Package);
        Assert.Equal(["GPU"], result.MissingItemKeys);
    }

    [Fact]
    public void Evaluate_WhenAnySnapshotLimitIsExceeded_ReturnsExceededDimensions()
    {
        var package = new CalculatedPackage(5m, 55m, 40m, 30m, 125m, 4_000m);
        var limits = new PackageLimits(4m, 60m, 35m, 30m, 120m, 5_000m);

        var result = PackageConstraintEvaluator.Evaluate(package, limits);

        Assert.False(result.IsAllowed);
        Assert.Equal(
            [PackageConstraint.Weight, PackageConstraint.Width, PackageConstraint.TotalDimensions],
            result.ExceededConstraints);
    }
}

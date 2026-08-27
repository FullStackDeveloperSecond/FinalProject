namespace DoSelect.Domain.Shipping;

public sealed record PackageItemDimensions(
    string ItemKey,
    int Quantity,
    decimal? WeightKg,
    decimal? LengthCm,
    decimal? WidthCm,
    decimal? HeightCm,
    decimal UnitDeclaredValue);

public sealed record CalculatedPackage(
    decimal WeightKg,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal TotalCm,
    decimal DeclaredValue);

public sealed record PackageCalculationResult(
    bool IsComplete,
    CalculatedPackage? Package,
    IReadOnlyList<string> MissingItemKeys);

public static class PackageSnapshotCalculator
{
    public static PackageCalculationResult Calculate(
        IReadOnlyCollection<PackageItemDimensions> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one package item is required.", nameof(items));
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemKey) || item.Quantity <= 0 ||
                item.UnitDeclaredValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items));
            }
        }

        var missing = items
            .Where(item => item.WeightKg is null || item.LengthCm is null ||
                item.WidthCm is null || item.HeightCm is null)
            .Select(item => item.ItemKey.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
        {
            return new PackageCalculationResult(false, null, missing);
        }

        var normalized = items.Select(item =>
        {
            if (item.WeightKg <= 0 || item.LengthCm <= 0 || item.WidthCm <= 0 ||
                item.HeightCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(items));
            }

            var sides = new[] { item.LengthCm!.Value, item.WidthCm!.Value, item.HeightCm!.Value };
            Array.Sort(sides);
            Array.Reverse(sides);
            return new
            {
                item.Quantity,
                item.WeightKg,
                item.UnitDeclaredValue,
                Longest = sides[0],
                Middle = sides[1],
                Shortest = sides[2],
            };
        }).ToArray();

        var combinedSides = new[]
        {
            normalized.Max(item => item.Longest),
            normalized.Max(item => item.Middle),
            normalized.Sum(item => item.Shortest * item.Quantity),
        };
        Array.Sort(combinedSides);
        Array.Reverse(combinedSides);

        var package = new CalculatedPackage(
            normalized.Sum(item => item.WeightKg!.Value * item.Quantity),
            combinedSides[0],
            combinedSides[1],
            combinedSides[2],
            combinedSides.Sum(),
            normalized.Sum(item => item.UnitDeclaredValue * item.Quantity));
        return new PackageCalculationResult(true, package, []);
    }
}

public sealed record PackageLimits(
    decimal MaxWeightKg,
    decimal MaxLengthCm,
    decimal MaxWidthCm,
    decimal MaxHeightCm,
    decimal MaxTotalCm,
    decimal MaxDeclaredValue);

public enum PackageConstraint
{
    Weight,
    Length,
    Width,
    Height,
    TotalDimensions,
    DeclaredValue,
}

public sealed record PackageConstraintResult(
    bool IsAllowed,
    IReadOnlyList<PackageConstraint> ExceededConstraints);

public static class PackageConstraintEvaluator
{
    public static PackageConstraintResult Evaluate(CalculatedPackage package, PackageLimits limits)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(limits);
        if (new[]
            {
                limits.MaxWeightKg,
                limits.MaxLengthCm,
                limits.MaxWidthCm,
                limits.MaxHeightCm,
                limits.MaxTotalCm,
                limits.MaxDeclaredValue,
            }.Any(value => value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(limits));
        }

        var exceeded = new List<PackageConstraint>();
        AddIf(package.WeightKg > limits.MaxWeightKg, PackageConstraint.Weight);
        AddIf(package.LengthCm > limits.MaxLengthCm, PackageConstraint.Length);
        AddIf(package.WidthCm > limits.MaxWidthCm, PackageConstraint.Width);
        AddIf(package.HeightCm > limits.MaxHeightCm, PackageConstraint.Height);
        AddIf(package.TotalCm > limits.MaxTotalCm, PackageConstraint.TotalDimensions);
        AddIf(package.DeclaredValue > limits.MaxDeclaredValue, PackageConstraint.DeclaredValue);
        return new PackageConstraintResult(exceeded.Count == 0, exceeded);

        void AddIf(bool condition, PackageConstraint constraint)
        {
            if (condition)
            {
                exceeded.Add(constraint);
            }
        }
    }
}

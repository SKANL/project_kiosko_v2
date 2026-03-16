using CommunityToolkit.Mvvm.ComponentModel;

namespace Nodus.Judge.Presentation.ViewModels;

/// <summary>
/// Observable model for a single rubric criterion and its current score.
/// Used in VotingViewModel.Criteria to drive the dynamic scoring UI.
/// </summary>
public sealed partial class CriterionScoreItem : ObservableObject
{
    /// <summary>Stable criterion identifier matching the rubric JSON (e.g. "innovation").</summary>
    public string CriterionId { get; init; } = string.Empty;

    /// <summary>Localised display label (e.g. "Innovación").</summary>
    public string Label       { get; init; } = string.Empty;

    public double Min    { get; init; } = 0;
    public double Max    { get; init; } = 10;
    public double Step   { get; init; } = 0.5;

    /// <summary>Relative weight used when computing the weighted score.</summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>The judge's current score for this criterion. Two-way bound to the slider.</summary>
    [ObservableProperty]
    private double _value = 7.0;
}

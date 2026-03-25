using Nodus.Judge.Presentation.ViewModels;
using System.ComponentModel;
using Microsoft.Maui.Controls;

namespace Nodus.Judge.Presentation.Views;

public partial class ProjectDetailsPage : ContentPage
{
	private readonly ProjectDetailsViewModel _vm;
	private string _lastAiSummary = string.Empty;
	private bool _isSubscribed;

	public ProjectDetailsPage(ProjectDetailsViewModel vm)
	{
		InitializeComponent();
		_vm = vm;
		BindingContext = vm;
		_vm.PropertyChanged += OnViewModelPropertyChanged;
		_isSubscribed = true;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		if (_isSubscribed) return;
		_vm.PropertyChanged += OnViewModelPropertyChanged;
		_isSubscribed = true;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		if (!_isSubscribed) return;
		_vm.PropertyChanged -= OnViewModelPropertyChanged;
		_isSubscribed = false;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		Dispatcher.Dispatch(async () =>
		{
			if (e.PropertyName == nameof(ProjectDetailsViewModel.AiSummary))
			{
				var current = _vm.AiSummary;
				if (string.IsNullOrWhiteSpace(current)) return;
				if (string.Equals(current, _lastAiSummary, StringComparison.Ordinal)) return;
				_lastAiSummary = current;

				if (AiSummaryCard == null) return;
				AiSummaryCard.Opacity = 0;
				await AiSummaryCard.FadeToAsync(1, 220, Easing.CubicOut);
				return;
			}

			if (e.PropertyName == nameof(ProjectDetailsViewModel.IsDescriptionExpanded))
			{
				await AnimateTextSectionAsync(DescriptionBodyLabel);
				return;
			}

			if (e.PropertyName == nameof(ProjectDetailsViewModel.IsObjectivesExpanded))
			{
				await AnimateTextSectionAsync(ObjectivesBodyLabel);
				return;
			}

			if (e.PropertyName == nameof(ProjectDetailsViewModel.IsTeamExpanded))
			{
				await AnimateTextSectionAsync(TeamBodyLabel);
				return;
			}

			if (e.PropertyName == nameof(ProjectDetailsViewModel.IsTechExpanded))
			{
				await AnimateTextSectionAsync(TechBodyLabel);
			}
		});
	}

	private static async Task AnimateTextSectionAsync(Label? label)
	{
		if (label == null || !label.IsVisible) return;

		// Let bindings settle so the label contains the target text before measuring.
		await Task.Yield();

		var width = label.Width;
		if (width <= 0)
		{
			width = label.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
			if (width <= 0) return;
		}

		var start = label.Height;
		if (start <= 0)
		{
			start = label.Measure(width, double.PositiveInfinity).Height;
			if (start <= 0) return;
		}

		var end = label.Measure(width, double.PositiveInfinity).Height;
		if (end <= 0 || Math.Abs(end - start) < 1)
		{
			label.HeightRequest = -1;
			return;
		}

		label.AbortAnimation("SectionExpand");
		label.HeightRequest = start;

		var animation = new Animation(v => label.HeightRequest = v, start, end, Easing.CubicOut);
		var tcs = new TaskCompletionSource<bool>();
		animation.Commit(
			label,
			"SectionExpand",
			length: 220,
			finished: (_, _) =>
			{
				label.HeightRequest = -1;
				tcs.TrySetResult(true);
			});

		await tcs.Task;
	}
}

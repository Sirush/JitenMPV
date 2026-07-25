using System;
using CommunityToolkit.Mvvm.ComponentModel;
using JitenMPV.Core.Api.Models;
using JitenMPV.Core.Config;
using JitenMPV.Core.Theming;

namespace JitenMPV.App.ViewModels;

public partial class StateStyleViewModel : ViewModelBase
{
    public KnownState State { get; }
    public string DisplayName { get; }

    [ObservableProperty] private string _textColor = "#ffffff";
    [ObservableProperty] private string _outlineColor = "#000000";
    [ObservableProperty] private string _shadowColor = "#000000";
    [ObservableProperty] private bool _hasShadow;

    [ObservableProperty] private double _outlineSize = 3;
    [ObservableProperty] private double _shadowDepth = 1;

    [ObservableProperty] private int _textOpacityPercent = 100;

    [ObservableProperty] private bool _bold;
    [ObservableProperty] private bool _italic;
    [ObservableProperty] private bool _underline;
    [ObservableProperty] private bool _strikethrough;

    [ObservableProperty] private string _swatchColor = "#ffffff";

    public StateStyleViewModel(KnownState state)
    {
        State = state;
        DisplayName = state.ToString();
    }

    partial void OnTextColorChanged(string value)
        => SwatchColor = value;

    public CustomStateStyle ToCustomStateStyle()
    {
        var opacity = TextOpacityPercent < 100
            ? (int?)Math.Round(TextOpacityPercent * 255.0 / 100)
            : null;

        return new CustomStateStyle
        {
            TextColor = TextColor,
            OutlineColor = OutlineColor,
            ShadowColor = HasShadow ? ShadowColor : null,
            OutlineSize = OutlineSize,
            ShadowDepth = HasShadow ? ShadowDepth : null,
            TextOpacity = opacity,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strikethrough = Strikethrough,
        };
    }

    public static StateStyleViewModel FromCustomStateStyle(KnownState state, CustomStateStyle css)
    {
        var vm = new StateStyleViewModel(state)
        {
            TextColor = css.TextColor,
            OutlineColor = css.OutlineColor,
            ShadowColor = css.ShadowColor ?? "#000000",
            HasShadow = css.ShadowColor is not null,
            OutlineSize = css.OutlineSize,
            ShadowDepth = css.ShadowDepth ?? 1,
            TextOpacityPercent = css.TextOpacity is { } op
                ? (int)Math.Round(op * 100.0 / 255)
                : 100,
            Bold = css.Bold,
            Italic = css.Italic,
            Underline = css.Underline,
            Strikethrough = css.Strikethrough,
        };
        vm.SwatchColor = vm.TextColor;
        return vm;
    }

    public static StateStyleViewModel FromWordStyleState(KnownState state, WordStyleState ws)
        => FromCustomStateStyle(state, CustomStateStyle.FromWordStyleState(ws));
}

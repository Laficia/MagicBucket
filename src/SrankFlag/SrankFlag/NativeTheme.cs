using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;

namespace SrankFlag;

internal static class NativeTheme
{
    private static long nextPoll;
    private static int theme = -1;
    internal static Vector4 Text { get; private set; } = new(0.93f, 0.93f, 0.93f, 1f);
    internal static Vector4 ButtonText { get; private set; } = Vector4.One;

    internal static Vector4 ConditionColor(ConditionPhase phase) => phase switch
    {
        ConditionPhase.Active => new(.35f, .95f, .45f, 1),
        ConditionPhase.Soon => new(1, .82f, .18f, 1),
        ConditionPhase.Waiting => new(.95f, .28f, .30f, 1),
        _ => Text
    };

    // Called on the framework thread only, after KTK initialization completes.
    internal static unsafe void RefreshIfChanged(Action<string> log)
    {
        if (Environment.TickCount64 < nextPoll) return;
        nextPoll = Environment.TickCount64 + 200;
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkUIColorHolder == null) return;
        int nextTheme = (int)stage->AtkUIColorHolder->ActiveColorThemeType;
        Vector4 nextText = ColorHelper.GetColor(8);
        Vector4 nextButtonText = ColorHelper.GetColor(50);
        if (theme == nextTheme && Text == nextText && ButtonText == nextButtonText) return;
        theme = nextTheme;
        Text = nextText;
        ButtonText = nextButtonText;
        // Refreshes native textures, text bindings and our custom node overrides together.
        ColorHelper.RefreshColorTheme();
        log($"[原生UI] 主题配色已更新：theme={theme}，正文={Text}，按钮={ButtonText}");
    }
}

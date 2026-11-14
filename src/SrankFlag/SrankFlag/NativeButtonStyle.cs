using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace SrankFlag;

internal static class NativeButtonStyle
{
	private sealed class Origin
	{
		internal Vector4 Bg;

		internal Vector4 Fg;
	}

	private sealed class HoverState
	{
		internal bool Hovered;

		internal Vector4 Idle;

		internal Vector4 Hover;

		internal ThemedTextNode? Label;
	}

	internal const float MinW = 40f;

	private const float PadX = 36f;

	private const float CjkPx = 12.5f;

	private const float AsciiPx = 6.8f;

	private const float LineH = 15f;

	private static readonly ConditionalWeakTable<TextButtonNode, Origin> Origins = new ConditionalWeakTable<TextButtonNode, Origin>();

	private static readonly ConditionalWeakTable<TextButtonNode, HoverState> Hovers = new ConditionalWeakTable<TextButtonNode, HoverState>();

	private static readonly Vector4 HoverWhite = new Vector4(1f, 1f, 1f, 1f);

	private static readonly Vector4 HoverNearWhite = new Vector4(0.92f, 0.94f, 0.98f, 1f);

	private static readonly Vector4 HoverGold = new Vector4(1f, 0.78f, 0.3f, 1f);

	private static readonly Vector4 HoverGoldLight = new Vector4(1f, 0.88f, 0.55f, 1f);

	private static HoverState Ensure(TextButtonNode b)
	{
		return Hovers.GetValue(b, (TextButtonNode _) => new HoverState
		{
			Idle = b.LabelNode.TextColor,
			Hover = HoverWhite
		});
	}

	private static ThemedTextNode Lab(TextButtonNode b)
	{
		HoverState hoverState = Ensure(b);
		if (hoverState.Label != null)
		{
			return hoverState.Label;
		}
		TextNode labelNode = b.LabelNode;
		ThemedTextNode textNode = new ThemedTextNode
		{
			Position = labelNode.Position,
			Size = new Vector2(MathF.Max(1f, labelNode.Width), MathF.Max(1f, labelNode.Height)),
			FontSize = labelNode.FontSize,
			FontType = FontType.Axis,
			LineSpacing = 12u,
			AlignmentType = labelNode.AlignmentType,
			TextColorID = 50,
			UseButtonPalette = true,
			String = ""
		};
		textNode.AddTextFlags(TextFlags.MultiLine);
		textNode.AttachNode((NodeBase?)b, NodePosition.AsLastChild);
		hoverState.Label = textNode;
		hoverState.Idle = labelNode.TextColor;
		labelNode.IsVisible = false;
		return textNode;
	}

	internal static void SetLabel(TextButtonNode b, string? text)
	{
		try
		{
			Lab(b).String = text ?? "";
		}
		catch
		{
		}
	}

	private static void PinLabel(TextButtonNode b, Vector2 pos, Vector2 size, AlignmentType align)
	{
		ThemedTextNode textNode = Lab(b);
		textNode.Position = pos;
		textNode.Size = new Vector2(MathF.Max(1f, size.X), MathF.Max(1f, size.Y));
		textNode.AlignmentType = align;
	}

	private static void SyncLabel(TextButtonNode b)
	{
		TextNode labelNode = b.LabelNode;
		PinLabel(b, labelNode.Position, new Vector2(MathF.Max(1f, labelNode.Width), MathF.Max(1f, labelNode.Height)), labelNode.AlignmentType);
	}

	private static void SetSize(TextButtonNode b, float w, float h)
	{
		b.Size = new Vector2(w, h);
	}

	internal static void SizePin(TextButtonNode b, float w, float h)
	{
		SetSize(b, w, h);
		SyncLabel(b);
	}

	internal static void InitHover(TextButtonNode b)
	{
		HoverState st = Ensure(b);
		NeutralizeColors(b);
		b.ShowClickableCursor = true;
		Lab(b);
		b.AddEvent(AtkEventType.MouseOver, () =>
		{
			st.Hovered = true;
			RePaint(b);
		});
		b.AddEvent(AtkEventType.MouseOut, () =>
		{
			st.Hovered = false;
			RePaint(b);
		});
	}

	internal static void HideBackground(TextButtonNode b)
	{
		Lab(b).UseButtonPalette = false;
		b.BackgroundNode.IsVisible = false;
		b.BackgroundNode.Alpha = 0f;
		b.BackgroundNode.AddColor = Vector3.Zero;
		b.BackgroundNode.MultiplyColor = Vector3.One;
	}

	internal static void Paint(TextButtonNode b, Vector4 idle, Vector4 hover)
	{
		Lab(b).UseButtonPalette = b.BackgroundNode.IsVisible;
		HoverState hoverState = Ensure(b);
		hoverState.Idle = idle;
		hoverState.Hover = hover;
		Lab(b).TextColor = (hoverState.Hovered ? hover : idle);
		NeutralizeColors(b);
	}

	private static void RePaint(TextButtonNode b)
	{
		if (Hovers.TryGetValue(b, out HoverState value))
		{
			Lab(b).TextColor = (value.Hovered ? value.Hover : value.Idle);
			NeutralizeColors(b);
		}
	}

	private static void NeutralizeColors(TextButtonNode b)
	{
		TextNode textNode = Lab(b);
		textNode.AddColor = Vector3.Zero;
		textNode.MultiplyColor = Vector3.One;
		b.LabelNode.AddColor = Vector3.Zero;
		b.LabelNode.MultiplyColor = Vector3.One;
		b.BackgroundNode.AddColor = Vector3.Zero;
		b.BackgroundNode.MultiplyColor = Vector3.One;
		b.BackgroundNode.Position = Vector2.Zero;
	}

	internal static void SoftenTextInput(TextInputNode n)
	{
		n.FocusBorderNode.MultiplyColor = new Vector3(0.46f, 0.56f, 0.72f);
		n.FocusBorderNode.AddColor = Vector3.Zero;
		n.BackgroundNode.MultiplyColor = new Vector3(0.7f, 0.76f, 0.86f);
		n.BackgroundNode.AddColor = Vector3.Zero;
	}

	internal static void Prepare(TextButtonNode b)
	{
		b.LabelNode.AddTextFlags(TextFlags.MultiLine);
		Lab(b).AddTextFlags(TextFlags.MultiLine);
	}

	internal static string Safe(string? s)
	{
		return s ?? "";
	}

	internal static float Measure(string? s)
	{
		float num = 0f;
		if (s == null)
		{
			return num;
		}
		foreach (char c in s)
		{
			if ((c != '\n' && c != '\r') || 1 == 0)
			{
				num += ((c < '\u0080') ? 6.8f : 12.5f);
			}
		}
		return num;
	}

	internal static float FitText(ThemedTextNode n, string? text, float maxW, float minW = 40f)
	{
		text = Safe(text);
		n.String = text;
		float num = Math.Clamp(Measure(text) + 8f, minW, maxW);
		if (MathF.Abs(n.Width - num) >= 0.5f)
		{
			n.Width = num;
		}
		return num;
	}

	internal static float Apply(TextButtonNode b, string? label, float maxW, float baseH, bool selected)
	{
		if (b == null)
		{
			return 0f;
		}
		label = Safe(label);
		if (maxW < 40f)
		{
			maxW = 40f;
		}
		float num = MathF.Max(16f, maxW - 36f);
		float num2 = Measure(label);
		string text;
		int lines;
		float num3;
		if (label.Length == 0 || num2 <= num)
		{
			text = label;
			lines = 1;
			num3 = Math.Clamp(num2 + 36f, 40f, maxW);
		}
		else
		{
			text = Wrap(label, num, out lines);
			num3 = maxW;
		}
		float h = baseH + (float)Math.Max(0, lines - 1) * 15f;
		SetSize(b, num3, h);
		SetLabel(b, text);
		SyncLabel(b);
		SetSelected(b, selected);
		return num3;
	}

	private static string Wrap(string s, float avail, out int lines)
	{
		StringBuilder stringBuilder = new StringBuilder(s.Length + 8);
		float num = 0f;
		lines = 1;
		foreach (char c in s)
		{
			if (c == '\r')
			{
				continue;
			}
			if (c == '\n')
			{
				stringBuilder.Append('\n');
				num = 0f;
				lines++;
				continue;
			}
			float num2 = ((c < '\u0080') ? 6.8f : 12.5f);
			if (num + num2 > avail && num > 0f)
			{
				stringBuilder.Append('\n');
				num = 0f;
				lines++;
			}
			stringBuilder.Append(c);
			num += num2;
		}
		return stringBuilder.ToString();
	}

	internal static float Radio(TextButtonNode b, string? text, float maxW, float baseH, bool on)
	{
		text = Safe(text);
		b.BackgroundNode.IsVisible = false;
		float result = Apply(b, text, maxW, baseH, selected: false);
		Origin origin = GetOrigin(b);
		Paint(b, on ? new Vector4(1f, 0.72f, 0.24f, origin.Fg.W) : origin.Fg, on ? HoverGoldLight : HoverWhite);
		HideBackground(b);
		return result;
	}

	internal static float PlainRadio(TextButtonNode b, string? label, float maxW, float baseH, bool on, bool dimOff = false)
	{
		label = Safe(label);
		float num = Math.Clamp(Measure(label) + 36f, 40f, maxW);
		SetSize(b, num, baseH);
		SetLabel(b, label);
		SyncLabel(b);
		Origin origin = GetOrigin(b);
		Vector4 vector;
		if (on)
		{
			vector = new Vector4(1f, 0.78f, 0.3f, origin.Fg.W);
		}
		else
		{
			vector = (dimOff ? new Vector4(0.42f, 0.45f, 0.5f, origin.Fg.W) : new Vector4(0.66f, 0.7f, 0.76f, origin.Fg.W));
		}
		TextButtonNode b2 = b;
		Vector4 idle = vector;
		Vector4 hover;
		if (on)
		{
			hover = HoverGoldLight;
		}
		else
		{
			hover = (dimOff ? HoverNearWhite : HoverWhite);
		}
		Paint(b2, idle, hover);
		HideBackground(b);
		return num;
	}

	internal static void ClearBadge(TextButtonNode b, float baseH)
	{
		SetSize(b, 26f, baseH);
		SetLabel(b, "×");
		PinLabel(b, new Vector2(0f, 1f), new Vector2(26f, baseH - 2f), AlignmentType.Center);
		Paint(b, new Vector4(0.7f, 0.74f, 0.8f, 1f), HoverWhite);
		HideBackground(b);
	}

	internal static void MiniAction(TextButtonNode b, string? label, float baseH, bool on)
	{
		label = Safe(label);
		float num = MathF.Max(28f, Measure(label) + 6f);
		SetSize(b, num, baseH);
		SetLabel(b, label);
		PinLabel(b, new Vector2(1f, 1f), new Vector2(num - 2f, baseH - 2f), AlignmentType.Center);
		b.BackgroundNode.IsVisible = true;
		Origin origin = GetOrigin(b);
		b.BackgroundNode.Color = (on ? new Vector4(0.92f, 0.64f, 0.18f, origin.Bg.W) : origin.Bg);
		Paint(b, origin.Fg, on ? HoverGoldLight : HoverWhite);
	}

	internal static void SortHeader(TextButtonNode b, string? label, float h, bool on)
	{
		label = Safe(label);
		float num = Math.Clamp(Measure(label) + 8f, 30f, 120f);
		SetSize(b, num, h);
		SetLabel(b, label);
		PinLabel(b, new Vector2(0f, 1f), new Vector2(num, h - 2f), AlignmentType.Left);
		Paint(b, on ? new Vector4(1f, 0.78f, 0.3f, 1f) : new Vector4(0.58f, 0.62f, 0.68f, 1f), on ? HoverGoldLight : HoverWhite);
		HideBackground(b);
	}

	internal static void CompactRadio(TextButtonNode b, string? label, float baseH, bool on)
	{
		label = Safe(label);
		float num = MathF.Max(28f, Measure(label) + 8f);
		SetSize(b, num, baseH);
		SetLabel(b, label);
		PinLabel(b, new Vector2(0f, 1f), new Vector2(num, baseH - 2f), AlignmentType.Center);
		Paint(b, on ? new Vector4(1f, 0.78f, 0.3f, 1f) : new Vector4(0.62f, 0.64f, 0.68f, 1f), on ? HoverGoldLight : HoverWhite);
		HideBackground(b);
	}

	internal static void GhostButton(TextButtonNode b, string? label, float baseH, bool hover)
	{
		label = Safe(label);
		float num = MathF.Max(28f, Measure(label) + 8f);
		SetSize(b, num, baseH);
		SetLabel(b, label);
		PinLabel(b, new Vector2(0f, 1f), new Vector2(num, baseH - 2f), AlignmentType.Center);
		Paint(b, new Vector4(0.74f, 0.8f, 0.9f, 1f), HoverGold);
		HideBackground(b);
	}

	internal static void HintTag(TextButtonNode b, string? label, float maxW, float baseH)
	{
		label = Safe(label);
		float num = Math.Clamp(Measure(label) + 8f, 40f, maxW);
		SetSize(b, num, baseH);
		SetLabel(b, label);
		PinLabel(b, new Vector2(0f, 1f), new Vector2(num, baseH - 2f), AlignmentType.Left);
		Vector4 vector = new Vector4(0.86f, 0.9f, 0.98f, 1f);
		Paint(b, vector, vector);
		HideBackground(b);
	}

	internal static void SectionHeader(TextButtonNode b, string? label, float h, bool open)
	{
		label = Safe(label);
		float num = MathF.Max(28f, Measure(label) + 8f);
		SetSize(b, num, h);
		SetLabel(b, label);
		PinLabel(b, new Vector2(0f, 1f), new Vector2(num, h - 2f), AlignmentType.Left);
		Paint(b, open ? new Vector4(0.94f, 0.78f, 0.42f, 1f) : new Vector4(0.62f, 0.66f, 0.72f, 1f), open ? HoverGoldLight : HoverWhite);
		HideBackground(b);
	}

	internal static void RunButton(TextButtonNode b, string? label, float w, float h, bool enabled)
	{
		label = Safe(label);
		SetSize(b, w, h);
		SetLabel(b, label);
		SyncLabel(b);
		b.BackgroundNode.IsVisible = true;
		Origin origin = GetOrigin(b);
		b.BackgroundNode.Color = (enabled ? new Vector4(0.92f, 0.64f, 0.18f, origin.Bg.W) : new Vector4(0.18f, 0.2f, 0.24f, origin.Bg.W));
		Paint(b, enabled ? new Vector4(1f, 0.98f, 0.9f, origin.Fg.W) : new Vector4(0.45f, 0.48f, 0.53f, origin.Fg.W), enabled ? HoverWhite : new Vector4(0.45f, 0.48f, 0.53f, origin.Fg.W));
	}

	internal static void Fixed(TextButtonNode b, string? label, float w, float baseH, Vector4 bg, Vector4 fg)
	{
		label = Safe(label);
		float num = MathF.Max(16f, w - 12f);
		float num2 = Measure(label);
		string text;
		int lines;
		if (label.Length == 0 || num2 <= num)
		{
			text = label;
			lines = 1;
		}
		else
		{
			text = Wrap(label, num, out lines);
		}
		float num3 = baseH + (float)Math.Max(0, lines - 1) * 15f;
		SetSize(b, w, num3);
		SetLabel(b, text);
		PinLabel(b, new Vector2(5f, 2f), new Vector2(w - 10f, num3 - 4f), AlignmentType.Left);
		b.BackgroundNode.IsVisible = true;
		Origin origin = GetOrigin(b);
		b.BackgroundNode.Color = new Vector4(bg.X, bg.Y, bg.Z, origin.Bg.W);
		Paint(b, new Vector4(fg.X, fg.Y, fg.Z, origin.Fg.W), HoverWhite);
	}

	internal static void Icon(TextButtonNode b, string label, Vector4 tint, bool on)
	{
		SetSize(b, 42f, 42f);
		SetLabel(b, label);
		PinLabel(b, new Vector2(2f, 2f), new Vector2(38f, 38f), AlignmentType.Center);
		b.BackgroundNode.IsVisible = true;
		Origin origin = GetOrigin(b);
		float num = (on ? 1f : 0.55f);
		b.BackgroundNode.Color = new Vector4(tint.X * num, tint.Y * num, tint.Z * num, origin.Bg.W);
		Paint(b, on ? new Vector4(1f, 0.96f, 0.72f, origin.Fg.W) : new Vector4(0.82f, 0.85f, 0.92f, origin.Fg.W), HoverWhite);
	}

	internal static void Stripe(TextButtonNode b, string label, float w, float baseH, bool alternate, bool selected)
	{
		Vector4 bg;
		if (selected)
		{
			bg = new Vector4(0.9f, 0.58f, 0.16f, 1f);
		}
		else
		{
			bg = (alternate ? new Vector4(0.2f, 0.23f, 0.3f, 1f) : new Vector4(0.12f, 0.15f, 0.21f, 1f));
		}
		Vector4 fg = (selected ? new Vector4(1f, 0.98f, 0.86f, 1f) : new Vector4(0.88f, 0.92f, 1f, 1f));
		Fixed(b, label, w, baseH, bg, fg);
	}

	internal static void Glow(ThemedTextNode n, string text, Vector4 color, uint fontSize)
	{
		n.String = text;
		n.FontSize = fontSize;
		n.FontType = FontType.TrumpGothic;
		n.TextColor = color;
		n.TextOutlineColorID = 7;
		n.AddTextFlags(TextFlags.Bold | TextFlags.Edge | TextFlags.Glare);
		n.AlignmentType = AlignmentType.Left;
	}

	internal static void TeleportLine(TextButtonNode b, string? label, float w, float baseH, bool selected)
	{
		label = Safe(label);
		Vector4 vector = new Vector4(0.62f, 0.66f, 0.72f, 0.42f);
		Fixed(b, label, w, baseH, vector, new Vector4(0.9f, 0.93f, 0.98f, 1f));
		if (selected)
		{
			b.BackgroundNode.Color = vector;
		}
		b.BackgroundNode.IsVisible = selected;
		Lab(b).UseButtonPalette = selected;
	}

	private static Origin GetOrigin(TextButtonNode b)
	{
		if (!Origins.TryGetValue(b, out Origin value))
		{
			value = new Origin
			{
				Bg = b.BackgroundNode.Color,
				Fg = b.LabelNode.TextColor
			};
			Origins.Add(b, value);
		}
		value.Fg = b.LabelNode.TextColor;
		return value;
	}

	internal static void SetSelected(TextButtonNode b, bool on)
	{
		Origin origin = GetOrigin(b);
		if (on)
		{
			b.BackgroundNode.Color = new Vector4(1f, 0.68f, 0.22f, origin.Bg.W);
			Paint(b, new Vector4(1f, 0.97f, 0.82f, origin.Fg.W), HoverWhite);
		}
		else
		{
			b.BackgroundNode.Color = origin.Bg;
			Paint(b, origin.Fg, HoverWhite);
		}
	}
}

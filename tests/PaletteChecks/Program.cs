using System.Numerics;
using SrankFlag;
using System.Text.Json;

// UIColor rows 8 and 50 read from this installed CN client on 2026-11-14.
var themes = new (string Name, uint Text, uint ButtonText)[]
{
    ("Dark", 4007773695, uint.MaxValue),
    ("Light", 2102541311, uint.MaxValue),
    ("ClassicFF", uint.MaxValue, uint.MaxValue),
    ("ClearBlue", 4007773695, uint.MaxValue),
    ("ClearWhite", 2102541311, uint.MaxValue),
    ("ClearGreen", 4007773695, uint.MaxValue),
    ("Unknown2", 4007773695, uint.MaxValue),
    ("Unknown3", 2102541311, uint.MaxValue),
};
var textColors = new (string Name, Vector4 Color)[]
{
    ("body", new(.86f, .89f, .94f, 1)),
    ("hint", new(.52f, .56f, .62f, 1)),
    ("disabled", new(.42f, .45f, .50f, 1)),
    ("link", new(.30f, .82f, 1, 1)),
    ("gold", new(1, .82f, .30f, 1)),
    ("success", new(.45f, .95f, .50f, 1)),
    ("danger", new(.95f, .33f, .30f, 1)),
    ("condition-failed", new(.74f, .30f, .30f, 1)),
    ("hover", Vector4.One),
};
Vector4 stripeA = new(.12f, .15f, .21f, .85f), stripeB = new(.18f, .21f, .28f, .85f);
int checks = 0;
double minimum = double.MaxValue;
foreach (var theme in themes)
{
    var native = Unpack(theme.Text);
    bool light = NativePalette.IsLight(native);
    var panel = light ? new Vector4(.66f, .67f, .69f, 1) : new Vector4(.16f, .17f, .20f, 1);
    foreach (var color in textColors)
    {
        var text = NativePalette.Text(color.Color, native);
        foreach (var background in new[] { panel, Composite(NativePalette.Surface(stripeA, native, true), panel), Composite(NativePalette.Surface(stripeB, native, true), panel) })
        {
            double contrast = Contrast(text, background);
            minimum = Math.Min(minimum, contrast);
            Check(contrast >= 4.5, $"{theme.Name}/{color.Name}: contrast {contrast:F3}");
        }
        Check(text.W == color.Color.W, "Text opacity changed");
        Check(text.X >= 0 && text.Y >= 0 && text.Z >= 0 && text.X <= 1 && text.Y <= 1 && text.Z <= 1, "Color outside RGB bounds");
        if (color.Name == "success") Check(text.Y > text.X && text.Y > text.Z, "Success lost green hue");
        if (color.Name == "danger") Check(text.X > text.Y && text.X > text.Z, "Danger lost red hue");
    }
    // Native buttons remain dark with white text even in the Light theme.
    var buttonText = NativePalette.Text(Vector4.One, Unpack(theme.ButtonText));
    Check(buttonText == Vector4.One, "Native button text was incorrectly darkened");
    Check(NativePalette.Surface(stripeA, native, true).W == stripeA.W, "Row opacity changed");
    if (light)
        Check(NativePalette.Luminance(NativePalette.Surface(stripeA, native, true)) > .75, "History row remained dark in light theme");
}

Console.WriteLine($"PASS: {checks} checks across {themes.Length} native color-table variants; minimum reference-surface contrast {minimum:F2}:1.");

string[] states = ["open", "cd", "over", "unknown"];
Check(states.Where(s => HuntWindowDisplay.Includes(s, true)).SequenceEqual(["open"]), "Checked filter leaked a closed/unknown hunt");
Check(states.Where(s => HuntWindowDisplay.Includes(s, false)).SequenceEqual(["cd"]), "Unchecked filter leaked an open/expired hunt");
Check(!new[] { "cd", "over" }.Any(s => HuntWindowDisplay.Includes(s, true)), "Zero open hunts must produce an empty list");
Check(HuntWindowDisplay.Countdown(0) == "00:00:00", "Zero countdown");
Check(HuntWindowDisplay.Countdown(.1) == "00:00:01", "Countdown announced an opening early");
Check(HuntWindowDisplay.Countdown(90061) == "25:01:01", "Multi-day countdown lost total hours");
Check(HuntWindowDisplay.Countdown(double.NaN) == "时间未知", "Invalid countdown should not be invented");
foreach (float width in new[] { 384f, 564f, 764f, 1164f, 1964f })
{
    var c = NativeWindowLayout.MainColumns(width);
    Check(c.WorldX+c.WorldWidth < c.NameX, "Server/name columns overlap");
    Check(c.NameX+c.NameWidth < c.ProgressX, "Name/timer columns overlap");
    Check(c.ProgressX+c.ProgressWidth+42 <= width+.01f, "Timer column exceeds resized window");
    Check(Math.Abs(NativeWindowLayout.HistoryColumns(width).Sum()-width)<.01f, "History columns exceed resized window");
}
Console.WriteLine("PASS: strict open/closed filters, countdown boundaries and resized column bounds.");

var reference = JsonSerializer.Deserialize<List<ForecastReference>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"site-forecast-reference.json")))!;
foreach (var sample in reference)
{
    var actual = ConditionForecast.NextWindow(sample.En,sample.Now);
    Check(actual?.Start == sample.Start && actual?.End == sample.End,$"Website forecast differs: {sample.En} at {sample.Now}; expected {sample.Start}..{sample.End}, actual {actual?.Start}..{actual?.End}");
}
Check(ConditionClock.Evaluate(true,1000,1200,1000).Phase==ConditionPhase.Active,"Condition start must turn green immediately");
Check(ConditionClock.Evaluate(true,1000,1200,700).Phase==ConditionPhase.Soon,"Exactly five minutes must turn yellow");
Check(ConditionClock.Evaluate(true,1000,1200,699.9).Phase==ConditionPhase.Waiting,"More than five minutes must stay red");
Check(ConditionClock.Evaluate(true,1000,1200,1200).Phase==ConditionPhase.Unknown,"Expired condition must not remain green");
Check(ConditionClock.Evaluate(false,null,null,1000).Phase==ConditionPhase.Manual,"Action prerequisites must not be presented as timed/fulfilled");
Check(ConditionClock.TimerText(new(ConditionPhase.Active,61))=="条件已进入 · 剩余 00:01:01","Main detail countdown must identify the active condition");
Check(ConditionClock.TimerText(new(ConditionPhase.Soon,300),true)=="还有 00:05:00","Overlay countdown differs from main detail");
Check(ConditionClock.RangeText(0,90000)=="北京时间 01-01 08:00:00 — 01-02 09:00:00","Condition range lost the Beijing offset or end date");
var longWindow=ConditionForecast.NextWindow("Okina",18*4200);
Check(longWindow?.Start==15*4200+12*175 && longWindow?.End==15*4200+108*175,"Okina's ongoing multi-day full-moon window was lost after day two");
using(var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"srank_data.json"))))
{
    var all=data.RootElement.GetProperty("dcs").EnumerateArray().SelectMany(dc=>dc.GetProperty("hunts").EnumerateArray()).Select(h=>h.GetProperty("en").GetString()!).ToHashSet();
    Check(all.All(en=>ConditionCatalog.Find(en) is {Method.Length:>0}),"A known S rank is missing its trigger conditions");
    var worlds=data.RootElement.GetProperty("dcs").EnumerateArray().SelectMany(dc=>dc.GetProperty("worlds").EnumerateArray()).Select(w=>w.GetString()!).Distinct().ToArray();
    Check(worlds.Select(ServerColors.ForWorld).Distinct().Count()==worlds.Length,"Different servers share the same color");
}
Console.WriteLine($"PASS: {reference.Count} website forecast samples, red/yellow/green boundaries, all {ConditionCatalog.All.Count} hunt definitions and distinct server colors.");

string subscriptionDirectory=Path.Combine(Path.GetTempPath(),"MagicBucket-subscriptions-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(subscriptionDirectory);
try
{
    var subscriptions=new ConditionSubscriptions(subscriptionDirectory);
    Check(ConditionCatalog.All.Count(x=>subscriptions.Contains(x.En))==13,"Compact default must retain the website's 13 timed subscriptions");
    string extra=ConditionCatalog.All.First(x=>!ConditionForecast.HasSpec(x.En)).En;
    subscriptions.Toggle(extra);
    Check(new ConditionSubscriptions(subscriptionDirectory).Contains(extra),"Subscription selection was not persisted");
    subscriptions.SelectAll(true);
    Check(ConditionCatalog.All.All(x=>new ConditionSubscriptions(subscriptionDirectory).Contains(x.En)),"All 47 S ranks must remain subscribable");
    subscriptions.SelectAll(false);
    Check(!ConditionCatalog.All.Any(x=>new ConditionSubscriptions(subscriptionDirectory).Contains(x.En)),"Cleared subscriptions returned after reload");
}
finally
{
    File.Delete(Path.Combine(subscriptionDirectory,"condition_subscriptions.json"));
    Directory.Delete(subscriptionDirectory);
}
Console.WriteLine("PASS: compact defaults, all-hunt selection and subscription persistence.");

foreach(string dc in new[]{"莫古力","猫小胖","陆行鸟"})
{
    Check(!BearRefreshPolicy.UsesMainSite(dc),"Other DC selected the main-site source");
    Check(!BearRefreshPolicy.CanStart(dc,false,TimeSpan.FromDays(100)),"Other DC started automatic loading");
    Check(BearRefreshPolicy.CanStart(dc,true,TimeSpan.FromSeconds(10)),"Explicit manual loading was refused");
    Check(!BearRefreshPolicy.CanStart(dc,true,TimeSpan.FromSeconds(9)),"Manual refresh throttle was lost");
}
Check(BearRefreshPolicy.UsesMainSite("豆豆柴")&&BearRefreshPolicy.CanStart("豆豆柴",false,TimeSpan.FromMinutes(15)),"Automatic main-site loading changed for 豆豆柴");
using(var history=JsonDocument.Parse("[{\"Time\":50},{\"Time\":200,\"Canceled\":true},{\"Time\":300,\"Canceled\":1},{\"Time\":100,\"Canceled\":0},{\"Time\":\"999\"}]"))
    Check(BearRefreshPolicy.LatestKill(history.RootElement)==100,"Canceled or invalid bear records changed the last kill");
Check(BearRefreshPolicy.ResolveDeath(200,100,0,true)==100,"Manual bear loading must correct a canceled newer record");
Check(BearRefreshPolicy.ResolveDeath(200,100,250,true)==250,"Manual bear loading overwrote a newer Sonar kill");
Check(BearRefreshPolicy.ResolveDeath(200,0,0,true)==0,"Unknown authoritative history retained a stale kill");
Check(BearRefreshPolicy.ResolveDeath(200,100,0,false)==200,"Non-authoritative updates replaced a newer kill");
Check(BearRefreshPolicy.CompletionNotice("莫古力","公开熊站",350,376,0,false).StartsWith("刷新完成：莫古力"),"A completed manual refresh lost its notice");
Check(BearRefreshPolicy.CompletionNotice("猫小胖","公开熊站",0,329,0,false).StartsWith("刷新完成"),"An empty valid history must not be reported as network failure");
Check(BearRefreshPolicy.CompletionNotice("陆行鸟","公开熊站",20,24,3,false).Contains("刷新部分完成")&&BearRefreshPolicy.CompletionNotice("陆行鸟","公开熊站",20,24,3,false).Contains("3 次请求失败"),"A partial result was reported as complete");
Check(BearRefreshPolicy.CompletionNotice("莫古力","公开熊站",0,0,6,true).StartsWith("刷新失败"),"An unavailable service was reported as complete");
Check(BearRefreshPolicy.CompletionNotice("莫古力","公开熊站",12,16,0,true).Contains("超时或中止"),"An interrupted manual refresh lost its warning");
Console.WriteLine("PASS: three DCs require manual bear loading, main-site routing is restricted, and canceled records are ignored.");
Console.WriteLine("PASS: manual refresh notices distinguish complete, empty, partial, interrupted and failed results.");

void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
static Vector4 Unpack(uint rgba) => new((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
static Vector4 Composite(Vector4 fg, Vector4 bg) => new(fg.X * fg.W + bg.X * (1 - fg.W), fg.Y * fg.W + bg.Y * (1 - fg.W), fg.Z * fg.W + bg.Z * (1 - fg.W), 1);
static double Contrast(Vector4 a, Vector4 b)
{
    double x = NativePalette.Luminance(a), y = NativePalette.Luminance(b);
    return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
}
internal sealed record ForecastReference(string En,double Now,double? Start,double? End);

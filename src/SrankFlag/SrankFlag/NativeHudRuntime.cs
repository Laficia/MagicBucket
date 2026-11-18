using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Plugin;
using KamiToolKit;
using KamiToolKit.BaseTypes;

namespace SrankFlag;

internal sealed class NativeHudRuntime
{
	internal const bool Enabled = true;

	internal const bool EnabledAuto = true;

	internal const bool EnabledMain = true;

	private readonly HudActions actions;

	private readonly AutoWindowActions autoActions;

	private readonly MainActions mainActions;

	private readonly SettingsActions settingsActions;

	private readonly HistoryActions historyActions;

	private readonly Action<string> log;

	private Task? initTask;

	private HudWindow? window;

	private AutoWindow? autoWindow;

	private MainWindow? mainWindow;

	private SettingsWindow? settingsWindow;

	private NativeHistoryWindow? historyWindow;
	private ConditionWindow? conditionWindow;
	private ConditionWindow? conditionFloating;
	private readonly ConditionActions conditionFloatingActions;
	private DateTime lastConditionFloatingSyncUtc;
	private HiddenHuntWindow? hiddenHuntWindow;
	private readonly HiddenHuntActions hiddenHuntActions;
	private DateTime lastHiddenHuntsSyncUtc = DateTime.MinValue;
	private readonly ConditionActions conditionActions;
	private DateTime lastConditionsSyncUtc = DateTime.MinValue;

	private bool failed;

	private bool autoFailed;

	private bool mainFailed;

	private bool settingsFailed;

	private bool historyFailed;

	private bool disposed;

	private DateTime lastSyncUtc = DateTime.MinValue;

	private DateTime lastAutoSyncUtc = DateTime.MinValue;

	private DateTime lastMainSyncUtc = DateTime.MinValue;

	private DateTime lastSettingsSyncUtc = DateTime.MinValue;

	private DateTime lastHistorySyncUtc = DateTime.MinValue;

	private DateTime lastTickErrorLogUtc = DateTime.MinValue;

	internal bool Active
	{
		get
		{
			if (!failed && !disposed)
			{
				return window != null;
			}
			return false;
		}
	}

	internal bool ActiveAuto
	{
		get
		{
			if (!failed && !autoFailed && !disposed)
			{
				return autoWindow != null;
			}
			return false;
		}
	}

	internal bool ActiveMain
	{
		get
		{
			if (!failed && !mainFailed && !disposed)
			{
				return mainWindow != null;
			}
			return false;
		}
	}

	internal NativeHudRuntime(HudActions actions, AutoWindowActions autoActions, MainActions mainActions, SettingsActions settingsActions, HistoryActions historyActions, ConditionActions conditionActions, ConditionActions conditionFloatingActions, HiddenHuntActions hiddenHuntActions, Action<string> log)
	{
		this.actions = actions;
		this.autoActions = autoActions;
		this.mainActions = mainActions;
		this.settingsActions = settingsActions;
		this.historyActions = historyActions;
		this.conditionActions = conditionActions;
		this.conditionFloatingActions = conditionFloatingActions;
		this.hiddenHuntActions = hiddenHuntActions;
		this.log = log;
		MainWindow.Logger = log;
		SettingsWindow.Logger = log;
		HudWindow.Logger = log;
		NativeHistoryWindow.Logger = log;
	}

	private bool InitReady()
	{
		Task task = initTask;
		if (task == null || !task.IsCompleted)
		{
			return false;
		}
		if (task.IsFaulted)
		{
			failed = true;
			log($"[原生UI] 初始化异常，全部回退（无 ImGui 兜底）：{task.Exception}");
			return false;
		}
		return true;
	}

	internal void BeginInit(IDalamudPluginInterface pi)
	{
		if (disposed)
		{
			return;
		}
		try
		{
			initTask = KamiToolKitLibrary.InitializeAsync(pi);
			log("[原生HUD] KamiToolKit 初始化中…");
		}
		catch (Exception ex)
		{
			failed = true;
			log("[原生HUD] KTK 初始化失败，回退（无 ImGui 兜底）：" + ex.Message);
		}
	}

	internal void Tick(bool wantOpen, Func<HudSnapshot> snapshot)
	{
		if (failed || disposed)
		{
			return;
		}
		try
		{
			if (window == null)
			{
				if (!InitReady())
				{
					return;
				}
				window = new HudWindow(actions)
				{
					InternalName = "MagicBucketNativeHud",
					Title = "触发中 · 魔法水桶",
					Size = new Vector2(360f, 300f)
				};
				log("[原生HUD] 原生窗口已就绪");
			}
			if (!((DateTime.UtcNow - lastSyncUtc).TotalMilliseconds < 200.0))
			{
				lastSyncUtc = DateTime.UtcNow;
				if (wantOpen && !window.IsOpen)
				{
					window.Open();
				}
				else if (!wantOpen && window.IsOpen)
				{
					window.Close();
				}
				if (window.IsOpen)
				{
					ApplyMeasured("触发悬浮窗", snapshot, window.Apply);
				}
			}
		}
		catch (Exception value)
		{
			if ((DateTime.UtcNow - lastTickErrorLogUtc).TotalSeconds >= 5.0)
			{
				lastTickErrorLogUtc = DateTime.UtcNow;
				log($"[原生HUD] 本帧刷新异常，已跳过并重试（不影响其它窗口）：{value}");
			}
		}
	}

	internal void TickAuto(bool wantOpen, Func<AutoWindowSnapshot> snapshot)
	{
		if (failed || autoFailed || disposed)
		{
			return;
		}
		try
		{
			if (autoWindow == null)
			{
				if (!InitReady())
				{
					return;
				}
				autoWindow = new AutoWindow(autoActions)
				{
					InternalName = "MagicBucketNativeAuto",
					Title = "自动前往 · 魔法水桶",
					Size = new Vector2(660f, 430f)
				};
				log("[原生UI] 自动前往原生窗口已就绪");
			}
			if (!((DateTime.UtcNow - lastAutoSyncUtc).TotalMilliseconds < 200.0))
			{
				lastAutoSyncUtc = DateTime.UtcNow;
				if (wantOpen && !autoWindow.IsOpen)
				{
					autoWindow.Open();
				}
				else if (!wantOpen && autoWindow.IsOpen)
				{
					autoWindow.Close();
				}
				if (autoWindow.IsOpen)
				{
					ApplyMeasured("自动前往", snapshot, autoWindow.Apply);
				}
			}
		}
		catch (Exception value)
		{
			if ((DateTime.UtcNow - lastTickErrorLogUtc).TotalSeconds >= 5.0)
			{
				lastTickErrorLogUtc = DateTime.UtcNow;
				log($"[原生UI] 自动前往窗本帧刷新异常，已跳过并重试：{value}");
			}
		}
	}

	internal void TickMain(bool wantOpen, Func<MainSnapshot> snapshot)
	{
		if (failed || mainFailed || disposed)
		{
			return;
		}
		try
		{
			if (!InitReady()) return;
			NativeTheme.RefreshIfChanged(log);
			if (mainWindow == null)
			{
				if (!InitReady())
				{
					return;
				}
				mainWindow = new MainWindow(mainActions)
				{
					InternalName = "MagicBucketNativeMain",
					Title = "魔法水桶 · S 怪速查",
					Size = new Vector2(600f, 800f)
				};
				log("[原生UI] 主窗口原生窗口已就绪");
			}
			if ((DateTime.UtcNow - lastMainSyncUtc).TotalMilliseconds < 350.0)
			{
				return;
			}
			lastMainSyncUtc = DateTime.UtcNow;
			if (wantOpen && !mainWindow.IsOpen)
			{
				mainWindow.Open();
			}
			else if (!wantOpen && mainWindow.IsOpen)
			{
				mainWindow.SuppressClosed = true;
				try
				{
					mainWindow.Close();
				}
				finally
				{
					mainWindow.SuppressClosed = false;
				}
			}
			if (mainWindow.IsOpen)
			{
				ApplyMeasured("主窗口", snapshot, mainWindow.Apply);
			}
		}
		catch (Exception value)
		{
			if ((DateTime.UtcNow - lastTickErrorLogUtc).TotalSeconds >= 5.0)
			{
				lastTickErrorLogUtc = DateTime.UtcNow;
				log($"[原生UI] 主窗口本帧刷新异常，已跳过并重试：{value}");
			}
		}
	}

	private void ApplyMeasured<T>(string name, Func<T> snapshot, Action<T> apply)
	{
		long start = System.Diagnostics.Stopwatch.GetTimestamp();
		T data = snapshot();
		long built = System.Diagnostics.Stopwatch.GetTimestamp();
		apply(data);
		double applyMs = System.Diagnostics.Stopwatch.GetElapsedTime(built).TotalMilliseconds;
		double buildMs = System.Diagnostics.Stopwatch.GetElapsedTime(start, built).TotalMilliseconds;
		if (buildMs + applyMs >= 25)
			log($"[原生UI] {name}刷新耗时 {buildMs + applyMs:F1} ms（数据 {buildMs:F1} ms，布局 {applyMs:F1} ms）");
	}

	internal void ForceMainRefresh()
	{
		lastMainSyncUtc = DateTime.MinValue;
	}

	internal bool ResizeWindow(string name, float width, float height)
	{
		ResponsiveAddon? target = name.ToLowerInvariant() switch
		{
			"main" => mainWindow,
			"auto" => autoWindow,
			"hud" => window,
			"settings" => settingsWindow,
			"history" => historyWindow,
			"conditions" => conditionWindow,
			"watch" => conditionFloating,
			"hidden" => hiddenHuntWindow,
			_ => null
		};
		if (target == null) return false;
		target.ResizeTo(width, height);
		target.RememberSize();
		ReviveAfterError();
		return true;
	}

	internal void ReviveAfterError()
	{
		autoFailed = false;
		mainFailed = false;
		settingsFailed = false;
		historyFailed = false;
		lastSyncUtc = (lastAutoSyncUtc = (lastMainSyncUtc = (lastSettingsSyncUtc = (lastHistorySyncUtc = DateTime.MinValue))));
	}

	internal void SetMainWindowOpen(bool open)
	{
		ForceOne(mainWindow, open, "主窗", () =>
		{
			lastMainSyncUtc = DateTime.MinValue;
		});
	}

	internal void SetHudWindowOpen(bool open)
	{
		ForceOne(window, open, "触发悬浮窗", () =>
		{
			lastSyncUtc = DateTime.MinValue;
		});
	}

	internal void SetAutoWindowOpen(bool open)
	{
		ForceOne(autoWindow, open, "自动前往窗", () =>
		{
			lastAutoSyncUtc = DateTime.MinValue;
		});
	}

	internal void SetSettingsWindowOpen(bool open)
	{
		ForceOne(settingsWindow, open, "设置面板", () =>
		{
			lastSettingsSyncUtc = DateTime.MinValue;
		});
	}

	internal void SetHistoryWindowOpen(bool open)
	{
		ForceOne(historyWindow, open, "历史窗口", () =>
		{
			lastHistorySyncUtc = DateTime.MinValue;
		});
	}

	private void ForceOne(NativeAddon? w, bool open, string name, Action resetThrottle)
	{
		if (w == null || disposed)
		{
			return;
		}
		try
		{
			if (open && !w.IsOpen)
			{
				w.Open();
			}
			else if (!open && w.IsOpen)
			{
				SuppressClose(w);
			}
			resetThrottle();
		}
		catch (Exception ex)
		{
			log("[原生UI] " + name + " 状态同步失败，尝试释放重开：" + ex.Message);
			try
			{
				SuppressClose(w);
				w.Open();
			}
			catch (Exception ex2)
			{
				log("[原生UI] " + name + " 释放重开仍失败：" + ex2.Message);
			}
		}
	}

	private static void SuppressClose(NativeAddon w)
	{
		if(w is ConditionWindow conditions)
		{
			conditions.SuppressClosed=true;
			try{conditions.Close();}finally{conditions.SuppressClosed=false;}
			return;
		}
		if (!(w is MainWindow mainWindow))
		{
			if (!(w is HudWindow hudWindow))
			{
				if (!(w is AutoWindow autoWindow))
				{
					if (!(w is SettingsWindow settingsWindow))
					{
						if (w is NativeHistoryWindow nativeHistoryWindow)
						{
							nativeHistoryWindow.SuppressClosed = true;
							try
							{
								nativeHistoryWindow.Close();
								return;
							}
							finally
							{
								nativeHistoryWindow.SuppressClosed = false;
							}
						}
						w.Close();
						return;
					}
					settingsWindow.SuppressClosed = true;
					try
					{
						settingsWindow.Close();
						return;
					}
					finally
					{
						settingsWindow.SuppressClosed = false;
					}
				}
				autoWindow.SuppressClosed = true;
				try
				{
					autoWindow.Close();
					return;
				}
				finally
				{
					autoWindow.SuppressClosed = false;
				}
			}
			hudWindow.SuppressClosed = true;
			try
			{
				hudWindow.Close();
				return;
			}
			finally
			{
				hudWindow.SuppressClosed = false;
			}
		}
		mainWindow.SuppressClosed = true;
		try
		{
			mainWindow.Close();
		}
		finally
		{
			mainWindow.SuppressClosed = false;
		}
	}

	internal void TickSettings(bool wantOpen, Func<SettingsSnapshot> snapshot)
	{
		if (failed || settingsFailed || disposed)
		{
			return;
		}
		try
		{
			if (settingsWindow == null)
			{
				if (!InitReady())
				{
					return;
				}
				settingsWindow = new SettingsWindow(settingsActions)
				{
					InternalName = "MagicBucketNativeSettings",
					Title = "魔法水桶 · 设置",
					Size = new Vector2(420f, 430f)
				};
				log("[原生UI] 设置面板原生窗口已就绪");
			}
			if (!wantOpen)
			{
				if (settingsWindow.IsOpen)
				{
					settingsWindow.SuppressClosed = true;
					try
					{
						settingsWindow.Close();
						return;
					}
					finally
					{
						settingsWindow.SuppressClosed = false;
					}
				}
				return;
			}
			if (!settingsWindow.IsOpen)
			{
				settingsWindow.Open();
				lastSettingsSyncUtc = DateTime.MinValue;
			}
			if (!((DateTime.UtcNow - lastSettingsSyncUtc).TotalMilliseconds < 200.0))
			{
				lastSettingsSyncUtc = DateTime.UtcNow;
				if (settingsWindow.IsOpen)
				{
					ApplyMeasured("设置", snapshot, settingsWindow.Apply);
				}
			}
		}
		catch (Exception value)
		{
			if ((DateTime.UtcNow - lastTickErrorLogUtc).TotalSeconds >= 5.0)
			{
				lastTickErrorLogUtc = DateTime.UtcNow;
				log($"[原生UI] 设置面板本帧刷新异常，已跳过并重试：{value}");
			}
		}
	}

	internal void ForceSettingsRefresh()
	{
		lastSettingsSyncUtc = DateTime.MinValue;
	}

	internal void TickHistory(bool wantOpen, Func<HistorySnapshot> snapshot)
	{
		if (failed || historyFailed || disposed)
		{
			return;
		}
		try
		{
			if (historyWindow == null)
			{
				if (!InitReady())
				{
					return;
				}
				historyWindow = new NativeHistoryWindow(historyActions)
				{
					InternalName = "MagicBucketNativeHistory",
					Title = "魔法水桶 · S 怪历史",
					Size = new Vector2(700f, 520f)
				};
				log("[原生UI] 历史窗口已就绪");
			}
			if (!wantOpen)
			{
				if (historyWindow.IsOpen)
				{
					historyWindow.SuppressClosed = true;
					try
					{
						historyWindow.Close();
						return;
					}
					finally
					{
						historyWindow.SuppressClosed = false;
					}
				}
				return;
			}
			if (!historyWindow.IsOpen)
			{
				historyWindow.Open();
				lastHistorySyncUtc = DateTime.MinValue;
			}
			if (!((DateTime.UtcNow - lastHistorySyncUtc).TotalMilliseconds < 200.0))
			{
				lastHistorySyncUtc = DateTime.UtcNow;
				if (historyWindow.IsOpen)
				{
					ApplyMeasured("历史", snapshot, historyWindow.Apply);
				}
			}
		}
		catch (Exception value)
		{
			if ((DateTime.UtcNow - lastTickErrorLogUtc).TotalSeconds >= 5.0)
			{
				lastTickErrorLogUtc = DateTime.UtcNow;
				log($"[原生UI] 历史窗口本帧刷新异常，已跳过并重试：{value}");
			}
		}
	}

	internal void ForceHistoryRefresh()
	{
		lastHistorySyncUtc = DateTime.MinValue;
	}

	internal void SetConditionsWindowOpen(bool open)
	{
		ForceOne(conditionWindow, open, "条件订阅", () => lastConditionsSyncUtc = DateTime.MinValue);
	}

	internal void ShowMainDetails(string key)
	{
		mainWindow?.ShowDetails(key);
		SetMainWindowOpen(true);
		ForceMainRefresh();
	}

	internal void TickConditions(bool open, ConditionSnapshot snapshot)
		=> TickConditionWindow(ref conditionWindow, ref lastConditionsSyncUtc, open, snapshot, false);

	internal void TickConditionFloating(bool open, ConditionSnapshot snapshot)
		=> TickConditionWindow(ref conditionFloating, ref lastConditionFloatingSyncUtc, open, snapshot, true);

	private void TickConditionWindow(ref ConditionWindow? target, ref DateTime lastSync, bool open, ConditionSnapshot snapshot, bool floating)
	{
		if (disposed || failed || (!open && target == null) || !InitReady()) return;
		if ((DateTime.UtcNow - lastSync).TotalMilliseconds < 200) return;
		lastSync = DateTime.UtcNow;
		try
		{
			target ??= new ConditionWindow(floating ? conditionFloatingActions : conditionActions, floating)
			{
				InternalName = floating ? "MagicBucketNativeWatch" : "MagicBucketNativeConditions",
				Title = floating ? "魔法水桶 · S 怪提醒" : "魔法水桶 · 条件订阅",
				Size = floating ? new Vector2(345, 320) : new Vector2(450, 520)
			};
			if (open && !target.IsOpen) target.Open();
			else if (!open && target.IsOpen)
			{
				target.SuppressClosed = true;
				try { target.Close(); } finally { target.SuppressClosed = false; }
			}
			if (target.IsOpen) target.Apply(snapshot);
		}
		catch (Exception ex) { log("[原生UI] 条件订阅刷新失败："+ex); }
	}

	internal void TickHiddenHunts(bool open, HiddenHuntSnapshot snapshot)
	{
		if (disposed || failed || !InitReady() || (!open && hiddenHuntWindow == null)) return;
		if ((DateTime.UtcNow - lastHiddenHuntsSyncUtc).TotalMilliseconds < 200) return;
		lastHiddenHuntsSyncUtc = DateTime.UtcNow;
		try
		{
			hiddenHuntWindow ??= new HiddenHuntWindow(hiddenHuntActions)
			{
				InternalName = "MagicBucketNativeHiddenHunts",
				Title = "魔法水桶 · 已隐藏",
				Size = new Vector2(400, 380)
			};
			if (open && !hiddenHuntWindow.IsOpen) hiddenHuntWindow.Open();
			else if (!open && hiddenHuntWindow.IsOpen)
			{
				hiddenHuntWindow.SuppressClosed = true;
				try { hiddenHuntWindow.Close(); } finally { hiddenHuntWindow.SuppressClosed = false; }
			}
			if (hiddenHuntWindow.IsOpen) hiddenHuntWindow.Apply(snapshot);
		}
		catch (Exception ex)
		{
			if ((DateTime.UtcNow - lastTickErrorLogUtc).TotalSeconds < 5) return;
			lastTickErrorLogUtc = DateTime.UtcNow;
			log("[原生UI] 已隐藏窗口刷新失败：" + ex);
		}
	}

	internal void Shutdown()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		try { if (conditionFloating != null) { conditionFloating.SuppressClosed = true; conditionFloating.Dispose(); conditionFloating = null; } }
		catch (Exception ex) { log("[原生UI] 订阅悬浮窗关闭失败：" + ex.Message); }
		try
		{
			if (hiddenHuntWindow != null)
			{
				hiddenHuntWindow.SuppressClosed = true;
				hiddenHuntWindow.Dispose();
				hiddenHuntWindow = null;
			}
		}
		catch (Exception ex) { log("[原生UI] 已隐藏窗口关闭失败：" + ex.Message); }
		try { if (conditionWindow != null) { conditionWindow.SuppressClosed=true; conditionWindow.Close(); conditionWindow=null; } } catch (Exception ex) { log("[原生UI] 条件订阅关闭失败："+ex.Message); }
		try
		{
			window?.Close();
		}
		catch
		{
		}
		try
		{
			autoWindow?.Close();
		}
		catch
		{
		}
		try
		{
			SettingsWindow settingsWindow = this.settingsWindow;
			if (settingsWindow != null)
			{
				settingsWindow.SuppressClosed = true;
				settingsWindow.Close();
			}
		}
		catch
		{
		}
		try
		{
			MainWindow mainWindow = this.mainWindow;
			if (mainWindow != null)
			{
				mainWindow.SuppressClosed = true;
				mainWindow.Close();
			}
		}
		catch
		{
		}
		try
		{
			NativeHistoryWindow nativeHistoryWindow = historyWindow;
			if (nativeHistoryWindow != null)
			{
				nativeHistoryWindow.SuppressClosed = true;
				nativeHistoryWindow.Close();
			}
		}
		catch
		{
		}
		window = null;
		autoWindow = null;
		this.mainWindow = null;
		this.settingsWindow = null;
		historyWindow = null;
		Task task = initTask;
		if (task == null || !task.IsCompletedSuccessfully)
		{
			return;
		}
		try
		{
			KamiToolKitLibrary.Dispose();
		}
		catch (Exception ex)
		{
			log("[原生HUD] 卸载清理异常：" + ex.Message);
		}
	}
}

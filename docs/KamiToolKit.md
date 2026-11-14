# KamiToolKit 使用记录

研究对象：[Dalamud-DailyRoutines/KamiToolKit](https://github.com/Dalamud-DailyRoutines/KamiToolKit)。参考源码在工作区 `reports/references/KamiToolKit`，主分支提交 `39a6e11618a7a5d2781102936a2b7607bb1e1c41`；插件携带 DLL 对应 `68bc31f2c0eb0e45d2308cd96a8bdbbb0be6e12d`。修改以实际携带 DLL 的 API 为准，不直接替换依赖版本。

## 生命周期

等待 `KamiToolKitLibrary.InitializeAsync` 完成后使用原生 UI。窗口继承 `NativeAddon`，`OnSetup` 建节点，`OnHide` 同步关闭意图，`OnFinalize` 清理引用。关闭会释放原生节点，再开时重新构建。`Close` 包含动画，不等于立即销毁；异步关闭等待不能堵塞框架线程。

节点修改、事件注册和释放都在游戏框架线程执行。通过 `AttachNode` / `AddNode` 建立托管和原生所有权，避免重复挂载，不在 `Dispose` 前额外 `DetachNode`。用 `SuppressClosed` 区分主动隐藏与用户关闭，传送时保留原有窗口意图。

`NodeBase.AddEvent(MouseDown/MouseUp/MouseClick)` 会给目标添加阻挡式碰撞标记。原生复选框的右键扩展应注册到它已有的 `CollisionNode`，避免在组件外层新增碰撞目标，导致原生 `ButtonClick` 收不到左键。选中状态只在模型变化时同步，不能反复根据旧快照调用 `SetChecked` 打断手势；原生宽高为整数，应先取整再比较，避免小数尺寸每帧重复写入。

## 尺寸与文字

`ResponsiveAddon` 为六个窗口提供共享缩放、拖动结束清理和独立尺寸保存。`ScrollingNode<ResNode>` 负责剪裁、滚动；尺寸变化重排现有节点，列宽由 `NativeWindowLayout` 分配，按钮通过 `Flow` 换行。`ThemedTextNode.Fit` 使用原生字形测量，缓存排版结果，过长文本有完整悬停提示。

右下角使用 `TextureButtonNode` 和 `ui/uld/ChatLog.tex` 原生缩放角标，纹理坐标与翻转方式参考库内部 `ResizeButtonNode`。拖动使用真实原生 MouseDown 及有期限的 viewport MouseMove / MouseUp 事件。鼠标位移按 addon 实际缩放换算；回调结束后的下一帧释放监听器，隐藏和销毁也清理监听器。不要在监听器自己的原生回调里释放其虚表。

Dalamud 开发插件可从内存加载，`Assembly.Location` 可能为空。配置位置通过 `IDalamudPluginInterface.AssemblyLocation` 获取。

## 主题配色

`TextColorID` / `TextOutlineColorID` 可绑定游戏 UIColor 行；直接写 `TextColor` / `TextOutlineColor` 会取消绑定。原版写死浅色文字后，背景换主题而文字不变。

当前客户端 UIColor 8 为正文，7 为描边，50 为按钮文字。浅色主题按钮仍使用白字，透明文字按钮和有底纹按钮需要区分。`NativeTheme` 在框架线程观察主题编号及颜色变化，再调用 `ColorHelper.RefreshColorTheme` 更新纹理与节点。

`ThemedTextNode` 保留原始语义色，主题变化时重算可读性，避免反复转换造成偏色。状态、悬停和行底色分别处理。自定义文字不要通过 `TextNode` 基类引用重新赋色，以免绕过适配层。

主站五服色：水晶塔 `#f5a742`、银泪湖 `#15c238`、太阳海岸 `#b1c215`、伊修加德 `#70b8ff`、红茶川 `#ff697f`。`ServerColors` 保存原始色相，渲染时适配主题对比度。

## 条件目录

通过 Babel AST 读取静态条件数据，未运行页面 DOM 或网络脚本。当前 47 只 S 的条件均已覆盖；其中 13 条天气、ET、月相规则使用主站独立计算样本对账。击杀、理符、宠物、FATE 等行动条件不能从时钟推断已完成，条件时间和各服 CD 分开显示。

主窗详情复用同一套条件计时，说明、倒计时、北京时间起止、操作区分别占行。长文本换行后使用原生测量高度留出空间；`Fit` 的缓存命中也恢复目标宽度，避免其他旧排版帮助方法覆盖宽度后发生额外换行和挤压。设置窗口销毁时清空滚动内容引用，防止关闭动画后的刷新访问已释放节点。

可选条件窗参考主站的精简提醒：默认 13 条时间规则；全部 47 只 S 在订阅管理内可选。提醒只展示名称、状态计时、已进闸服务器色签，完整方法与各服 CD 在悬停提示中。

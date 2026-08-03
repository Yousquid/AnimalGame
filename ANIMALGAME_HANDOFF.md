# AnimalGame 项目交接文档

> 更新日期：2026-08-03  
> Git 仓库根目录：`C:\Users\andsonyou\Documents\GitHub\AnimalGame`  
> Unity 工程目录：`C:\Users\andsonyou\Documents\GitHub\AnimalGame\AnimalGame`  
> Unity 版本：`2022.3.16f1c1`

## 1. 这份文档怎么用

这份文档是给换电脑后的开发者或新的 Codex 会话使用的项目上下文。开始工作前应先完整阅读本文，再检查当前分支的代码、Prefab 和场景。本文记录的是截至 2026-08-03 的设计意图与实现结构；如果文档和当前代码不一致，以当前代码和 Prefab 序列化值为准，并同步修订本文。

建议在新电脑上的第一次对话中直接告诉 Codex：

```text
请先完整阅读 Git 根目录的 ANIMALGAME_HANDOFF.md，然后检查当前 Git 状态、
Unity 版本、启用的 Build Scene、Assets/Scripts 下的实现及 Resources Prefab。
在理解现有架构和设计原因之前不要重写核心系统；之后再处理我的新需求。
```

## 2. 项目一句话概括

AnimalGame 是一个以“无传统视觉的生态调查机器人”为玩家视角的 2D 等高线探索游戏原型。玩家看到的是机器人根据灰度高度图生成的传感器地图，通过地形、坡度、重心、扫描与可通行性信息来移动和理解世界，而不是直接观看写实的 3D 环境。

长期玩法方向包括：

- 无战斗的类开放世界探索；
- 通过行走、爬坡、钩锁、爬墙、喷气等能力穿越地形；
- 发现、追踪和记录动物；
- 清理人类从太空投下的垃圾；
- 通过照片上传和垃圾回收获得机械部件，再反哺探索能力。

当前 Demo 重点仍是地图、移动、坡度、重心、镜头、扫描和可通行性 UI，动物、垃圾和完整升级循环尚不是当前实现核心。

## 3. 不可丢失的核心设计原则

1. **高度场是空间事实，等高线是它的表现。** 玩家不能直接与等高线碰撞。
2. **移动判定与 UI 判定必须读取同一个高度场和同一个判定器。** 不允许为了显示方便再写一套近似坡度规则。
3. **坡面与台阶分开检测。** 坡面来自机器人脚下拟合平面；台阶来自未平滑细节高度场的局部突变。
4. **普通陡坡不再直接把玩家当墙挡住。** 二级坡表现为费力与自然后滑；三级坡表现为抓地失败、挣扎、侧滑和方向失控。只有高台阶、边界和过大危险下坡继续硬停止。
5. **镜头跟随重心目标，不直接跟随玩家 Transform。** 视觉朝向可以慢慢恢复，但物理滑动方向要更快趋向真实下坡方向，避免横向漂移感。
6. **扫描可通行性标记是绝对地图位置的短期调查结果。** Debug 网格与正式扫描显示是两个独立系统。
7. **通行性标志在屏幕中保持固定角度。** 它们不随机器人或相机旋转。
8. **构建版必须与编辑器一致。** 动态等高线 Shader 必须有序列化直接引用，玩家 UI 尺寸按屏幕像素保持稳定。

## 4. 仓库和工程结构

```text
AnimalGame/                         Git 仓库根目录
├── ANIMALGAME_HANDOFF.md           本文
└── AnimalGame/                     Unity 工程
    ├── Assets/
    │   ├── Animation/Scan UI/      扫描启停键动画
    │   ├── Arts/                   UI、机器人和扫描图像资产
    │   ├── Resources/              运行时 Bootstrap 加载的 Prefab
    │   ├── Scenes/
    │   ├── Scripts/MapTest/        高度场、等高线和通行性显示
    │   ├── Scripts/RobotMap/       玩家、重心、镜头和扫描
    │   └── Shaders/                动态等高线 Shader
    ├── Packages/
    └── ProjectSettings/
```

当前 Build Settings 中实际启用的主场景是：

```text
Assets/Scenes/HeightMapPlayerScene.unity
```

旧的 `SampleScene` 和 `MapTestScene` 不作为当前正式构建入口。

## 5. 运行时装配流程

`HeightMapPlayerSceneBootstrap` 是主场景入口。它通过 `Resources.Load` 实例化：

```text
MapTest/MapTestController
Robot/RobotMarker
Camera/RobotCamera
Traversal/HeightMapTraversalEvaluator
Traversal/TraversalOverlay
Traversal/TraversalScanOverlay
UI/MainUI
```

随后完成主要依赖连接：

```text
MapTestSceneController
    ├── 生成/缓存 BakedHeightField
    ├── 创建地图 Sprite 与动态等高线 Material
    └── 向其他系统提供米制地图坐标和高度采样

HeightMapTraversalEvaluator.Initialize(map)
RobotMover.SetTraversalEvaluator(evaluator)
RobotHeightMotionDetector.Initialize(map)
RobotCameraShake.Initialize(robot, balance, heightMotion)
RobotCameraFollow.FollowBalanceTarget(balance)
TraversalOverlayUI.Initialize(...)
TraversalScanOverlayUI.Initialize(..., ScanChargeUI)
```

Bootstrap 还负责限制玩家不离开地图边界，并可显示实时 FPS。

## 6. 高度图、Bake 和等高线

### 6.1 当前地图 Prefab 的真实配置

`Assets/Resources/MapTest/MapTestController.prefab` 当前主要值：

| 参数 | 当前值 | 作用 |
|---|---:|---|
| Map Width | 250 m | 灰度图横向对应的物理宽度 |
| Map Height | 250 m | 灰度图纵向对应的物理长度 |
| Min Height | 0 m | 最低海拔 |
| Max Height | 70 m | 最高海拔 |
| Baked Resolution | 2048 | Bake 高度场采样分辨率 |
| Normalize Source Range | true | 将原图实际灰度范围归一化到完整高度范围 |
| Surface Smoothing Sigma | 0.75 m | 用于坡面拟合的物理尺度平滑 |
| Preview Resolution | 2160 | 地图显示纹理分辨率 |
| Contour Interval | 8 m | 等高距 |
| Pixels Per Unit | 32 | Sprite 世界单位换算 |
| Min Contour Width | 0.7 | 当前镜头内最低线宽 |
| Max Contour Width | 3.68 | 当前镜头内最高线宽 |
| Max Coverage | 0.3 | 防止相邻等高线因过粗合并 |
| Viewport Samples | 64 | 估计当前镜头高度范围的采样数 |

**重要：**早期讨论曾按 500×500 米推演，但当前 Prefab 已是 250×250 米。不要沿用旧对话中的地图尺寸。

### 6.2 `BakedHeightField`

`Assets/Scripts/MapTest/BakedHeightField.cs` 将原始可读的 8-bit 灰度图 Bake 为统一数据：

- `detailHeight`：保留原图局部变化，主要用于台阶和突变检测；
- `surfaceHeight`：经过以米为单位的高斯平滑，主要用于脚下平面拟合、坡度、移动与等高线；
- 运行时 `RFloat` 表面高度纹理：供动态等高线 Shader 读取；
- 所有地图采样按真实米制坐标双线性插值。

这套结构仍可使用原来的 8-bit 图，但通过物理尺度平滑、足底多点采样与平面拟合，大幅减少“单像素灰度变化被误判成巨大真实坡度”的情况。

### 6.3 动态等高线

动态等高线由 `DynamicHeightContours.shader` 绘制，并读取和移动系统相同的 baked surface height。

当前视觉规则不是按玩家高度上下区分，而是按**当前镜头内的绝对高度范围**：

- 镜头内最低的等高线最细、约 15% 不透明度；
- 镜头内最高的等高线最粗、100% 不透明度；
- 中间高度连续插值；
- 每次相机渲染前对视口采样，更新当前可见最低/最高高度。

`MapTestController.prefab` 对 Shader 有序列化直接引用，同时保留 `Shader.Find` 回退。直接引用不能删除，否则 Build 时 Shader 可能被裁剪，导致整张黑色地图看不见。

## 7. 坡度与通行性判定

核心类：

```text
Assets/Scripts/MapTest/HeightMapTraversalEvaluator.cs
Assets/Resources/Traversal/HeightMapTraversalEvaluator.prefab
```

### 7.1 机器人物理接触尺寸

当前主要参数：

| 参数 | 当前值 | 作用 |
|---|---:|---|
| Footprint Length | 2.0 m | 机器人前后接地区域长度 |
| Footprint Width | 1.5 m | 机器人左右接地区域宽度 |
| Longitudinal Samples | 7 | 足底前后采样数 |
| Lateral Samples | 7 | 足底左右采样数 |
| Movement Probe Distance | 6.0 m | 前方路线预判距离 |
| Path Sample Spacing | 1.0 m | 路径连续检查间距 |
| Hard Stop Probe | 2.0 m | 高台阶等硬阻挡的近距离检查 |

物理尺寸决定的是机器人如何平均脚下地形，不是画面中 Sprite 的直径。更大的足底会忽略更小的噪点，但可能跨过窄沟；更小的足底更敏感，也更容易被 8-bit 局部跳变影响。

### 7.2 坡面拟合

系统在机器人足底矩形内对 `surfaceHeight` 多点采样，并拟合局部平面。由平面梯度得到：

- 当前方向的有符号坡度；
- 最大表面坡度；
- 真实下坡方向；
- 表面粗糙度/拟合残差。

这比只比较玩家前后两个像素更符合机器人真实接触尺度，也不会因为玩家处在同一根等高线包围的区域内，就错误地认为内部完全平坦。

### 7.3 台阶检测

台阶从 `detailHeight` 单独检测，主要参数：

| 参数 | 当前值 |
|---|---:|
| Max Step Height | 0.65 m |
| Step Probe Spacing | 0.5 m |
| Lateral Step Samples | 3 |

台阶不等于坡度。系统会在横向多个点采样并使用稳健统计，寻找短距离的高度突变。超过阈值的高台阶仍然硬停止玩家。

### 7.4 三档坡度

当前阈值：

| 等级 | 范围 | 设计表现 |
|---|---|---|
| Level 1 | ≤ 12° 上坡 | 正常移动，可以静止停住 |
| Level 2 | 12°–38° | 最大速度降低，费力爬坡；不输入时会自然后滑 |
| Level 3 | ≥ 38° | 进入抓地失败流程，前进受限、向下滑、左右偏移并失去部分/全部转向控制 |
| 危险下坡 | > 55° 且满足持续长度 | 继续作为安全硬停止 |

危险下坡最小持续长度为 1.5 m，避免一个孤立采样点触发刹停。

### 7.5 判定输出

`TraversalResult` 不只是布尔值，还包含：

```text
passable / requiresHardStop
uphillLevel
signedSlopeAngle
maxUphill / maxDownhill / maxSurfaceSlope
maxStepHeight
surfaceRoughness
downhillDirection
blockReason
```

移动和两套通行性 UI 都必须调用这个 Evaluator，不要复制阈值。

## 8. 玩家移动

核心类和 Prefab：

```text
Assets/Scripts/RobotMap/RobotMover.cs
Assets/Resources/Robot/RobotMarker.prefab
```

### 8.1 基础速度

当前序列化值：

| 参数 | 当前值 |
|---|---:|
| Overall Motion Scale | 0.88 |
| Forward Speed | 4.2 |
| Reverse Speed | 3.0 |
| Turn Speed | 60°/s |
| Turn Acceleration | 60 |
| Turn Deceleration | 240 |
| Launch Acceleration | 2.2 |
| Running Acceleration | 4.8 |
| Coast Deceleration | 5.0 |
| Brake Deceleration | 8.0 |

`Overall Motion Scale` 是全局运动倍率；不要忘记许多局部效果会在此基础上再乘坡度、重心和失败状态倍率。

### 8.2 Level 2

```text
最小最高速度倍率：0.55
最大自然后滑比例：0.18
滑动加速度：2.2
真实下坡方向权重：0.30
横向滑动保留：0.20
横向抓地恢复加速度：12
```

### 8.3 Level 3 与失败状态机

三级坡不直接变成不可穿越墙。系统使用：

```text
Grip（抓地）0.35s
→ Strain（挣扎）0.45s
→ Slip（失败滑落）0.45s
```

当前 Level 3 主要参数：

```text
前进倍率 0.50
下滑比例 0.55
真实下坡方向权重 0.85
滑动加速度 10
横向漂移比例 0.65
初始横向漂移 0.30
漂移方向间隔 0.55s
平滑时间 0.12s
失控旋转速度 90°/s
```

进入失败流程后会暂时锁定玩家转向，制造“爬不上去”而不是“撞到隐形墙”的感觉。滑动结束后触发短时下坡朝向恢复：

```text
持续时间 0.45s
视觉对齐速度 90°/s
横向阻尼 18
地形速度恢复 4
```

滑动的速度方向会快速趋向真实下坡，Indicator 的视觉朝向可以较慢转动，以减少横向漂移感又保留重量感。

### 8.4 下坡加速

```text
4° 开始提供下坡加速
35° 达到完整效果
最大速度倍率 1.5
额外加速度 3
```

### 8.5 仍会硬停止的情况

- 超出地图边界；
- 超过最大台阶高度；
- 超过安全阈值并持续足够距离的危险下坡。

普通 Level 3 上坡本身不应触发硬停止。如果再次出现“三级坡像墙”的问题，优先检查 `RobotMover` 的最终位移二次安全检查是否错误地把普通陡上坡当成硬阻挡。

## 9. 输入与手柄适配

项目当前使用 Legacy Input Manager，并由：

```text
Assets/Scripts/RobotMap/AdaptiveLegacyGamepadInput.cs
```

每 0.75 秒读取 `Input.GetJoystickNames()`，自动识别 Xbox、Sony 或 Generic 手柄。

主要轴：

```text
Gamepad Move
Gamepad Turn
Gamepad Trigger Throttle
Gamepad Balance Horizontal
Gamepad Balance Vertical
Gamepad Sony Balance Horizontal
Gamepad Sony Balance Vertical
Gamepad Sony Left Trigger
Gamepad Sony Right Trigger
```

如果轴缺失，代码会捕获异常并输出一次警告。编辑器菜单：

```text
Animal Game/Repair Gamepad Input Axes
```

可修复所需轴配置。换电脑后不要只复制 `Assets`，必须保留并提交 `ProjectSettings/InputManager.asset`。

当前两种移动模式由 `RobotMarker.prefab` 上的 bool 选择：

1. 左摇杆同时控制移动方向和转向；
2. 左摇杆横向只控制转向，右扳机前进、左扳机后退。

当前触发器模式为第二种，且默认含义为 **RT 前进、LT 后退**。

重心调整输入：

- 手柄右摇杆；
- 键盘方向键；
- 最大人为反向修正能力为支持半径的 60%，玩家不能仅凭输入把重心推到 100% 外缘。

扫描输入：

- 键盘 `E`；
- Xbox/Sony 的 `LB`；
- Debug 通行性覆盖层开关为 `Q`。

## 10. 重心平衡系统

核心类：

```text
Assets/Scripts/RobotMap/RobotBalanceController.cs
Assets/Scripts/RobotMap/RobotBalanceView.cs
```

### 10.1 逻辑

目标重心偏移由三部分组成：

```text
坡面重力影响
+ 速度变化产生的惯性影响
+ 玩家右摇杆/方向键的反向修正
```

控制器使用带阻尼弹簧平滑重心，并在接近支撑范围边缘时增加阻力。当前主要值：

| 参数 | 当前值 |
|---|---:|
| Center Of Mass Height | 0.9 m |
| Usable Support | 0.98 |
| Slope Influence | 0.65 |
| Full Acceleration | 8 |
| Inertia Influence | 0.08 |
| Acceleration Smoothing | 0.22 |
| Max Measured Acceleration | 30 |
| Max Counterbalance | 0.60 |
| Spring Frequency | 2 |
| Damping Ratio | 1.3 |
| Max Normalized Offset | 1.2 |
| Edge Resistance Starts | 0.72 |
| Edge Resistance Strength | 1.8 |

严重失衡会降低移动控制权：最小 Drive Authority 0.68、最小 Steering Authority 0.45。

手动按 1–6 制造左右失衡的旧实验已经取消，不要恢复。重心超出范围的“侧翻”只保留了状态表达方向，**真正的侧翻/失败系统尚未实装**。

### 10.2 视觉

重心 UI 包含：

- 玩家中心到重心点的连接线；
- 支撑/可控范围圈；
- 重心点的位置、大小和透明度。

当前 Prefab 数值是权威值。当前中心点 Alpha 已被调得很低（约 0.084），并不等于早期讨论中的 35%；外缘点 Alpha 为 0.80。Guide Alpha 从约 0.015 增至 0.35。范围圈直径 150 px，点最大移动半径 75 px。

如果要恢复“中心点 35%、圈线 8%”的旧设想，请在 `RobotBalanceView` Prefab 上明确调整，不要假定代码默认值仍是旧值。

### 10.3 镜头目标

`RobotCameraFollow` 必须调用 `FollowBalanceTarget(balance)`。跟踪目标是跟随玩家移动的重心目标；支撑圈外缘时镜头偏移倍率约 1.25。镜头不再使用早期按键实验那套人为左右倾斜。

## 11. 玩家视觉

`RobotMarkerView` 使用：

- `Arts` 中的 `robot_body` 作为外圈；
- `robot_body_fill` 作为其下方填充，颜色跟随游戏背景；
- `Indicator` 作为方向指示；
- Fill 必须位于轮廓之下但在等高线之上，以遮挡穿过玩家内部的等高线。

当前玩家视觉保持恒定屏幕尺寸：目标约 45 px。实现会按相机 `orthographicSize` 与 `pixelHeight` 缩放整个 Marker 根节点，因此 4K Build 和编辑器窗口中的身体、填充、Indicator 比例一致。

`keepMarkerSizeConstantOnScreen` 为关闭时才退回固定世界尺寸。当前移动前后视觉晃动（Drive Bob）已通过 bool 关闭，移动拖尾也关闭。

## 12. 高度变化、腾空与镜头冲击

### 12.1 `RobotHeightMotionDetector`

这是高度场上的虚拟腾空检测，不依赖真正 3D Rigidbody。它比较玩家平面运动、地面高度变化与虚拟垂直速度，推断：

- 地面是否在脚下快速下降；
- 是否短暂腾空；
- 最近一段时间是否腾空过；
- 落地时的冲击速度与归一化强度。

关键值：

```text
Virtual Gravity 9.81
Takeoff Planar Speed 0.65
Ground Fall-away 1.25
Minimum Airborne Duration 0.055s
Recent Airborne Memory 0.6s
Minimum Landing Impact 0.45
Full Landing Impact 3.6
```

### 12.2 `RobotCameraShake`

镜头冲击是带方向的弹簧响应，不是纯随机噪声。来源包括：

- 普通移动（刻意保持很弱）；
- Level 2/3 坡面；
- 重心不稳（刻意比平时移动更强）；
- 高台阶、危险下坡和 Level 3 滑落；
- 腾空与落地；
- 急减速。

Prefab 当前全局上限约为位置 0.28、旋转 4°、Zoom 0.03。落地冲击和严重重心偏移应明显强于普通行驶。

### 12.3 手柄震动

手柄震动与镜头冲击共用事件强度，但 Sony 有独立校准，不能直接套 Xbox 数值：

```text
Sony Low Multiplier 0.30
Sony High Multiplier 0.20
Sony Response Exponent 1.35
Sony Low Cap 0.42
Sony High Cap 0.28
```

这是为了保留 Xbox 当前手感，同时避免 Sony 手柄马达明显过强。重心偏移超过 65% 时会持续震动；当前严重失衡持续震动基准约 Low 0.72 / High 0.46，再经过 Sony 独立校准。

## 13. 扫描系统

核心类和资源：

```text
Assets/Scripts/RobotMap/ScanChargeUI.cs
Assets/Animation/Scan UI/Scan_Idle.anim
Assets/Animation/Scan UI/Scan_Hold.anim
Assets/Animation/Scan UI/Scan_Release.anim
Assets/Resources/UI/MainUI.prefab
```

### 13.1 输入和动画状态

```text
Idle：平时播放 Scan_Idle
Hold：按住 E/LB 开始蓄力，播放 Scan_Hold
Charged：达到最大蓄力，继续按住会触发轻微镜头抖动
Release：蓄满后松开，播放 Scan_Release 并触发正式扫描事件
```

当前最大蓄力时间约 0.20 秒，Release 动画目标时长约 0.25 秒。只有蓄满后松开才触发 `FullyChargedScanReleased`。

### 13.2 扫描圈

Hold 阶段原本的黄色收束圈已经取消。Release 阶段从固定的**玩家 UI 中心**生成圆环并扩散到 Main UI 圆形边界，不以世界玩家 Transform 为中心。

当前扫描圈扩张时间约 0.65 秒，目标 UI 半径约 430 px，起始玩家半径约 43 px，线宽约 2 px，颜色在 `MainUI.prefab` 中可调。

### 13.3 扫描镜头效果

- Hold：从基础 Orthographic Size 9 平滑缩小到约 8.5；
- Release 第一段：平滑扩大到约 9.5；
- Release 第二段：恢复到基础 9；
- 当前 Zoom Out / Return 时长约 0.45 / 0.55 秒；
- 达到满蓄力后继续按住，会有持续但可控的 Charged Shake。

## 14. 两套可通行性显示

### 14.1 Debug：`TraversalOverlayUI`

用途是测试全局/局部判定，不参与正式扫描玩法。特征：

- `Q` 开关；
- 屏幕空间固定行列网格；
- 固定角度；
- 中心 Exclusion 区域不计算也不绘制；
- 使用与玩家相同的 `HeightMapTraversalEvaluator`；
- 当前 Prefab 约为间距 4 px、中心排除 25 px、显示半径 25 m、最大可见 120、图标 20 px、刷新 10 Hz。

Debug 层必须继续独立封装，不能影响正式扫描的生命周期、缓存或刷新。

### 14.2 正式扫描：`TraversalScanOverlayUI`

正式扫描仅响应 `FullyChargedScanReleased`。

当前主要流程：

1. 新扫描立即清除上一次尚未过期的扫描标记；
2. 在 Main UI 圆内建立稳定屏幕网格，中心 Exclusion 区域完全不计算；
3. 使用 `ContourRegionIndex` 判断玩家当前所处的闭合等高线区域；
4. 选择靠近 UI 圈内等高线交界两侧的候选点；
5. 在玩家当前闭合区域内补充局部不可通行点，并覆盖其周围 N 米邻域；
6. 随 Release 扫描波逐步显示候选标记；
7. 将这些标记记录为地图绝对位置，持续显示约 3.5 秒；
8. 持续按玩家实时位置与能力重新评估，状态变化时立即更换图标并可播放一次淡出/淡入。

当前主要参数：

| 参数 | 当前值 |
|---|---:|
| Grid Spacing | 56 px |
| Contour Boundary Half Width | 5 m |
| Terrain Gradient Probe | 1.5 m |
| Center Exclusion | 40 px |
| Maximum Signs | 120 |
| Unpassable Neighborhood | 8 m |
| Display Lifetime | 3.5 s |
| Periodic Refresh | 0.75 s |
| Realtime Move Threshold | 0.35 m |
| Realtime Interval | 0.08 s |
| Icon Size | 16 px |
| Canvas Order | 21 |

周期刷新呼吸效果有独立 bool，当前关闭。状态发生改变时的视觉刷新有独立 bool，当前开启。

### 14.3 `ContourRegionIndex`

该类按每个等高线高度分别建立高地侧和低地侧的连通分量，排除接触地图边缘的非闭合区域，并返回玩家当前闭合区域的 Handle、面积和边界高度。

不要简单地只找“最近闭合线”，因为嵌套山丘、盆地和地图边缘区域需要区分内外侧和连通性。

### 14.4 性能优化

正式扫描曾因大量图标生成/销毁与集中计算发生明显掉帧，现已改为：

- 扫描候选点分帧计算；
- 刷新有每帧数量与毫秒预算；
- 中心和显示范围外候选不参与计算；
- 复用数据容器；
- `TraversalSignsGraphic` 将所有 Passable 图标合成一个 UI Mesh，Unpassable 图标合成另一个 UI Mesh；
- 生命周期结束时清空批量 Mesh，而不是逐个销毁数百 GameObject。

当前预算大致为：扫描 32 个/帧、1.75 ms；周期刷新 16 个/帧、1.25 ms；实时变化检测 12 个/帧、1 ms。

## 15. 构建版问题与已修复事项

### 15.1 构建后地图全黑

原因不是高度图丢失，而是动态等高线 Shader 过去只通过 `Shader.Find` 获取，被 Unity Build 裁剪。地图背景和高度色全为黑色，Shader 丢失后自然整张地图不可见。

修复：在 `MapTestSceneController` 中增加序列化 Shader 字段，并在 `MapTestController.prefab` 直接引用 `AnimalGame/Dynamic Height Contours`。不要删除该引用。

### 15.2 构建后玩家大小不对

原因是玩家原先按世界单位显示，4K 或不同窗口高度下视觉像素尺寸变化。现已用恒定屏幕像素缩放整个 Marker 根节点修复，保持身体、Fill 与 Indicator 一致。

### 15.3 检查日志

Windows 构建日志通常位于：

```text
%USERPROFILE%\AppData\LocalLow\DefaultCompany\AnimalGame\Player.log
```

遇到黑屏、资源缺失、输入轴错误或初始化失败时，应先检查该日志，不要只根据画面猜测。

## 16. 重要文件索引

| 功能 | 文件 |
|---|---|
| 主场景运行时装配 | `Assets/Scripts/MapTest/HeightMapPlayerSceneBootstrap.cs` |
| 高度场 Bake | `Assets/Scripts/MapTest/BakedHeightField.cs` |
| 地图与等高线参数 | `Assets/Scripts/MapTest/MapTestSceneController.cs` |
| 坡度/台阶/通行性 | `Assets/Scripts/MapTest/HeightMapTraversalEvaluator.cs` |
| 闭合等高线区域 | `Assets/Scripts/MapTest/ContourRegionIndex.cs` |
| Debug 通行 UI | `Assets/Scripts/MapTest/TraversalOverlayUI.cs` |
| 正式扫描通行 UI | `Assets/Scripts/MapTest/TraversalScanOverlayUI.cs` |
| 批量标志 Mesh | `Assets/Scripts/MapTest/TraversalSignsGraphic.cs` |
| 玩家移动 | `Assets/Scripts/RobotMap/RobotMover.cs` |
| 玩家视觉 | `Assets/Scripts/RobotMap/RobotMarkerView.cs` |
| 重心逻辑 | `Assets/Scripts/RobotMap/RobotBalanceController.cs` |
| 重心 UI | `Assets/Scripts/RobotMap/RobotBalanceView.cs` |
| 镜头跟随 | `Assets/Scripts/RobotMap/RobotCameraFollow.cs` |
| 腾空/落地检测 | `Assets/Scripts/RobotMap/RobotHeightMotionDetector.cs` |
| 镜头和手柄冲击 | `Assets/Scripts/RobotMap/RobotCameraShake.cs` |
| 扫描蓄力与 UI | `Assets/Scripts/RobotMap/ScanChargeUI.cs` |
| Xbox/Sony 输入适配 | `Assets/Scripts/RobotMap/AdaptiveLegacyGamepadInput.cs` |

## 17. Resources Prefab 索引

```text
Assets/Resources/Camera/RobotCamera.prefab
Assets/Resources/MapTest/MapTestController.prefab
Assets/Resources/Robot/RobotMarker.prefab
Assets/Resources/Traversal/HeightMapTraversalEvaluator.prefab
Assets/Resources/Traversal/TraversalOverlay.prefab
Assets/Resources/Traversal/TraversalScanOverlay.prefab
Assets/Resources/UI/MainUI.prefab
```

这些不是可随意替换的示例 Prefab，而是主场景 Bootstrap 的运行时来源。修改场景里临时实例而不 Apply 到这些 Resources Prefab，重开或 Build 后不会生效。

## 18. 当前未完成或需要继续验证的内容

- 真正的侧翻/摔倒失败系统尚未实装；
- 动物追踪、拍照、上传和奖励循环尚未成为当前 Demo 主系统；
- 太空垃圾、清理与生态恢复尚待系统化接入；
- 钩锁、爬墙、喷气背包仍属于长期移动能力方向；
- Sony 手柄轴与震动虽然有适配和独立校准，仍应在不同 DualShock/DualSense、连接方式和驱动环境上实机验证；
- 8-bit 高度源经过统一 Bake 已显著改善坡度，但参数仍需结合最终机器人尺寸和关卡尺度调试；
- 正式扫描 UI 的候选规则、标志密度与生命周期仍是玩法调优点；
- 若修改地图尺寸、高度范围或平滑半径，必须重新验证坡度阈值，而不能只看等高线外观。

## 19. 换电脑后的恢复步骤

1. 克隆整个 Git 仓库，不要只复制 Unity 工程的 `Assets`；
2. 安装 Unity Hub 和 `2022.3.16f1c1`；
3. 用 Hub 打开内层 `AnimalGame` Unity 工程目录；
4. 等待 Package 与 Library 重新导入完成；
5. 确认 `HeightMapPlayerScene.unity` 是唯一启用的 Build Scene；
6. 检查 Console 是否有脚本或 Input Axis 错误；
7. 打开 `HeightMapPlayerScene`，Play 测试地图、玩家大小、坡度、重心和扫描；
8. 分别测试键鼠、Xbox 和 Sony 手柄；
9. 做一次 Development Build，并检查 `Player.log`；
10. 修改前运行 `git status --short`，避免覆盖未提交的 Prefab 或场景更改。

建议首次验收清单：

```text
[ ] 地图和动态等高线可见
[ ] 玩家约保持 45px，内部不被等高线穿过
[ ] Level 2 会费力/后滑，Level 3 会挣扎/滑落而不是撞墙
[ ] 高台阶与危险下坡仍能硬停止
[ ] 镜头跟随重心目标
[ ] 右摇杆/方向键可修正重心
[ ] E/LB 蓄满释放扫描
[ ] 扫描图标角度固定并能实时改变通行状态
[ ] Q 只开关独立 Debug 网格
[ ] Xbox 与 Sony 震动强度分别合理
[ ] Build 中地图和玩家尺寸与编辑器一致
```

## 20. 给后续开发者/Codex 的工作方式

处理新需求前：

1. 先读本文；
2. 查看 `git status --short`，保护用户已有修改；
3. 检查相关脚本和 **Resources Prefab 的实际序列化值**；
4. 追踪数据源，不要只修表面 UI；
5. 修改代码后同时检查 Prefab/场景引用是否需要更新；
6. 至少做静态编译检查，并在高风险改动后做 Unity Batchmode 或实际 Build 验证；
7. 把新增的重要设计决定、输入映射、Prefab 参数和已知问题更新到本文。

尤其避免以下回退：

- 为通行性 UI 复制一套与玩家不同的坡度判断；
- 用等高线 Collider 直接阻挡玩家；
- 将普通 Level 3 上坡重新改成硬停止；
- 让扫描标志继承机器人或相机旋转；
- 把正式扫描和 Q Debug 网格重新混在同一个生命周期中；
- 删除动态等高线 Shader 的直接 Prefab 引用；
- 只改场景实例而忘记 Apply 到 `Resources` Prefab；
- 用 Xbox 震动数值直接驱动 Sony 手柄。

---

这份文档的目的不是代替代码，而是保存“为什么这样实现”。每次跨电脑或开启新会话时，先让新的协作者读完它，再让其从当前代码确认细节，能够最大限度保留此前迭代的意图与上下文。

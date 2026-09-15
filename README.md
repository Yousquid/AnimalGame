# AnimalGame — 程序员技术文档

> **第一部分 · 试读版**
>
> 代码与序列化配置核对日期：2026-09-14。本文描述当前工作区的实现，不将历史讨论中的设计方案视为已经实现的功能。
>
> 本次完整展开：工程入口、场景装配、地图数据、坐标换算、地形通行检测。其他系统先给出代码入口，后续再按同等深度补齐；这不是完整的全项目文档。

本文同时回答两个问题：**设计者想让玩家体验到什么，以及程序现在怎样实现这种体验。** 后文按以下口径区分信息来源：

- **设计目标 / 历史需求**：依据本任务中设计者明确提出的需求归纳，不是原话逐字引用。需求有过修改时，说明与本节相关的演变，不将早期试验要求永久固化。
- **实现取舍 / 工程解释**：根据当前代码解释一种组织方式或算法有什么作用；不把推断写成设计者当初指定的技术方案。尤其不能将“使用 Resources”“平面拟合”等实现细节冒充原始玩法需求。
- **当前实现与限制**：以源码和序列化资产为准。设计目标不等于已经完全达成；不一致之处保留说明，不因补充设计背景而擅自改代码或参数。

各功能节先交代目的，再保留原有调用关系、算法和参数解释。章节末的体验检查是后续验证建议，不是已经执行的测试结果，也不是禁止未来设计调整的固定规则。

## 阅读导航

- [1. 工程定位与运行入口](#1-工程定位与运行入口)
- [2. 文件结构与职责划分](#2-文件结构与职责划分)
- [3. 场景启动与运行时装配](#3-场景启动与运行时装配)
- [4. 地图数据与坐标体系](#4-地图数据与坐标体系)
- [5. 地形通行检测：完整实现拆解](#5-地形通行检测完整实现拆解)
- [6. 配置入口与排查示例](#6-配置入口与排查示例)
- [7. 本部分的验证范围与后续章节](#7-本部分的验证范围与后续章节)

## 1. 工程定位与运行入口

本章设计背景：项目需要在等高线、地表纹理和机器人 UI 构成的画面里，传达地势、运动与失衡，而不只是显示一张可移动的地图。历史需求包括更拟真的连续侧翻、机械臂扶正、直观区分地势高低，以及在正式地图中延续测试地图的操控和镜头感觉。因此，接手代码首先要分清“地图上的物理数据”和“帮助玩家理解这些数据的视觉表现”。

### 1.1 先建立正确的技术模型

**设计目标：视觉调整不应意外改变已有玩法。** 例如设计者更换较小的机身贴图时，明确希望不影响其他玩家系统；调整侧翻期间重心指示点的视觉距离时，也专门确认过是否只影响显示、不增加实际侧翻难度。程序需要能解释哪一层决定外观、哪一层决定运动和危险，而不是默认把它们一起缩放。

当前可玩地图使用二维平面上的机器人、地图 Sprite、UI 和独立高度数据。**地图海拔是采样得到的逻辑数据，不是机器人 Transform 的 Z 坐标，也不是通过 Unity Terrain 碰撞得到的高度。**

正常驾驶由 `RobotMover.Update()` 读取输入、查询地形、计算速度并直接修改 Transform。地形通行能力由自定义的 `HeightMapTraversalEvaluator` 计算；树干等障碍物使用自定义圆形足迹扫描。排查“为什么走不动”时，入口应是这些脚本，而不是首先寻找 Rigidbody、TerrainCollider 或 NavMesh 参数。

还需要区分三个层次：

1. **配置与编辑数据**：地图 `ScriptableObject`、物种配置、Prefab 和场景中保存的属性。
2. **运行时计算数据**：高度数组、通行检测结果、机器人速度和各系统状态。
3. **表现**：Sprite、Shader、UI、镜头跟随和震动。表现尺寸不必等于逻辑碰撞尺寸。

项目没有一个集中定义所有游戏行为的总控制器。入口负责装配组件，随后各组件通过 Unity 生命周期、显式初始化和彼此引用协作。

### 1.2 打开哪个文件夹

**本节用途：复现同一个工程环境。** 这是程序员交接信息，不对应独立玩法需求；保留它是为了避免用错误工程目录或不同依赖环境来判断设计效果。

Git 仓库根目录与 Unity 工程根目录相差一层：

```text
AnimalGame/                  ← Git 仓库根目录，本 README 所在位置
└── AnimalGame/              ← Unity Hub 应打开的工程目录
    ├── Assets/
    ├── Packages/
    └── ProjectSettings/
```

当前工程记录的 Unity 版本是 **2022.3.16f1**。版本依据是 [ProjectVersion.txt](AnimalGame/ProjectSettings/ProjectVersion.txt)，不是本机恰好安装的版本。

主要包声明见 [manifest.json](AnimalGame/Packages/manifest.json)：

| 包 | 当前声明版本 | 阅读代码时的意义 |
| --- | --- | --- |
| Universal RP | 14.0.9 | 地图、动物和 UI 相关 Shader 的渲染环境 |
| Input System | 1.6.1 | 工程安装了该包，但不能因此推断所有操作都经 Input Action Asset 读取 |
| uGUI | 1.0.0 | Canvas 和界面组件 |
| TextMeshPro | 3.0.6 | 文本相关依赖 |
| Unity Test Framework | 1.1.33 | 已声明测试工具包；不代表本文已经执行了自动化测试 |

机器人输入代码中仍直接使用 `KeyCode` 和 `AdaptiveLegacyGamepadInput`。研究按键映射、死区或手柄兼容性时，应沿实际调用查找，不要只查看已安装的 Input System 包。

### 1.3 场景选择

**设计目标：新增正式地图，同时延续已有测试体验。** 落基山脉地图的初始要求是长宽各为测试图的 3 倍、面积为 9 倍，并保持相近的角色、控制、镜头体验（3C）。不同地图不意味着重新定义一套玩家玩法。下面列出现有入口；它们是否已满足相同体验，还需结合实际尺度和参数判断，不能仅凭复用脚本得出结论。

| 场景 | 入口与用途 | 当前 Build Settings |
| --- | --- | --- |
| [HeightMapPlayerScene](AnimalGame/Assets/Scenes/HeightMapPlayerScene.unity) | 测试地图的可玩场景，使用 `HeightMapPlayerSceneBootstrap` | 已启用，第一个启用场景 |
| [RockyMountainPlayerScene](AnimalGame/Assets/Scenes/RockyMountainPlayerScene.unity) | 落基山脉地图的可玩场景，复用同一 Bootstrap | 已启用，第二个启用场景 |
| [MapTestScene](AnimalGame/Assets/Scenes/MapTestScene.unity) | 地图展示入口 `MapTestSceneBootstrap`，不是完整玩家装配流程 | 未启用 |
| [SampleScene](AnimalGame/Assets/Scenes/SampleScene.unity) | 早期演示入口 `RobotMapBootstrap` / `RobotMapDemo` | 未启用 |
| [AnimalPlacementTestScene](AnimalGame/Assets/Scenes/AnimalPlacementTestScene.unity) | 动物摆放测试场景 | 未列入当前构建列表 |

构建列表来源：[EditorBuildSettings.asset](AnimalGame/ProjectSettings/EditorBuildSettings.asset)。要在 Editor 检查正式地图，直接打开 `RockyMountainPlayerScene` 后进入 Play Mode；构建时的首场景则由启用场景顺序决定，两者不是同一概念。

## 2. 文件结构与职责划分

本章设计背景：设计者希望地图能持久保存、在 Editor 中摆放树木等 Prefab，并多次要求把机械臂长度、扶正容错与输入频率等参数暴露出来供手动调整。接手者需要知道“去哪改内容”，不能每次调效果都从驱动代码重新找起。

实现取舍：下面的目录划分把地图 / 物种配置、场景摆放、运行时行为和编辑器工具分别作为阅读入口。**这是现有工程如何承接可编辑需求的解释，不代表这些目录名本身是设计要求。**

以下是阅读入口图，不穷举 `.meta`、生成缓存或全部美术文件。

```text
仓库根目录/
├── README.md                              本技术文档
├── ANIMALGAME_HANDOFF.md                   历史交接资料，按需查阅
└── AnimalGame/                            Unity 工程
    ├── Assets/
    │   ├── Scripts/
    │   │   ├── MapTest/                    地图、高度采样、通行、摆放、植被
    │   │   ├── RobotMap/                   玩家驾驶、平衡、侧翻、机械臂、扫描、拍照
    │   │   ├── Animals/                    动物通用组件及物种行为
    │   │   ├── Discovery/                  可发现实体相关代码
    │   │   └── Rendering/                  玩家 UI 范围等共用显示逻辑
    │   ├── Editor/                        Inspector、Scene 工具、画笔、烘焙、资源生成器
    │   ├── Maps/                          地图资产与高度源图
    │   │   └── Generated/                 已生成的地图相关资源
    │   ├── Prefabs/
    │   │   ├── Resources/                 Bootstrap 按字符串路径加载的核心 Prefab
    │   │   │   ├── MapTest/
    │   │   │   ├── Robot/
    │   │   │   ├── Camera/
    │   │   │   ├── UI/
    │   │   │   └── Traversal/
    │   │   ├── Animals/                   动物实体 Prefab
    │   │   └── Environment/               植被等环境物件
    │   ├── Data/Animals/                  MuskratConfig、PileatedWoodpeckerConfig
    │   ├── Arts/                          美术素材
    │   ├── Materials/
    │   ├── Shaders/
    │   └── Scenes/
    ├── Packages/
    ├── ProjectSettings/
    └── Tools/HeightMaps/                   彩色高度图转换等离线工具
```

`Library`、`Temp`、`Logs`、`obj` 不属于需要逐文件理解的游戏源码。Unity 生成的 `.sln` / `.csproj` 也不是组件和资源配置的权威来源；资源引用主要保存在 `.unity`、`.prefab`、`.asset` 及其 `.meta` GUID 中。

### 2.1 核心模块阅读入口

**本节用途：让修改范围与设计问题对应。** 例如“树冠应该遮住机器人”是显示问题，“树干不能穿过”是实体阻挡问题；“重心点显得更靠外”也不自动等同于“重心更容易越界”。模块入口用于帮助程序员沿正确的数据链修改，避免为了一个视觉需求误改共用的物理参数。

| 模块 | 主要入口 | 责任边界 |
| --- | --- | --- |
| 可玩场景装配 | [HeightMapPlayerSceneBootstrap.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapPlayerSceneBootstrap.cs) | 创建核心对象、注入引用、设置出生点 |
| 地图运行时 | [MapTestSceneController.cs](AnimalGame/Assets/Scripts/MapTest/MapTestSceneController.cs) | 读取地图资产、生成高度场和地图显示、提供采样与换算 |
| 地图持久数据 | [HeightMapLevelAsset.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapLevelAsset.cs) | 地图尺度、高度参数、材质类型、绘制数据、水深和烘焙资源引用 |
| 高度场 | [BakedHeightField.cs](AnimalGame/Assets/Scripts/MapTest/BakedHeightField.cs) | 灰度解码、重采样、平滑、掩码和高度查询 |
| 地形判定 | [HeightMapTraversalEvaluator.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapTraversalEvaluator.cs) | 支撑面拟合、台阶与下坡检测、障碍物扫描 |
| 正常驾驶 | [RobotMover.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMover.cs) | 输入、转向、速度、地形运动响应、正常移动控制权 |
| 重心与侧翻 | [RobotBalanceController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotBalanceController.cs)、[RobotTumbleController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotTumbleController.cs) | 平衡状态、侧翻过程；后续章节展开 |
| 机械臂与扶正 | [RobotArmController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotArmController.cs)、[RobotSelfRightingController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotSelfRightingController.cs) | 机械臂操控和扶正流程；后续章节展开 |
| 扫描 | [ScanChargeUI.cs](AnimalGame/Assets/Scripts/RobotMap/ScanChargeUI.cs)、[BioScanController.cs](AnimalGame/Assets/Scripts/RobotMap/BioScanController.cs) | 扫描界面、范围与生物扫描衔接 |
| 拍照 | [PhotoModeController.cs](AnimalGame/Assets/Scripts/RobotMap/PhotoModeController.cs)、[PhotoModeUI.cs](AnimalGame/Assets/Scripts/RobotMap/PhotoModeUI.cs)、[PhotoResultUI.cs](AnimalGame/Assets/Scripts/RobotMap/PhotoResultUI.cs) | 模式和流程、取景显示、结果显示分开 |
| 动物 | [AnimalAgent.cs](AnimalGame/Assets/Scripts/Animals/AnimalAgent.cs)、[AnimalSpeciesConfig.cs](AnimalGame/Assets/Scripts/Animals/AnimalSpeciesConfig.cs) | 通用动物入口及物种配置；另有独立物种行为脚本 |
| 环境物件 | [HeightMapPlacedObject.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapPlacedObject.cs)、[HeightMapObstacleFootprint.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapObstacleFootprint.cs) | 地图位置记录与实体阻挡分开 |
| 地图编辑工具 | [HeightMapSurfacePainterEditor.cs](AnimalGame/Assets/Editor/HeightMapSurfacePainterEditor.cs)、[HeightMapStaticWaterPainterEditor.cs](AnimalGame/Assets/Editor/HeightMapStaticWaterPainterEditor.cs) | 地表类型与静态水深的编辑、烘焙 |

目录名中的 `MapTest` 和 `RobotMap` 是沿用的历史命名，不能据此认为其中代码只用于测试场景。例如正式落基山脉地图也使用 `MapTestSceneController`。

另外，**一个 `.cs` 文件不一定只有一个类**。`RobotTumbleUiRotation` 在 `HeightMapPlayerSceneBootstrap.cs` 中；`TerrainSurfaceDefinition` 在 `HeightMapLevelAsset.cs` 中；地表画笔文件中也包含 Inspector、Window 和 Baker 等多个类。找类型时应使用全局符号搜索。

## 3. 场景启动与运行时装配

本章设计目标：固定地图、场景中保存的摆放内容和出生点，需要与共用的机器人、相机、扫描、拍照等功能一起组成可玩的关卡。换地图时，关卡数据可以变，但不应因为漏放一个组件就丢失已有玩家能力。

当前实现选择了 Bootstrap 装配和 Resources Prefab 复用。设计要求是“新地图继续使用已有功能”，并不是指定必须在运行时动态创建这些对象；后续若更换装配方式，应以功能和配置是否完整保留来判断，而非把当前加载机制当作玩法的一部分。

### 3.1 地图先准备，玩家后装配

**实现取舍：先有地形，再解释玩家位置与状态。** 出生点是地图米坐标，通行和平衡也要读高度；让它们在地图未就绪时启动，可能导致错误出生位置或空数据被误当作平地。下面的初始化顺序是为这些依赖提供有效数据，而非为了改变玩家看到的入场节奏。

`MapTestSceneController` 带有 `[ExecuteAlways]` 和 `[DefaultExecutionOrder(-1000)]`：

- `[ExecuteAlways]` 让地图在编辑状态也能生成预览。
- `-1000` 让场景中已有的地图组件先于默认顺序的玩家 Bootstrap 执行 `Awake()`。
- Bootstrap 如未找到地图，则从 Resources 实例化地图 Prefab；实例化期间会执行地图组件的初始化。

简化后的启动链：

```text
MapTestSceneController.Awake
└── RebuildGeneratedMap
    ├── ApplyFixedLevelAsset        从地图资产复制有效配置
    ├── ReleaseGeneratedMap        释放上一次生成的资源
    ├── BakePhysicalHeightField    建立 CPU 高度数组及配套纹理
    ├── CreateCamera               仅运行时；创建临时地图相机
    └── CreateHeightVisualization  建立 Sprite / 材质

HeightMapPlayerSceneBootstrap.Awake
├── 复用地图，或实例化地图 Prefab
├── 实例化机器人、玩家相机、通行检测器、Overlay、Main UI
├── 检查必需组件，补充部分可回退创建的组件
├── 将地图米坐标出生点转换为世界坐标
├── 切换地图使用的相机，设置相机跟随目标
└── Initialize / Set...             连接地图、驾驶、平衡、扫描、拍照与 UI
```

`map.UseCamera(camera)` 会停用不同于玩家相机的原地图相机。这里存在先生成地图相机、再由可玩场景切换到玩家相机的过程；不要把初始化中出现过两个相机对象直接解释为两个相机一直同时渲染。

### 3.2 Resources 路径是装配契约

**实现取舍：复用同一组玩家功能与调参资产。** 同一套 Prefab 有助于让测试地图和正式地图继承相同的玩家能力。但共享也意味着修改会影响多个场景；修正某张高度图的数据问题，不应顺手修改共用的驾驶能力来掩盖它。

以下路径相对于 `Assets/Prefabs/Resources/`，加载时不带 `.prefab` 后缀：

| Resources key | 对应资产 | 主要组件要求 |
| --- | --- | --- |
| `MapTest/MapTestController` | [MapTestController.prefab](AnimalGame/Assets/Prefabs/Resources/MapTest/MapTestController.prefab) | `MapTestSceneController` |
| `Robot/RobotMarker` | [RobotMarker.prefab](AnimalGame/Assets/Prefabs/Resources/Robot/RobotMarker.prefab) | `RobotMover`，以及玩家功能组件 |
| `Camera/RobotCamera` | [RobotCamera.prefab](AnimalGame/Assets/Prefabs/Resources/Camera/RobotCamera.prefab) | `Camera`、`RobotCameraFollow` |
| `Traversal/HeightMapTraversalEvaluator` | [HeightMapTraversalEvaluator.prefab](AnimalGame/Assets/Prefabs/Resources/Traversal/HeightMapTraversalEvaluator.prefab) | `HeightMapTraversalEvaluator` |
| `Traversal/TraversalOverlay` | [TraversalOverlay.prefab](AnimalGame/Assets/Prefabs/Resources/Traversal/TraversalOverlay.prefab) | `TraversalOverlayUI` |
| `Traversal/TraversalScanOverlay` | [TraversalScanOverlay.prefab](AnimalGame/Assets/Prefabs/Resources/Traversal/TraversalScanOverlay.prefab) | `TraversalScanOverlayUI` |
| `UI/MainUI` | [MainUI.prefab](AnimalGame/Assets/Prefabs/Resources/UI/MainUI.prefab) | 根节点 `PhotoModeUI`、子级 `ScanChargeUI` 等 |

重命名这些资源或将其移出 Resources 时，需要同时修改加载字符串。保持 `.meta` GUID 只能保护序列化引用，不能修复字符串加载路径。

Bootstrap 会为缺失的部分组件执行 `AddComponent`，例如 Balance、Tumble、Arm、BioScan、SelfRighting、CameraShake、PhotoResultUI。但它不是通用的自动修复器：机器人驾驶组件、玩家相机及跟随组件、所需 Overlay、`PhotoModeUI`、`ScanChargeUI` 和已生成地图仍有明确检查。缺失资源会报错；检查失败会停用 Bootstrap。

**编辑期场景树不等于 Play Mode 中最终的场景树。** 只查看 `.unity` 文件，可能看不到运行时才实例化的 UI、相机和玩家对象；同样，也不能因某个功能组件能自动添加，就认为其 Prefab 中的序列化调参可以省略。

### 3.3 重要的依赖连接

**设计目标：表现要能传达实际发生的行为。** 历史需求中，每次侧翻着陆、扶正落地和障碍撞击都需要相应的镜头 / 手柄反馈；地势滤镜则要帮助玩家判断当前位置与周围的高低关系。这些效果需要取得运动或地图数据，而不能只靠互不相关的 UI 动画。下表说明装配接线，不代表本章已经核验每一类反馈的强度和触发规则。

`HeightMapPlayerSceneBootstrap.Awake()` 中值得设置断点的位置：

| 调用 | 建立的关系 |
| --- | --- |
| `traversalEvaluator.Initialize(map)` | 通行检测获得地图数据与坐标转换入口 |
| `robot.SetTraversalEvaluator(traversalEvaluator)` | 驾驶获得检测器；内部继续把检测器传给平衡与侧翻组件 |
| `heightMotion.Initialize(map)` | 高度运动检测获得高度采样入口 |
| `cameraShake.Initialize(robot, balance, heightMotion)` | 镜头反馈连接玩家运动、平衡和高度变化 |
| `photoMode.InitializeCamera(cameraFollow, cameraShake)` | 拍照控制器接入相机控制与反馈 |
| `map.UseSurfaceRevealUi(scanChargeUi)` | 地图材质读取玩家 UI 的显示范围 |
| `map.UseElevationFilterTarget(robot.transform)` | 地势滤镜取得参考玩家位置 |
| `photoResultUi.Initialize(photoMode, photoModeUi, camera, map)` | 结果显示连接快门流程、取景 UI 和地图 |

目前场景入口使用 `FindObjectOfType` 寻找地图，未在该查找处按场景筛选；同时，除地图外的核心 Prefab 会直接实例化。因此这套装配方式应按“一个可玩场景中一套主玩家系统”理解，**不能直接假定它已经支持多地图 Additive 装载或重复 Bootstrap**。

### 3.4 出生点与摆放对象

**历史需求：把地图当作可持续编辑的关卡，并将出生点可视化。** 设计者希望直接在固定地图中摆放树木等 Prefab，后来也要求能看到并调整玩家出生点。目的在于直观选择测试地点和组织关卡，而不是每次运行靠猜测坐标或临时生成摆放结果。

玩家出生点保存于场景的 `HeightMapPlayerSceneBootstrap.playerSpawnMapPositionMeters`，单位是地图米，不在 `RobotMarker` 的贴图或局部 Transform 中。

对应的 [HeightMapPlayerSceneBootstrapEditor.cs](AnimalGame/Assets/Editor/HeightMapPlayerSceneBootstrapEditor.cs) 提供 Scene 视图可视化与拖动入口，并通过序列化属性写回出生点。

环境物件使用 `HeightMapPlacedObject`：编辑时移动 Transform 会捕获其地图坐标和采样海拔；`SnapToStoredMapPosition()` 则反向将保存的地图坐标转换回世界坐标。海拔仍只是附带记录，不是物件的 Z 位移。地图尺度改变后，不应假设每个已摆放对象都会自动重新投影到其历史米坐标，需要检查是否执行了对应的重新定位操作。

Bootstrap 的 `LateUpdate()` 将玩家位置限制在地图的**矩形 WorldBounds** 内。它不负责把玩家自动移到不规则可玩掩码内，也不负责替出生点寻找最近的安全站立区。

## 4. 地图数据与坐标体系

本章涉及的设计目标有明确的先后关系：先把测试地图固定保存下来并可编辑；再用地表纹理表达不同地形；随后新增更大的正式落基山脉地图，并处理高度图失真和像素台阶。它们共同要求地图数据可以持久编辑，但外观质量或资源替换不能无意中改变已调好的操控体验。

阅读本章时应把三个问题分开：**地形本身是什么、玩家看起来有多大、机器人能否通过。** 某一项改善，并不自动意味着另外两项也正确。

### 4.1 哪份数据是权威配置

**历史需求：地图不是每次游玩临时决定的结果，而是设计者能保存、继续编辑的关卡。** 地形纹理的区域归属也希望记录到地图本身。`HeightMapLevelAsset` 承接持久的地图定义，场景承接物件摆放；这不是把整张地图连同所有实例都存进一个 Prefab，而是用资产与场景配合实现类似关卡编辑器的工作方式。

两个当前主要地图资产：

- [MainHeightMapLevel.asset](AnimalGame/Assets/Maps/MainHeightMapLevel.asset)
- [RockyMountainHeightMapLevel.asset](AnimalGame/Assets/Maps/RockyMountainHeightMapLevel.asset)

它们的类型是 `HeightMapLevelAsset : ScriptableObject`，并不只是“高度图路径”。它同时保存地图物理尺度、高度范围、平滑配置、显示参数、地表种类与绘制数据、水深数据、烘焙结果引用等。

`MapTestSceneController` 中仍有同名字段，但其 Header 已标为 `Legacy Fallback`。只要绑定的 `levelAsset` 有效，`ApplyFixedLevelAsset()` 就会把资产参数复制到 Controller 中。因此：

> 正常调地图应修改当前场景绑定的 `HeightMapLevelAsset`，不要把 Controller 的旧字段当成另一个独立配置源。

这也是解释“某些值改完后又回去了”的首要检查点之一；具体问题仍应核对是否存在 Inspector、校验或初始化覆盖，而不是一律归为 Unity 保存失败。

### 4.2 当前序列化快照

**设计与现状的区别：目标比例不等于每个当前参数都永久固定。** 初始正式地图要求是物理面积 9 倍；后续又有扩大地图相对玩家的视觉尺度、调整高差的试验。本表保留当前值，供程序员复现现状，而不是根据旧需求把所有参数强行恢复。高度范围或 PPU 与早期讨论不同，应先确认改动背景。

下表取自上述两个资产，**是当前保存值，不是代码默认值，也不是历史目标值**。

| 字段 | 测试地图 | 落基山脉地图 |
| --- | --- | --- |
| 当前高度源 | `grey_height_map.png` | `Rocky_Moutain_New_GreyMap.png` |
| `mapWidthMeters × mapHeightMeters` | 250 × 250 m | 750 × 750 m |
| `minimumHeightMeters` / `maximumHeightMeters` | 0 / 70 m | 0 / 100 m |
| `bakedHeightResolution` | 2048 | 2048 |
| `normalizeSourceRange` | 开启 | 开启 |
| `surfaceSmoothingSigmaMeters` | 0.75 m | 2.25 m |
| `detailSmoothingSigmaMeters` | 0 m | 0.75 m |
| `preserveLargeStepHeightMeters` | 1.8 m | 2 m |
| `largeStepConfirmationDistanceMeters` | 1 m | 1.5 m |
| `previewResolution` | 2160 | 4096 |
| `pixelsPerUnit` | 32 | 14 |
| `contourIntervalMeters` | 8 m | 8 m |
| `playableAreaMask` | 未绑定 | 未绑定 |
| `useHeightMapBorderMask` | 关闭 | 关闭 |

注意：源图名字中的 `Moutain` 为项目现有拼写，查找资源时需要保留。`Maps/Generated` 中有新资源并不代表当前场景已绑定它；应沿场景 → `levelAsset` → `heightMap` 的实际 GUID 引用确认。

### 4.3 三套坐标不能混用

**设计目标：玩家应该感到地形空间扩大，而不是仅仅看到数字变大。** 设计者曾明确追问“是否只把长宽的物理米数乘了 3，游戏里的大小却没相应扩大”。下面拆开米、世界单位和 UV，正是为了让程序员能同时检查移动距离、画面比例和采样位置，不把改一个数字误当成整个尺度需求已经完成。

| 坐标 | 单位 / 范围 | 用途 |
| --- | --- | --- |
| 地图坐标 `mapPositionMeters` | `(0…mapWidthMeters, 0…mapHeightMeters)`，米 | 地形采样、物件记录、足迹大小、台阶高度和检测距离 |
| 世界坐标 `worldPosition` | Unity 单位，位于 XY 平面 | Transform、相机跟随、画面中的机器人移动 |
| 纹理坐标 `uv` | 通常为 `[0,1]²` | 高度数组和地图纹理采样 |

以地图 Sprite 的 `WorldBounds` 为基准，世界位置转地图坐标的核心公式为：

```text
u = (worldX - bounds.min.x) / bounds.size.x
v = (worldY - bounds.min.y) / bounds.size.y
mapX = u × mapWidthMeters
mapY = v × mapHeightMeters
```

反向映射见 `MapPositionToWorld()`。该方法会先将米坐标裁到矩形地图范围，再做线性映射；它不会检查不规则可玩掩码是否允许该点。

`TrySampleWorldPosition()` / `TrySampleMapPosition()` 则不仅采高度，也会验证边界和可玩掩码，返回 `bool`。不能将其失败时初始化为 `0` 的输出高度当成真实海拔。

一个容易误读的命名：`RobotMover.MapPosition` 实际返回 `transform.position`，即世界坐标。对新接入的逻辑，应追溯返回值实现，而非仅凭属性名判断单位。

当前换算使用 Renderer 的轴对齐 `bounds`，不是完整的逆 Transform 映射。地图根对象任意旋转后，不能假定这些方法仍然对应正确的纹理坐标；维持轴对齐地图是安全的现有使用前提。

### 4.4 地图米数与画面大小分别由什么决定

**历史需求：保持等高线代表的米数不变，让地图相对玩家更大。** 玩家希望两条等高线之间容纳更大的可活动空间，但不是通过删掉中间等高线、提高等高距来伪造稀疏效果。因此应检查世界显示尺度及其运动换算，而不是直接修改 `contourIntervalMeters`。初始正式地图还要求延续测试地图的 3C 感觉，改尺度后仍需验证车速、相机与 UI 的相对关系。

`CreateHeightVisualization()` 当前生成正方形 Sprite，其本地边长为：

```text
mapLocalWorldSize = previewResolution / pixelsPerUnit
```

在地图 Transform 缩放为 1 且未旋转时：

- 测试地图边长：`2160 / 32 = 67.5` Unity 单位。
- 落基山脉边长：`4096 / 14 ≈ 292.57` Unity 单位。

所以当前落基山脉地图的**逻辑米数边长是测试图的 3 倍，Unity 世界显示边长约为 4.33 倍**。二者不应混为一谈；这里是在描述当前参数，不是在将显示比例改回某个历史目标。

只增大 `mapWidthMeters` / `mapHeightMeters`：同一灰度地形在逻辑上更宽、坡度和米制采样间距会改变，但不自动按同倍数放大 Sprite。

只修改 `pixelsPerUnit`：会改变显示尺度和世界坐标到地图米数的比例。现有部分玩家速度以世界单位计算，因此不能承诺它对所有运动手感完全无影响。

`previewResolution` 也参与上述尺寸公式。若只是提高清晰度且希望显示大小不变，需要同步处理 PPU，不能把这个字段当作纯粹独立的质量开关。

对于方向和距离，不要在业务代码中固定写死“1 Unity 单位等于多少米”。已有方法：

```csharp
// worldDirection 是世界空间方向；输出为沿此方向的 Unity 距离。
float worldDistance = map.MapMetersToWorldDistance(worldDirection, distanceMeters);
Vector2 mapDirection = map.WorldDirectionToMapDirection(worldDirection);
Vector2 worldDirectionAgain = map.MapDirectionToWorldDirection(mapDirection);
```

方向换算考虑 X/Y 不同尺度后重新归一化；`WorldSpeedToMapSpeed()` 也复用了同一换算链。

### 4.5 从源灰度图到三套高度数据

**设计目标：去掉图片带来的假地形，不抹平原本该存在的危险。** 在落基山脉出现大量 Step Block 后，设计者接受了给 Detail 单独轻度平滑、同时保留真实悬崖和大台阶的方向。三套通道让程序既能读连续地表，又能对疑似大落差回查未平滑数据。

**实现取舍：三通道是服务上述目标的技术手段，不是要求地形永远只能有三份数据。** 判断未来重构是否合适，应检查误阻挡是否减少、真实落差是否仍被保留，而不是只检查字段名有没有变化。

实现入口：`BakedHeightField.Bake()`。

```text
源 Texture2D
  └── 解码灰度 + 双线性重采样 + 高度范围映射
        └── Raw Detail Height
              ├── 独立 Detail Gaussian Blur  → Detail Height
              └── 独立 Surface Gaussian Blur → Surface Height → RFloat 纹理

显式 Mask / 源图边界规则 → Playable Mask
```

| 高度通道 | 含义 | 本部分涉及的用途 |
| --- | --- | --- |
| Raw Detail | 经源图解码、重采样、映射后的未平滑高度 | 大台阶持续高度差复核 |
| Detail | Raw 的独立轻度平滑版本；Sigma 为 0 时共用 Raw 数组 | 正常驾驶的台阶残差检测 |
| Surface | Raw 的独立地表平滑版本 | 支撑平面、坡度、常规海拔采样和等高线高度纹理 |

**Surface 不是在 Detail 上再次平滑得到的。** 两条平滑分支互不串联，提高 Surface Sigma 不会直接平滑 Detail 通道。

CPU 数组保存米制高度，但提交给 Shader 的 `SurfaceTexture` 将 Surface 高度重新归一化到配置高度范围内的 0…1，并使用 `RFloat` 格式保存。新增 Shader 读取这个纹理时，不能把采样值直接当成米，需要结合最小 / 最大高度还原。

灰度解码分支：

- `TextureFormat.R16`：直接读取 `ushort` 像素并除以 65535，避免退化为 8 位精度。
- `R8` / `Alpha8`：读取字节并除以 255。
- 其他格式：经 `GetPixels32()` 读取，再计算灰度。

`normalizeSourceRange` 开启时，先统计**整张源图**的灰度最小值与最大值，再映射到配置高度范围；该统计发生在可玩区域过滤之前，图外背景也可能参与最小值统计。关闭时使用完整的 0…1 灰度尺度。

概念公式：

```text
t = InverseLerp(sourceMin, sourceMax, sampledGray)
rawHeightMeters = Lerp(minimumHeightMeters, maximumHeightMeters, t)
```

这里的 Raw 不等于原始文件字节，更不等于高精度真实 DEM：它已经过重采样和高度映射。将低精度源图重采样到 2048，并不会凭空恢复被丢失的地形细节。

### 4.6 平滑、分辨率与采样精度

**历史需求：提高地形连续性，同时保持山脉特征。** 设计者先尝试更平滑的灰度源图，后来又追问从彩色等高图生成灰度图的失真问题。这不是“越模糊越好”：过度平滑会改变峰谷和悬崖，单纯提高输出分辨率也无法补回源数据。下面的单位换算用于控制实际平滑范围，避免在不同地图大小下使用同样像素半径却得到不同的物理效果。

高度场内部为正方形 `N × N` 数组，逻辑米尺寸可以独立设置。每轴相邻样本间距：

```text
texelSizeX = mapWidthMeters  / (N - 1)
texelSizeY = mapHeightMeters / (N - 1)
sigmaPixelsX = sigmaMeters / texelSizeX
sigmaPixelsY = sigmaMeters / texelSizeY
```

2048 分辨率下，测试地图约为 `0.1221 m/样本间隔`，落基山脉约为 `0.3664 m/样本间隔`。同样的数组尺寸覆盖更大的地图，空间采样会更疏。

Gaussian Blur 分为横向、纵向两遍；每轴核半径为 `ceil(3 × sigmaPixels)`，边界采用坐标 Clamp。它是普通高斯平滑，不是自动识别悬崖的保边滤波。真实大台阶的额外保护来自通行检测中的 Raw 复核，而非这个滤波器自身。

`minimumHeightMeters` / `maximumHeightMeters` 是映射范围，不是平滑后实测出的地图最低、最高点。平滑会降低局部峰值、抬高局部低谷；涉及“实际最高点是多少”的功能，应明确统计哪套高度通道以及是否只统计可玩区。

### 4.7 可玩区与地图边缘

**设计背景：源图中应存在的边缘地形，不希望在生成地图时无故缺失或被切断。** 设计者曾对比源图与游戏地图指出边缘缺失。本节解释哪些机制可能影响范围；“完整保留有效地形”不等于“把高度图周围的背景全部变成可玩低地”。

另一个历史需求是让延伸至矩形地图边界的等高线也能与边界形成闭合区域，用于地表 Alpha 处理。**那是区域封闭关系的要求，与本节的可玩 Mask 不是同一个问题**；不能因为都涉及“地图边缘”，就认为切换 Mask 会同时完成闭合等高线逻辑。

`BakedHeightField.Bake()` 的选择顺序：

1. 有显式 `playableAreaMask`：优先使用；重采样后灰度 `>= 0.5` 的位置有效。
2. 没有显式 Mask，但启用 `useHeightMapBorderMask`：从图像四条边向低于阈值的像素做四邻域洪泛，标记图外区域。
3. 两者都未启用：没有不规则可玩掩码，Controller 仍做矩形边界验证。

自动边界规则不是“所有黑色像素都不能走”：只有与图边连通的低灰度区域算图外，以保留内部封闭的低谷。可选 Inset 会进一步向内部收缩有效区域，因此可能删除细窄地形。

当前两个主地图资产都没有启用不规则 Mask。源图看起来是某种轮廓，不等于程序自动把整个黑色背景排除出了可玩范围。

高度平滑与 Mask 是分开的处理；平滑核并没有根据 Mask 排除图外像素。因此在配置边缘显示或通行问题时，源图背景、边界掩码、平滑和机器人足迹是否越界都应分别核查。

### 4.8 “烘焙”并非只有一种

**历史需求：地表是固定的，显示范围可以变化。** 设计者要求地形 Texture 的区域归属长期不变、记录进地图；纹理仅在玩家 UI 范围内显示。同时要求闭合等高线区域“边缘较明显、越往内部越淡”，后来进一步明确中心仍应留下很浅的纹理，而不是完全透明。这个内外衰减应属于固定地形，不因玩家走到不同位置而重新定义中心或透明度。

**实现取舍：把可预计算的内容与必须随玩家显示的部分分开。** 区域类型、材质过渡、固定区域 Alpha 适合在编辑器计算并保存；玩家移动时可以改变哪部分结果可见，但不重新生成整张地形。完整画笔和 Alpha 算法留待后续章节，本节先说明为什么不能把所有这些工作都叫“实时渲染”或“完全静态”。

本项目需要区分三件事：

| 工作 | 发生时机 | 运行时是否仍有工作 |
| --- | --- | --- |
| CPU 物理高度场构建 | 地图初始化、编辑预览重建时 | 建立后供游戏逐次采样；并非每帧重建整个高度场 |
| 地表 Texture 的编辑器烘焙 | 地表编辑工具生成并保存结果 | 读取烘焙纹理显示，不等于每帧重新生成种类混合与区域 Alpha |
| 等高线与 UI 范围显示 | 运行时 Shader 和相机相关更新 | 仍需渲染、采样纹理和更新相机可见范围参数 |

静态地表数据不因玩家移动而重新绘制，和“完全没有运行时渲染成本”是两回事。玩家范围裁剪、等高线和地势滤镜仍属于动态显示。

编辑模式会使用不超过 `editorHeightResolution` / `editorPreviewResolution` 的较低分辨率；但生成 Sprite 时会换算 PPU，以保持世界显示大小与运行时一致。因此 Editor 预览可以更粗，不应将其采样精度直接当作 Play Mode 精度。

Controller 在编辑模式比较配置 Hash 来决定是否重建；在运行时的 `Update()` 不执行这套编辑器配置变化检查。调参验证优先采用“退出 Play → 修改对应资产 → 重新运行”，不要假设 Play 中所有资产修改都会立即重烘焙高度场。

## 5. 地形通行检测：完整实现拆解

本章最明确的历史设计约束是：**清除高度图带来的意外像素阻挡，同时保留真实悬崖、大台阶与树干的阻挡；侧翻则有不同于正常驾驶的运动规则。** 目标不是让地图任何地方都能走，也不是让机器人被每一个灰度像素困住。

下文会把“用户明确要求的效果”和“根据当前算法解释的取舍”分开。坡度分级、采样数量等没有明确历史来源的具体选择，按现有实现解释，不归为设计者亲自指定的数值。

源码：[HeightMapTraversalEvaluator.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapTraversalEvaluator.cs)。调用方：[RobotMover.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMover.cs)。

### 5.1 输出不是一个简单的 canMove

**实现取舍：区分“失去正常爬行能力”和“遇到必须阻挡的实体或断差”。** 从当前实现看，坡上的失稳可以继续表现为滑动、转向变化，而树干或台阶需要另一种响应。保留不同结果字段，才能让驾驶与反馈选择相应行为；若统一改成一个 `canMove`，可能把坡地运动也变成硬墙。这是对代码效果的解释，不代表历史需求指定过这份返回结构。

`SlopeTraversalResult` 至少表达三个不同问题：

| 字段 | 含义 |
| --- | --- |
| `HasData` | 本次是否获得了有效判定结果 |
| `IsPassable` | 按当前能力分类是否视为可正常通过 |
| `RequiresHardStop` | 驾驶调用方是否需要执行硬停 |
| `UphillLevel` | 上坡能力分级 |
| `SignedSlopeAngle` | 行进方向坡度；正为上坡，负为下坡 |
| `MaximumSurfaceSlopeAngle` | 支撑面整体最陡方向坡度，包含横向分量 |
| `MaximumStepHeight` | 台阶检测输出的最大残差或复核恢复的高度差 |
| `SurfaceRoughness` | 支撑面拟合的均方根残差，单位为高度米 |
| `DownhillWorldDirection` | 下坡方向，已转换到世界坐标 |
| `BlockReason` | `None / Slope / Step / UnsafeDownhill / Boundary / Obstacle / DeepWater` |

最重要的反例是三级上坡：它可以返回 `IsPassable = false`，但 `RequiresHardStop = false`。驾驶会继续交给失稳、滑落等逻辑处理，并不是撞到一堵不可移动的墙。

`NoData` 也不是阻挡结果。例如 `EvaluateObstacleSweep()` 没有击中障碍时返回 `NoData`。读取结果时需要尊重每个 API 的契约，不能只用 `!IsPassable` 判定碰撞。

### 5.2 查询入口及距离

**实现取舍：前方评估、脚下支撑、实际接触和侧翻剖面解决不同问题。** 一段距离之外有树，不等于玩家已经撞到树；能从前方地形预判风险，也不意味着应提前几米发生实体接触。另有明确历史要求：向坡下侧翻时忽略正常向下移动所需的坡度检测，避免翻滚过程被驾驶保护规则直接截住。

以下数值来自检测器 Prefab，可能与 `.cs` 中的字段初始化值不同：

| 方法 | 用途 | 当前行为 |
| --- | --- | --- |
| `EvaluateMovement(startWorld, direction)` | 较长距离的前方评估 | 6 m；包括地图障碍 |
| `EvaluateImmediateSafety(startWorld, direction)` | 正常移动近距离硬阻挡检查 | 2 m；不在这一查询中检查树干等地图障碍 |
| `EvaluateCurrentSurface(world, direction)` | 当前位置支撑面 | 单个足迹；包含局部 Step / Downhill / DeepWater 判定 |
| `EvaluateMapPath(startMap, endMap)` | 显式地图米坐标路径评估 | 包括地图障碍，沿路径多次拟合 |
| `EvaluateObstacleSweep(startWorld, endWorld)` | 实际位移段与树干等障碍的扫描 | 根据起终点段，不使用 6 m 远距离预警代替实体接触 |
| `TryEvaluateTumbleSegment(...)` | 侧翻位移地形剖面 | 独立 API，不套用正常驾驶的上下坡硬停规则 |

`EvaluateMovement` 这个名字容易让人误以为它就是每帧驾驶的唯一入口。当前 `RobotMover.Update()` 实际使用的是短距离 `EvaluateImmediateSafety` 和当前位置 `EvaluateCurrentSurface`，并在真正移动前额外扫描障碍物。

### 5.3 路径如何离散评估

**实现取舍：检查机器人即将经过的一段地形，而非只看起点或终点。** 只检查两个端点可能漏掉中间的窄落差；沿路径重复检查足迹，用于兼顾路径上的变化与车身占地。当前 1 m 间距是精度与成本之间的实现参数，不是设计要求地形必须按 1 m 分格。

`EvaluateMapPathInternal()` 的主流程：

```text
检查初始化与起点 → 起点无数据返回 NoData
检查终点有效性   → 无效返回 Boundary
按需扫描实体障碍 → 命中返回 Obstacle
将路径分成 ceil(pathLength / pathEvaluationSpacing) 段
对每一段的中点：
    检查横向三个位置的水深
    在机器人足迹内拟合地表
    检查 Step 残差
    累计连续 UnsafeDownhill 距离
    汇总最大坡度、粗糙度等数据
没有硬阻挡时，依据最大上坡角度给出等级与可通行分类
```

当前路径评估间距为 1 m。6 m 查询通常拟合 6 个足迹，2 m 查询通常拟合 2 个足迹。每个足迹本身有长宽，不是只采路径中心线上的一个高度。

这意味着返回 Boundary 时，玩家中心可能尚未越出地图：前方终点或支撑足迹中的某个样本已经越界。遇到“边缘提前阻挡”应先分辨是哪一层越界，而非直接缩小玩家贴图。

### 5.4 支撑面的最小二乘平面拟合

**实现取舍：用一块接地范围描述机器人站在怎样的坡上。** 单个像素的差值容易把细小起伏放大成姿态变化，多点拟合则提供较稳定的整体坡向。这与减少碎小数据噪声干扰的目标一致；但此处的矩形平面拟合并不是完整的履带接触物理仿真，不能据此声称机器人每个接触点都已经真实模拟。

`TryAnalyzeSurface()` 以查询方向作为足迹纵向，垂直方向作为横向，在矩形上读取 `Surface Height`。它是**朝查询方向排列的检测矩形**，不能简单理解为机器人 Sprite 的旋转包围盒。

当前 Prefab：长 2 m，宽 1.5 m，纵向 7 点、横向 7 点，共 49 个高度样本。采样数会限制到允许范围并调整为奇数，确保包含中心。

设样本局部坐标为 `(s, t)`，分别代表前后、左右，拟合：

```text
h(s, t) ≈ c + a × s + b × t
```

由于采样矩形对称，代码可以直接累计求得：

```text
a = Σ(s × h) / Σ(s²)
b = Σ(t × h) / Σ(t²)
c = 平均高度

行进方向坡度 = atan(a)
表面最大坡度 = atan(sqrt(a² + b²))
下坡方向 = -normalize(forward × a + right × b)
```

角度输出转换为度。粗糙度为拟合残差的 RMS，代码使用累计和计算，避免为每次拟合保存整份样本集合。

因此横着穿过陡坡时，“沿行进方向坡度”可能很小，但“表面最大坡度”仍然很大；两者被保留给不同的运动和平衡响应使用。

### 5.5 台阶检测为何不直接比较两点海拔差

**设计目标：让“爬坡”和“撞到一道台阶”可区分。** 设计者反馈过在高差受控、地图已经很大的情况下仍遇到大量 Step Block，因此不能仅以采样前后有高度变化就解释为合理阻挡。当前减去预期坡度变化的做法，试图识别连续爬升以外的突变；上坡太陡是否可控，应再由坡度相关规则处理。

`TryMeasureMaximumStepResidual()` 沿足迹纵向读取 `Detail Height`。当前 2 m 足迹、0.5 m 探测间距，对应 4 个纵向区间；横向设置 3 条平行检测线。

每个区间、每条线计算：

```text
actualChange   = detailHeightCurrent - detailHeightPrevious
expectedChange = fittedForwardGradient × actualSpacing
residual       = abs(actualChange - expectedChange)
```

`expectedChange` 来自 Surface 支撑面拟合。在理想连续平面上，真实高度变化与期望坡度变化相等，即使坡很陡，残差也接近零。这避免把“沿坡爬升”直接等同于“垂直台阶”。

对于每个纵向区间：先取多条横向检测线残差的**中位数**，再在所有纵向区间中取最大值。这样单条线上的孤立异常不会直接决定整辆机器人的硬停。

当前 `maximumStepHeightMeters = 0.65`。检测值 **大于** 0.65 m 时返回 Step 硬阻挡。残差使用绝对值，因此普通 Step 检测同时关注突升和突降，不仅是向上台阶。

限制也要明确：这仍是启发式地形判定，不是解析几何意义上对“台阶”的完美识别。剧烈曲率、源图量化、大范围平滑与局部 Detail 的差异，都可能影响残差。

### 5.6 如何消除像素台阶，同时保留较大落差

**历史问题与目标：不是降低玩家越障难度，而是修复输入地形的伪台阶。** 设计者发现仅提高 `Surface Smoothing Sigma Meters` 仍不能消除 Step Block，随后明确要求给 Detail 增加独立轻度平滑，并保留原本应该存在的悬崖和大台阶。

因此有两个需要同时满足的结果：视觉上连续、只是被灰度量化切成细阶的地表不应频繁硬停；真正有持续高度断差的位置不能因为平滑而全部变成可通过。单纯抬高共用的 0.65 m 阈值会改变玩家能力，并连带削弱测试地图的原有阻挡，不等价于修复这张图。

代码分两层处理，而不是单纯增大全局 0.65 m 阈值。

**第一层：普通检测读取轻度平滑的 Detail。**

落基山脉当前 `detailSmoothingSigmaMeters = 0.75`，使源图中的细碎量化台阶被削弱。测试地图为 0，仍沿用未平滑 Detail。

**第二层：对疑似大台阶使用 Raw 通道复核。**

启用条件是：有地图资产、Detail Sigma 大于 0，且 `preserveLargeStepHeightMeters` 大于 0。随后：

1. 计算同一小区间的 Raw 残差，并取横向中位数。
2. 只有 Raw 残差大于普通 Step 阈值，才发起复核。
3. 以疑似过渡位置为中心，向前、后分别扩展采样。
4. 计算跨过渡区的 Raw 海拔差绝对值，取横向中位数。
5. 达到地图的 `preserveLargeStepHeightMeters` 时，将该高度差恢复进最大 Step 检测值。

复核采样的半跨度是：

```text
halfSpan = actualStepSpacing / 2 + largeStepConfirmationDistanceMeters
```

例如落基山脉当前间距 0.5 m、额外确认距离 1.5 m，则前后采样点相距 3.5 m；复核高度差阈值为 2 m。

这里有两个容易写错的细节：

- 触发复核的局部 Raw 残差阈值仍是普通 Step 阈值 0.65 m，不是直接用 2 m。
- 远端复核比较的是 Raw 海拔差绝对值，**没有再次减去这段长距离的拟合坡度变化**。

所以它能排除许多短促尖峰、保护有持续落差的地形，但不能保证识别所有真实悬崖，也不能保证陡坡上永不误判。要改进这一算法，应同时记录局部 Raw 残差、远端高度差和 Surface 坡度，不能只看最终 Block 标签。

**按设计验收时要看成对结果。** 在相同玩家能力下，分别检查受量化影响的连续坡面、短促尖峰和真实大落差；不能只用“之前卡住的地点现在走过去了”证明完成。还应返回测试地图检查原先应阻挡的台阶没有消失。这里列的是建议检查项，不是已经通过的测试结论。

### 5.7 上坡等级与危险下坡

**实现取舍：坡度难度可以表现为操控和运动变化，不必全部表现为禁止移动。** 当前分级上坡与危险下坡规则分别承担运动响应和安全阻挡；连续危险长度又试图避免一个孤立陡样本立刻决定整段道路。这是现有代码的行为解释。12°、38°、55°应视为当前调参，不能因写进 README 就当作不可更改的设计常量。

当前 Prefab 的等级边界：

| 上坡角度 | 等级 | 检测器层面的含义 |
| --- | --- | --- |
| `0°…12°`，含 12° | LevelOne | 正常可通过分类 |
| `>12° 且 <38°` | LevelTwo | 正常可通过分类，驾驶层可调整动力和滑动 |
| `>=38°` | LevelThree | 标为 Slope 不可正常通过，但不直接要求硬停 |

实际的加速度、减速、滑动和转向响应属于 `RobotMover`，而不是检测器在这里直接推着 Transform 移动。

路径查询的危险下坡要求：有符号角度严格低于 `-55°`，且连续超限长度至少 1.5 m。某一段恢复安全时，累计长度归零；不是把整条路径中互不相连的危险小段相加。

`EvaluateCurrentSurface()` 是单足迹查询，没有这段连续距离累积。它可以报告当前位置的 UnsafeDownhill，而路径查询仍未满足持续长度。文档、UI 或调试工具比较两种结果时应保留这个差异。

### 5.8 树干等实体障碍的扫描

**历史需求：阻挡来自树的实体中心，不来自整片树冠。** 树木 / 灌木的分层要求中，中心与树干代表实体，树叶树冠应允许玩家进入其下方并在视觉上遮挡机身。设计者后来还要求撞树有明显反馈，并进一步强调反馈应随撞击速度变化，而不是每次一样强。

本节实现的是实体接触判定，不是完整的遮挡或震动算法。维护时需要保留这三个不同问题：在哪里撞到、进入树冠下能看到什么、撞击时反馈多强。放大树冠贴图不应直接扩大实体半径，增强震动也不应通过提前判碰撞来代替。

障碍来源是 `HeightMapObstacleFootprint.ActiveFootprints`，一个随组件启用 / 停用登记的集合。检测时过滤无效、未启用、不阻挡、不在地图同一场景或中心无法采样的对象。

每个物件提供逻辑米制的圆半径。玩家按运动段进行圆形扫掠，合并半径为：

```text
combinedRadius = playerRadius + obstacleRadius + contactSkin
```

当前玩家障碍半径 0.75 m、接触间隙 0.02 m。取运动线段上距离障碍中心最近的点，距离不大于合并半径即算命中。这不是单纯检查移动终点，因此能检测某些终点已经越过树干的穿越。

若起点已在接触范围内，且位移与“从障碍中心指向玩家”的向量点积非负，则允许切向或向外移动，避免物件覆盖玩家后把玩家永久锁死。

`HeightMapPlacedObject.footprintRadiusMeters` 的位置 / Gizmo 元数据与 `HeightMapObstacleFootprint.radiusMeters` 的实际阻挡参数不是一回事。树冠的视觉范围也不参与这套实体半径计算。

当前扫描逐个遍历活动足迹，没有在这一方法中建立空间网格或树结构。障碍物数量大幅增加后，应先测量该循环成本，再考虑空间索引。

### 5.9 静态水深的硬阻挡

**实现取舍（本节不追认原始设计）：区分“可以涉入但走得慢”和“已经无法通行”。** 当前代码分别处理浅水减速和深水硬停，这允许关卡用独立水深数据表达两种结果。本次可见历史需求中没有足够细节确认水深规则最初的完整设计理由，因此不把这段工程解释写成设计者已明确规定的水域玩法。

`IsStaticWaterTooDeep()` 从地图资产读取 `MaximumPassableStaticWaterDepthMeters`，在足迹横向左、中、右三个点采样水深；任一点超过可通过深度加数值容差，就返回 DeepWater。

水深不是从灰度海拔直接推断的，而是地图资产中独立编辑的水深数据。可通行浅水的减速由 `RobotMover.CalculateStaticWaterSpeedMultiplier()` 处理；“深水禁止通过”和“浅水行进变慢”是两层逻辑。

### 5.10 驾驶如何消费检测结果

**历史需求：侧翻是失衡后的连续运动，不是正常驾驶换了个动画。** 设计者要求首次侧翻按机身高度产生位移，后续是否继续翻滚由剩余运动与高差等因素决定，并特别强调向坡下翻滚不受普通下坡检测限制。这要求正常驾驶能让出位移控制，而不是与侧翻同时推动玩家。本节只解释控制权与查询衔接；具体侧翻能量、距离和最终摇摆算法仍需后续章节展开核对。

以下为 `RobotMover.Update()` 的流程摘要，不是可直接替换源码的实现：

```text
如果 MovementMode 不是 Driven：清理普通驾驶运动，退出
读取键盘与手柄，处理死区、机械臂占用、拍照锁定和平衡操控权
按探测方向查询 ImmediateSafety 与 CurrentSurface
推进三级坡度爬升失败流程并更新转向
若前向路径 RequiresHardStop：HardStop，退出
根据当前地形计算动力倍率、漂移、滑动、地形转向和浅水减速
推进当前车速，合成驾驶速度与地形速度
根据实际速度方向再查 ImmediateSafety
按本帧实际位移扫掠地图障碍
通过后：transform.position += desiredVelocity × deltaTime
```

第二次、按实际速度方向的 ImmediateSafety 检查会排除 `UnsafeDownhill` 作为该处的硬停原因；前面的主动探测检查仍保留危险下坡规则。因此不能用一句“所有下坡超限都会无条件停住”概括实际调用链。

侧翻时普通驾驶让出控制权，侧翻组件使用独立地形段查询。新增移动能力时，应先决定由哪套控制权执行位移，并明确是否沿用驾驶通行规则；不要把所有运动一律接到普通驾驶 HardStop 上。

### 5.11 性能与资源生命周期

**设计约束：地图规模变大、地表类型变多，不希望固定内容引入不必要的实时计算。** 这不意味着删除所有运行时判定：通行检测仍要回答当前位置能否走。工程上需要把一次性生成的高度数据和每次移动的局部查询分开，并量化各自成本；不能为了“零开销”取消正确性所需的检查，也不能每帧重复生成固定数据。

- 单个当前足迹至少包含 49 次 Surface 采样，以及 Detail 和可选 Raw 检测；路径查询会重复该过程。机器人一帧也不一定只查询一次。
- 横向残差使用检测器持有的固定 Scratch 数组，避免每次检测新建数组。该实例含可变 Scratch 状态，不能直接作为可并发重入的无状态服务使用。
- `BakedHeightField` 的高度数组在构建后重复使用；`Dispose()` 销毁配套运行时纹理。Controller 重建地图时释放旧生成资源，不应长期持有重建前的纹理或高度场引用。
- 2048² 的一个 `float` 高度数组约占 16 MiB。Raw / Detail / Surface 三份独立数组约为 48 MiB；Detail 与 Raw 共用时更少。这只是高度数组估算，不含源图、GPU 纹理、预览纹理、掩码和模糊临时内存。
- 高度分辨率、检测密度、地图显示分辨率分别影响不同成本。将预览分辨率翻倍不会直接让通行更准，将检测间距减半也不会恢复源图里已经缺失的高度精度。

## 6. 配置入口与排查示例

本章设计目标：让设计者能够自己调效果，同时让程序员知道一次改动会影响哪些玩法。历史上曾多次出现参数改不动、不同尺度混用，以及为改善视觉却可能改变物理难度的疑问。因此调参表不仅指明位置，也要帮助区分“修数据”“改表现”和“改玩家能力”。

### 6.1 常见修改应落在哪一层

**本节用途：把设计语言翻译为配置入口。** “地图看起来更大”“等高距更大”“玩家越障更强”是不同需求，即使有时都能让某片区域显得更容易通过，也不能互相替代。

| 想修改的内容 | 首先查看的位置 | 注意事项 |
| --- | --- | --- |
| 某地图最低 / 最高映射高度、米制长宽 | 场景绑定的 `HeightMapLevelAsset` | 不要只改 Controller 的 Legacy 字段 |
| 某地图像素台阶平滑 | 同一资产的 `detailSmoothingSigmaMeters` | Surface Sigma 不是替代项 |
| 大落差保留 | 同一资产的 `preserveLargeStepHeightMeters` / `largeStepConfirmationDistanceMeters` | 仅在相应复核条件满足时启用 |
| 等高线垂直间隔 | 同一资产的 `contourIntervalMeters` | 不等于画面中的固定像素间距 |
| 地图相对玩家的世界显示大小 | `previewResolution / pixelsPerUnit` 与地图 Transform | 同时检查米制换算和运动手感 |
| 玩家出生位置 | 场景 Bootstrap 的 `playerSpawnMapPositionMeters` | Scene 工具修改同一保存值 |
| 玩家能越过多高的 Step | 检测器 Prefab 的 `maximumStepHeightMeters` | 改共享 Prefab 会影响使用它的其他地图 |
| 前方提前多远检查硬阻挡 | 检测器 Prefab 的 `hardStopProbeDistanceMeters` | 不等于树干实体碰撞半径 |
| 地表支撑矩形大小 | 检测器 Prefab 的 `robotFootprintLengthMeters` / `robotFootprintWidthMeters` | 独立于机身贴图大小 |
| 玩家撞树半径 | 检测器 Prefab 的 `robotObstacleCollisionRadiusMeters` | 独立于支撑矩形 |
| 某棵树的实体半径 | 对象或其 Prefab 的 `HeightMapObstacleFootprint.radiusMeters` | 不应把树冠一起当成实体 |
| 右上角地形调试信息 | 场景 Bootstrap 的 `showRobotTerrainData` | 显示开关，不改变底层检测规则 |

### 6.2 示例：某处反复出现 Step Block

**排查目标：恢复预期可通行地形，不顺带删除合理障碍。** 这个示例对应落基山脉像素台阶问题；如果真正原因是树干、深水或边界，继续增大平滑参数既不解决问题，也可能损坏山体形状。

建议按以下顺序设置断点或临时日志，而不是一次同时改多个阈值：

1. 在 `RobotMover.HardStop` 的调用处看 `BlockReason`，确认确实是 Step，不是 Boundary、Obstacle 或 DeepWater。
2. 沿 `TryMeasureMaximumStepResidual` 查看当前位置、实际采样间距和 Surface 的 `fittedForwardGradient`。
3. 查看 Detail 每条横向线的残差，以及排序后使用的中位数。
4. 若普通 Detail 残差不高，检查是否走到 Raw 大落差复核，并记录复核前后的实际高度。
5. 确认运行中的 `map.LevelAsset` 和源高度图确实是预期资产，而非另一个场景 / 测试图配置。
6. 只调整已定位的那一层：源图质量、Detail 平滑、Raw 复核或玩家能力阈值。

一个有用的日志组合是：地图坐标、查询方向、BlockReason、SignedSlopeAngle、MaximumSurfaceSlopeAngle、MaximumStepHeight，以及触发复核时的 Raw 局部残差和远端高度差。单独打印“坡度不大”不足以解释 Step。

### 6.3 示例：地图边缘被提前截断

**排查目标：保留源图中的有效地形，同时维持正确边界。** 对设计者提出的“边缘缺失”，应先确认是画面少画了、数据被 Mask 排除了，还是玩家足迹提前越界。三者的修正方式不同，不能仅靠扩大可玩范围让问题表面消失。

应区分四种情况：

- 只是低分辨率 Editor 预览与 Play Mode 显示不同。
- 开启 Mask 后，阈值或 Inset 删除了窄边缘。
- 外部背景参与了高度归一化 / 平滑，改变了可见地形。
- 玩家中心仍在范围内，但路径终点或 2 × 1.5 m 支撑足迹已越界。

先用 `TrySampleMapPosition()` 判断目标点的数据有效性，再对照 Mask 和足迹；不要直接把等高线视觉变化解释为源图被裁切。

### 6.4 新增代码时的单位与空数据处理示例

**实现取舍：让后续功能沿用同一套地图事实。** 若一个系统把空采样当作 0 m，另一个系统又把它当作不可玩区，玩家可能在同一地点看到互相矛盾的高度或运动反馈。统一检查采样结果，是保持设计表现与底层状态一致的基础，不是新增一种玩法限制。

```csharp
// 示意：在一个已持有 MapTestSceneController 引用的组件中查询。
Vector2 worldPosition = transform.position;
if (!map.TrySampleWorldPosition(worldPosition, out Vector2 mapMeters, out float elevationMeters))
{
    // 此点可能越界、位于不可玩掩码中，或地图尚未生成。
    // 不把 elevationMeters 的默认值 0 当作有效海拔。
    return;
}

// elevationMeters 是逻辑海拔；不要直接写入 transform.position.z。
// mapMeters 可用于与其他地图米制位置计算逻辑距离。
```

### 6.5 把设计目的转成体验检查

以下是与本试读范围对应的回归建议，**尚未在本次文档修改中执行**。它们用于帮助程序员判断“设计效果是否保留”，补充编译通过或数据断言不能完全覆盖的体验问题。

| 设计目的 | 建议比较的情境 | 不能仅用什么作为完成依据 |
| --- | --- | --- |
| 正式地图更大，但延续原有操控感觉 | 测试图与正式图的机身比例、可视范围、相同输入下移动距离 | 只看到宽高字段乘了 3 |
| 地表类型和内部衰减保持静态 | 走开再返回同一地图坐标；比较其在相同显示条件下的纹理与 Alpha | 只确认输出了一张纹理，未检查运行时是否重新改写 |
| 清除假台阶、保留真落差 | 量化连续坡、孤立尖峰、大台阶以及旧测试图 | 只确认某一个卡点能通过 |
| 树冠遮挡、树干阻挡 | 从树冠外走入树冠下，再接近实体中心 | 只确认玩家无法穿过整棵树的图片矩形 |
| 撞击反馈与撞击速度相符 | 低速和高速接触同一树干，比较反馈差异 | 只把每次震动全部拉满；完整反馈实现待对应章节核对 |
| 坡下侧翻不被驾驶下坡保护截停 | 在会触发正常下坡保护的坡面分别驾驶与侧翻 | 为了侧翻顺畅而直接关闭所有模式的下坡检查 |
| 出生点能直观看懂和编辑 | 拖动出生点标记、保存场景、重新运行对照位置 | 只让标记移动，但没有写回实际出生参数 |

## 7. 本部分的验证范围与后续章节

本部分的实现说明基于此前对源码、Prefab、场景引用和地图资产的交叉核对；设计目标依据本任务中的历史需求补充。表中的数值区分了代码初始化值与实际序列化值。缺少明确历史依据的部分标为工程解释，而非补写虚构的原始设计。本文没有声称已通过新一轮游戏运行测试，也没有修改玩法、Prefab、地图数据或 HANDOFF。

后续每章采用“**设计目标与背景 → 玩家应体验到的结果 → 当前实现与选择原因 → 入口文件 / 数据 / 调用流程 / 算法 → 参数来源 → 设计与实现的差异及限制 → 体验检查**”的格式。历史需求只保留与理解当前功能有关的演变，不把文档变成逐条聊天记录；纯工程部分说明工程目的，不强行编造玩法动机。

后续建议按以下顺序补全：

1. **玩家操控与重心**：输入读取、速度与转向、坡地运动、重心数据与重心 UI 的区别。
2. **侧翻、机械臂与扶正**：控制权交接、连续侧翻能量 / 位移、最终摇摆、扶正输入和回正惯性。
3. **镜头与 UI**：Main UI 尺寸依赖、局部旋转、视野裁剪、震动与手柄反馈。
4. **地图编辑与地表渲染**：材质种类索引、画笔写入、Alpha / 噪声过渡、闭合区域衰减、静态水和地势滤镜。
5. **动物系统**：通用感知与状态、物种行为、麝鼠与啄木鸟、惊扰 / 躲藏 / 返回、树木绑定与生态配置。
6. **扫描、认知与拍照**：对象发现、认知状态、对焦与快门、命中判定、照片库，以及演示图片和封存 UI 的实际调用状态。
7. **开发与验证**：新增地图 / 物种 / 美术的步骤、编辑器生成工具的使用边界、回归检查与已知限制。

此试读版用于确认技术深度与组织方式。上述待补充章节不构成已完成的系统审计，也不意味着每个历史方案都已在当前代码中启用。

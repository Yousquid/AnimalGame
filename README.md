# AnimalGame — 程序员技术文档

> **设计意图与代码实现手册**
>
> 代码与序列化配置核对日期：2026-09-14。本文描述当前工作区的实现，不将历史讨论中的设计方案视为已经实现的功能。
>
> 面向接手项目的程序员：从工程入口与地图数据开始，说明玩家运动、重心、侧翻与扶正、镜头与反馈、扫描与拍照、地图编辑、动物与植被，以及开发和验证流程。记录已存在的实现，也明确尚未达到原始设计的部分。

本文同时回答两个问题：**设计者想让玩家体验到什么，以及程序现在怎样实现这种体验。** 后文按以下口径区分信息来源：

- **设计目标 / 历史需求**：依据本任务中设计者明确提出的需求归纳，不是原话逐字引用。需求有过修改时，说明与本节相关的演变，不将早期试验要求永久固化。
- **实现取舍 / 工程解释**：根据当前代码解释一种组织方式或算法有什么作用；不把推断写成设计者当初指定的技术方案。尤其不能将“使用 Resources”“平面拟合”等实现细节冒充原始玩法需求。
- **当前实现与限制**：以源码和序列化资产为准。设计目标不等于已经完全达成；不一致之处保留说明，不因补充设计背景而擅自改代码或参数。

各功能节按“最初遇到什么问题 → 想让玩家感受到什么 → 当前采用什么方法 → 修改时必须保留什么”展开，再说明调用关系、算法和参数。章节末的体验检查是后续验证建议，不是已经执行的测试结果，也不是禁止未来设计调整的固定规则。

## 阅读导航

- [1. 工程定位与运行入口](#1-工程定位与运行入口)
- [2. 文件结构与职责划分](#2-文件结构与职责划分)
- [3. 场景启动与运行时装配](#3-场景启动与运行时装配)
- [4. 地图数据与坐标体系](#4-地图数据与坐标体系)
- [5. 地形通行检测：完整实现拆解](#5-地形通行检测完整实现拆解)
- [6. 配置入口与排查示例](#6-配置入口与排查示例)
- [7. 玩家输入与运动](#7-玩家输入与运动)
- [8. 重心、连续侧翻、机械臂与扶正](#8-重心连续侧翻机械臂与扶正)
- [9. 机身表现、镜头、手柄反馈与 UI](#9-机身表现镜头手柄反馈与-ui)
- [10. 地形扫描、生物认知与拍照](#10-地形扫描生物认知与拍照)
- [11. 固定地图编辑、地表表现与高度图生产](#11-固定地图编辑地表表现与高度图生产)
- [12. 动物、感知、生态与植被](#12-动物感知生态与植被)
- [13. 开发工作流、数据生命周期与扩展](#13-开发工作流数据生命周期与扩展)
- [14. 实现差异、回归验证与源码索引](#14-实现差异回归验证与源码索引)

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
| 重心与侧翻 | [RobotBalanceController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotBalanceController.cs)、[RobotTumbleController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotTumbleController.cs) | 平衡状态、侧翻过程；第 8 章展开 |
| 机械臂与扶正 | [RobotArmController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotArmController.cs)、[RobotSelfRightingController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotSelfRightingController.cs) | 机械臂操控和扶正流程；第 8 章展开 |
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

**实现取舍：把可预计算的内容与必须随玩家显示的部分分开。** 区域类型、材质过渡、固定区域 Alpha 适合在编辑器计算并保存；玩家移动时可以改变哪部分结果可见，但不重新生成整张地形。完整画笔和 Alpha 算法见第 11 章；本节先说明为什么不能把所有这些工作都叫“实时渲染”或“完全静态”。

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

**当初要解决的问题。** 高度图原本想表达连贯的山坡，但图片像素与有限灰度会把高度分成细小层级。如果对紧挨着的少数点直接求差，坡度容易忽大忽小；本应是连续行驶的坡面，也可能被解释成连续撞击许多台阶。玩家看到的是平缓地势，操作上却频繁硬停，这种危险来自数据表达的误差，不是设计者想放置的障碍。

**希望达到的体验。** 机器人沿普通坡面移动时，高低变化和坡向响应应连续、稳定，尽量减少不应该出现的 Step Block；只有真正陡峭、具有明显断差或实体阻挡的地方，才带来相应的操控困难或碰撞。因此这套处理不是单纯“把所有坡变容易”，而是让玩家感知到的地形与运动结果更加一致。

**为什么组合这些做法。** Surface 的高斯平滑先重建较连续的地面高度；随后用一整块接地范围拟合代表性的坡面，减少单像素起伏支配坡向；台阶检测再扣除这段坡面本来就应有的连续高度变化。之后加入的独立 Detail 轻度平滑进一步针对残留像素台阶，而 Raw 复核用于保护真实大落差。这些步骤共同服务同一目的，但职责不同：**平面拟合本身不会重写高度数组，不能把所有去台阶效果都归功于拟合，也不能只提高 Surface Sigma 就期待 Detail 同时变平滑。**

矩形平面拟合是上述目标的一种实现近似，不是完整履带接触物理仿真。修改它时应同时比较连续坡上的稳定性与真实障碍的保留，不能只以估算坡度数值更小作为改进依据。

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

**历史需求：侧翻是失衡后的连续运动，不是正常驾驶换了个动画。** 设计者要求首次侧翻按机身高度产生位移，后续是否继续翻滚由剩余运动与高差等因素决定，并特别强调向坡下翻滚不受普通下坡检测限制。这要求正常驾驶能让出位移控制，而不是与侧翻同时推动玩家。本节只解释控制权与查询衔接；具体侧翻能量、距离和最终摇摆算法见第 8 章。

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

以下是与前六章对应的回归建议，**尚未在本次文档修改中执行**。它们用于帮助程序员判断“设计效果是否保留”，补充编译通过或数据断言不能完全覆盖的体验问题。

| 设计目的 | 建议比较的情境 | 不能仅用什么作为完成依据 |
| --- | --- | --- |
| 正式地图更大，但延续原有操控感觉 | 测试图与正式图的机身比例、可视范围、相同输入下移动距离 | 只看到宽高字段乘了 3 |
| 地表类型和内部衰减保持静态 | 走开再返回同一地图坐标；比较其在相同显示条件下的纹理与 Alpha | 只确认输出了一张纹理，未检查运行时是否重新改写 |
| 清除假台阶、保留真落差 | 量化连续坡、孤立尖峰、大台阶以及旧测试图 | 只确认某一个卡点能通过 |
| 树冠遮挡、树干阻挡 | 从树冠外走入树冠下，再接近实体中心 | 只确认玩家无法穿过整棵树的图片矩形 |
| 撞击反馈与撞击速度相符 | 低速和高速接触同一树干，比较反馈差异 | 只把每次震动全部拉满；完整反馈实现待对应章节核对 |
| 坡下侧翻不被驾驶下坡保护截停 | 在会触发正常下坡保护的坡面分别驾驶与侧翻 | 为了侧翻顺畅而直接关闭所有模式的下坡检查 |
| 出生点能直观看懂和编辑 | 拖动出生点标记、保存场景、重新运行对照位置 | 只让标记移动，但没有写回实际出生参数 |

## 7. 玩家输入与运动

### 7.1 设计目的：让机器人“被驱动”，而不是让图标直接跟随摇杆

这一组系统服务于前面地形检测的最终体验：地图不只是背景，坡度、阻挡和地表必须改变机器人移动时的感觉。玩家需要能区分“正常驾驶”“爬坡费力”“失去抓地力”“被障碍挡住”和“已经侧翻，不能继续驾驶”。如果所有结果都只表现为速度立即归零，就难以理解危险来自哪里；如果所有输入都直接移动 Transform，又会绕过地图的物理约束。

历史需求还不断增加了机械臂、拍照、扶正等共用摇杆的操作。因此，输入的设计目的不是让一个摇杆同时控制多个系统，而是在明确模式中分配控制权：平时驾驶；按住 L3 时操纵机械臂；右杆平时控制重心，在拍照时控制取景，在瘫痪扶正时反复推动发力。

**实现取舍 / 工程解释：** 当前用自定义速度积分和地形查询组合这些行为，而不是 Rigidbody 接触摩擦或完整履带动力学。这样可单独调节起步、倒车、制动、坡面滑移、侧翻和镜头反馈；代价是程序员必须维护各个模式之间的速度清理及控制权交接。

需要保留的体验是：增加新模式不能让同一份输入既移动玩家又改变机械臂；调整角色贴图大小不能悄悄改变地形坡度和障碍半径；地形检测变得平滑，也不意味着真实的陡坡和大台阶不再影响驾驶。

### 7.2 源码、输入配置与运行资产

| 入口 | 责任 |
| --- | --- |
| [RobotMover.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMover.cs) | 油门、转向、速度、坡面运动、运动控制权 |
| [AdaptiveLegacyGamepadInput.cs](AnimalGame/Assets/Scripts/RobotMap/AdaptiveLegacyGamepadInput.cs) | Xbox / Sony / Generic 手柄轴与按键适配 |
| [InputManager.asset](AnimalGame/ProjectSettings/InputManager.asset) | Legacy Input Manager 的命名轴配置 |
| [RobotMarker.prefab](AnimalGame/Assets/Prefabs/Resources/Robot/RobotMarker.prefab) | 当前玩家运动、重心、侧翻、机械臂和扶正的主要序列化配置 |
| [HeightMapTraversalEvaluator.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapTraversalEvaluator.cs) | 当前地面、路径安全及障碍扫描；算法见第 5 章 |
| [SceneReloadShortcut.cs](AnimalGame/Assets/Scripts/SceneReloadShortcut.cs) | R 键重载当前场景 |

`AdaptiveLegacyGamepadInput` 不是单纯的 Input System 包封装。连续轴主要通过 `Input.GetAxisRaw` 读取 Legacy 命名轴；部分按键在 `ENABLE_INPUT_SYSTEM` 下先读 `Gamepad.all`，再根据方法使用 Legacy 回退。修输入时必须同时检查脚本与 Input Manager，不能只找 Input Action Asset。

设备识别每约 `0.75s` 刷新一次，依据 `Input.GetJoystickNames()` 中的名称选择 Sony、Xbox 或 Generic 布局。Sony 的左右扳机是两根独立轴，代码识别 `[-1,1]` 型轴后归一化，再计算右减左；未知设备保留 Xbox 风格回退。缺失轴被记录进 `MissingAxes`，输出一次警告并返回零，避免每帧抛异常。

这是兼容性策略，不是多人输入管理：当前并没有给每个玩家绑定独立设备。同机插多个手柄时，按键遍历与连续轴选择也不保证来自同一台设备。

### 7.3 当前控制权与常用输入

以下是本章负责的操作。拍照、扫描、滤镜的完整按键流程分别见对应系统章节。

| 状态 / 功能 | 手柄 | 键盘 | 消费者 |
| --- | --- | --- | --- |
| 常态前进 / 倒车 | 当前 Prefab 启用扳机油门模式；适配层提供合并油门 | W / S | `RobotMover` |
| 常态转向 | 左杆横轴 | A / D | `RobotMover` |
| 手动重心配平 | 右杆 | 方向键 | `RobotBalanceController` |
| 机械臂模式 | 按住左杆按钮 L3 | 按住 Caps Lock | `RobotArmController` |
| 机械臂目标 | 按住 L3 后推动左杆 | 按住 Caps Lock 后按 WASD | `RobotArmController` |
| 扶正发力 | 向侧翻反方向反复推动右杆，回中后再次推 | 反复按 / 松对应方向键 | `RobotSelfRightingController` |
| 重置当前场景 | 本功能未提供手柄映射 | R | `SceneReloadShortcut` |

Caps Lock 检查的是 `Input.GetKey` 的按住状态，不是操作系统的大写锁定灯状态。扶正当前也不是“按住右杆按钮 R3”，更不是原先的连按 Y / 右 Ctrl 方案；它检测的是杆位的反复推入和回中。

`RobotMover` 有三个运动模式：

```text
Driven ──失衡事件──> ExternalTumble ──侧面 / 倒扣停稳──> Fallen
   ↑                       │                            │
   └────完整 360° 倍数停稳──┘                            │
   └──────────────────机械臂扶正完成─────────────────────┘
```

`Fallen` 及 `IsPermanentlyFallen` 的命名保留了早期“倒下后不可恢复”的试验语义；当前已经有扶正通道，不能把名字理解成永远无法退出的最终死亡状态。

`IsArmInputCaptured` 只在 `Driven` 下成立，令驾驶油门和转向输入为零，但没有立即清空全部已有速度；惯性减速和地形滑移仍可能继续。`IsPhotoModeInputLocked` 除输入归零，还立即清零主动驾驶速度和转向速度，但地形速度仍由地形逻辑处理。它们不是冻结世界的通用暂停开关。

### 7.4 每帧运动流程与量纲

`RobotMover.Update()` 的主要流程如下：

```text
不是 Driven？清理主动运动状态，返回
读取键盘 / 手柄，逐轴选绝对值更大的输入
应用机械臂 / 拍照输入占用，以及重心控制能力折损
沿意图方向读取 ImmediateSafety + CurrentSurface
推进三级爬坡失败状态机，计算转向
意图路径要求 HardStop？清速并返回
计算地形滑移、坡度速度倍率、下坡加速和水深倍率
积分主动速度、地形速度和地形角速度
沿实际合成速度再检查安全，并扫掠本帧障碍路径
Transform += 合成速度 × deltaTime，应用地形转向 / 下坡对齐
```

主动速度与地形速度分开保存：

```text
v_world = transform.up × CurrentSpeed + CurrentTerrainVelocity
Δposition_world = v_world × Δt
v_target = throttle × forwardOrReverseSpeed × overallMotionScale
           × slopeTopSpeedMultiplier × waterSpeedMultiplier
CurrentSpeed = MoveTowards(CurrentSpeed, v_target, acceleration × Δt)
```

**量纲陷阱：** `RobotMover.MapPosition` 实际返回 `transform.position`，其名字不代表地图米坐标；运动速度首先是 Unity 世界单位 / 秒。进入侧翻能量计算等以米为单位的系统时，才通过 `HeightMapTraversalEvaluator.WorldSpeedToMapSpeed()` 转换。改变地图世界显示比例时，应结合第 4 章坐标换算检查效果。

转向通过 `CurrentTurnSpeed` 的加速 / 减速逐步到达目标角速度。倒车时根据当前速度或油门符号反转转向响应；这不是直接将机器人朝向设为摇杆方向。三级爬坡失稳或下坡自动对齐期间会临时锁定普通转向。

输入死区不是简单截断后保留原数值：超过死区的幅度重新映射到 `[0,1]`。键盘和手柄按幅度择强，不相加，因此同时输入不会得到两倍油门。

### 7.5 坡面上的不同运动结果

**设计目的：平滑地形不等于平坦玩法。** 对高度数据的平滑旨在减少图像灰度台阶和孤立像素造成的“假阻挡”，让真实山坡能被持续读取和驾驶；在这些连续坡面上，仍需以爬坡变慢、滑动和失稳传达坡度，而不是通过随机的 Step Block 制造困难。这也是为什么高度重建、通行分类与运动反馈分属不同层。

目前有以下分工：

- **二级坡面**：随方向坡度增加降低上坡最高速度；添加下坡滑移，再用履带侧向抓地限制横滑。它应该表现为吃力但仍有控制感。
- **三级上坡**：可进入 `Grip → Strain → Slip`。先短暂抓住坡面，随后推进能力下降，最终失去主动前进并滑走。这个时间过程避免“只要触到阈值就瞬间弹走”的机械感。
- **失稳偏转**：横坡随机方向经平滑后形成横向漂移和地形角速度，不是每帧独立随机瞬移。
- **滑落后恢复**：短时间将滑移方向和机身朝向向下坡方向对齐，再交回常态驾驶。
- **连续下坡**：在允许通行的前提下，提高最高速度及加速度；过于危险的下坡仍可能由路径安全检测拦截。
- **可通行静态水域**：随水深给予额外速度损失；超过通行水深由检测层阻挡，而不是只无限减速。

三级爬坡序列只有在地面有数据、有非零油门、方向坡度向上且属于三级上坡时进入。如果仍在三级坡上却停止尝试上坡，抓地 / 吃力阶段可提前转入滑落；若已经离开三级地面，则重置序列。

两次方向查询有不同作用：第一次围绕驾驶意图保护前路；第二次围绕真实合成速度补查滑移方向。第二次 `actualPathResult` 的硬阻挡分支特意排除 `UnsafeDownhill`，但仍检查台阶等硬障碍，并追加树干足迹扫掠。不要将这段例外扩展为“常态驾驶完全忽略下坡安全”。

### 7.6 当前重要参数与排查

下表数值读取自 `RobotMarker.prefab`，不是全部采用脚本初始值。

| 组件字段 | 当前值 | 调整目的 / 注意事项 |
| --- | --- | --- |
| `overallMotionScale` | `0.88` | 统一缩放多项速度、加速度和转向参数；不是仅缩放平移 |
| `forwardSpeed` / `reverseSpeed` | `4.2` / `3` | 基础最高速度；还需乘总体、坡面、水域倍率 |
| `turnSpeed` / `turnAcceleration` | `60` / `60` | 角速度和起转速度；初始代码值不同 |
| `coastDeceleration` / `brakingDeceleration` | `5` / `8` | 松手滑行与反向制动分别调节 |
| `useTriggerThrottleGamepadMode` | `true` | 当前前后驾驶不由左杆纵轴提供 |
| `stickDeadZone` / `triggerDeadZone` | `0.15` / `0.05` | 普通驾驶死区，不会替代机械臂或重心死区 |
| `levelTwoMinimumTopSpeedMultiplier` | `0.55` | 二级上坡末端速度倍率 |
| `levelThreeGripDuration` / `levelThreeStrainDuration` / `levelThreeSlipDuration` | `0.35s / 0.45s / 0.45s` | 三级爬坡失败的三个阶段 |
| `levelThreeSlideAcceleration` | `10` | 三级滑移追随速度；仍经过总体倍率 |
| `downhillMaximumSpeedMultiplier` | `1.5` | 连续下坡最高速度上限倍率 |
| `shallowWaterSpeedReduction` / `deepWaterSpeedReduction` | `0.2 / 0.3` | 通行水域速度减少 20% 到 30% |

如果“摇杆动了但玩家不动”，先查 `MovementMode`、两个输入占用标记、`IsSlopeBlocked` 和 `CurrentTraversalResult.BlockReason`；之后再检查设备轴。不要首先将问题归因于 Rigidbody 或 Animator。

`SceneReloadShortcut` 在运行时安装快捷键实例，检测 R 并通过 `SceneManager` 重载当前活动场景；它是测试回到初始状态的入口，不是完整存档重置 / 读取系统。修改 Prefab 的效果要重新进入场景，运行时 Inspector 中的临时修改不自动成为资产默认值。

建议验证：平地起步、反向制动、松手惯性、二级坡横穿、三级上坡失败、机械臂模式滑行和拍照模式输入互斥。单独验证“检测能通过”还不足以证明运动体验正确。

## 8. 重心、连续侧翻、机械臂与扶正

### 8.1 设计演变与系统边界

这组功能经历了多次明确的设计迭代，理解这些目的比只知道类名更重要：

1. **最初的问题是缺少失衡后果。** 重心越界应该朝失衡方向倒下，并带来清晰的镜头和手柄冲击，而不是重心点越界后机器人仍正常开走。
2. **随后需要从“倒下特效”变成连续翻滚。** 第一次侧翻应伴随约一个机身高度的位移；后续在高度与长 / 宽间交替，并根据速度和地形落差继续或停下。因而当前不能只播放固定长度旋转动画。
3. **不能在翻滚最需要信息时隐藏重心 UI。** 点的位置要表现快速摆动、着陆回弹和危险方向，主要在支持圈外；机身与大 UI 可以翻滚，但重心显示应保持可读。
4. **最后一次不应戛然而止。** 不够能量继续完整翻滚时，机身仍有余势，应小幅反向摆动、再摆回来，然后才进入瘫痪或恢复驾驶。完整 360° 倍数的重心回中应与停稳同步，不能机器已经停稳后才补演回中。
5. **机械臂不只是装饰。** 它需要明确的连接关系、伸缩行程、接近身体的拉货操作空间，以及侧翻后朝支撑方向伸出的安装位置；后续扶正复用这套输入和外观。
6. **扶正需要持续身体性操作。** 早期连按 Y 后拉右杆的版本已经改为：左杆保持支撑方向，右杆向反方向反复推、回中、再推，持续足够节奏后扶起；操作中断应逐渐失去进展。
7. **扶正成功不是直接进入绝对稳定。** 越过支撑范围后会产生反向惯性，玩家需要立即反向配平，否则可能再次翻倒。

**实现取舍 / 工程解释：** 当前是基于二维逻辑和参数化表现的可控近似，没有真正的三维刚体翻滚、关节求解或地面支撑多点刚体模拟。下文明确区分“逻辑重心”“翻滚显示重心”“扶正进度重心”和“角色美术朝向”，避免改表现时意外改变失败条件。

### 8.2 组件分工、事件与更新顺序

| 源码 | 责任 / 主要对外状态 |
| --- | --- |
| [RobotBalanceController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotBalanceController.cs) | 常态逻辑重心、操控能力、`TippedOver` 事件 |
| [RobotTumbleController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotTumbleController.cs) | 连续 90° 翻滚步骤、能量、路径位移、最后摇摆、完整周数恢复 |
| [RobotBalanceView.cs](AnimalGame/Assets/Scripts/RobotMap/RobotBalanceView.cs) | 独立屏幕空间重心圈、圆点 / 叉号、残影 |
| [RobotMarkerView.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMarkerView.cs) | 机身、叉号、朝向箭头投影、最后摇摆位移 |
| [RobotArmController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotArmController.cs) | 输入占用、两个安装点、连接臂、刚性外臂瞄准与伸缩 |
| [RobotSelfRightingController.cs](AnimalGame/Assets/Scripts/RobotMap/RobotSelfRightingController.cs) | 支撑与节奏判定、扶正进度、失败回退、反向惯性 |

`RobotTumbleController` 提供 `Started`、`StepCompleted`、`FinalRockingStarted`、`RockImpact`、`Settled`；扶正组件提供 `ForcePulse`、`SupportFailed`、`RightingLanded`。镜头和震动等表现订阅这些阶段事件，避免根据一个 `isFallen` 布尔值猜测全部冲击时刻。

关键生命周期顺序：

| 执行顺序 | 回调 | 意义 |
| --- | --- | --- |
| `-50` | `RobotArmController.Update` | 先占用输入，常态驾驶才不会同帧误读为移动 |
| 默认 `0` | `RobotMover.Update` | 积分主动运动与地形运动 |
| `125` | `RobotTumbleController.Update` | 如已进入翻滚，接管位置 / 摇摆 / 恢复表现 |
| `130` | `RobotSelfRightingController.Update` | 读取机械臂状态、右杆节奏，推进扶正 |
| `100` | `RobotBalanceController.LateUpdate` | 根据更新后的运动量计算重心并发布越界事件 |
| `300` | `RobotBalanceView.LateUpdate` | 选择最终显示状态并绘制重心 UI |

表中的数字只排序同类 Unity 回调；**全部 `Update` 先于全部 `LateUpdate`**。`Balance.LateUpdate` 越界时同步调用侧翻监听器完成首步准备，侧翻的逐帧位移从后续 `Tumble.Update` 开始。不能只按 `100 < 125` 推断重心总在翻滚之前每帧运行。

### 8.3 常态重心：坡面、惯性、手动配平的叠加

**设计目的：危险要有可观察的积累过程。** 玩家需要看到坡面把重心推向坡下、转弯 / 加减速产生偏移，并通过右杆抵消；在临界位置，操作能力逐渐变差。纯粹把摇杆位置画在圆里无法表达地势，而完全由坡度决定则没有主动配平空间。

常态重心使用机身局部坐标：X 是机身右侧，Y 是机身前方。零是支持中心，幅度达到 1 是失衡阈值。支持半宽和半长来自通行检测器的足迹尺寸，不来自机身 Sprite 的像素尺寸。

坡面投影近似为：

```text
projectedDistanceMeters = centerOfMassHeightMeters × tan(slopeAngle)
                          × slopeBalanceInfluence
halfWidth  = footprintWidth  × 0.5 × usableSupportFraction
halfLength = footprintLength × 0.5 × usableSupportFraction
slopeOffsetLocal.x = dot(downhillWorld × projectedDistanceMeters, bodyRight) / halfWidth
slopeOffsetLocal.y = dot(downhillWorld × projectedDistanceMeters, bodyForward) / halfLength
```

坡角限制到 85°，避免正切发散。惯性取主动速度与地形速度的合速度差分，经最大加速度截断和指数平滑后取反方向，除以 `accelerationForFullOffset` 并乘 `inertiaInfluence`。因此瞬间制动不应直接把单帧差分噪声全部当作真实失衡。

右杆经过径向死区、`magnitude^counterbalanceInputExponent` 曲线和 `SmoothDamp`；操作时与松手回中使用不同时间。目标叠加为：

```text
target = slopeOffset + inertiaOffset + manualCounterbalance + selfRightingInertia
target = edgeResistance(ClampMagnitude(target, 4))
x'' = (target - x) × ω² - x' × 2ζω
ω = 2π × balanceSpringFrequency
```

二阶弹簧积分按不大于约 `1/120s` 的小步细分，单帧参与计算的 `deltaTime` 上限 `0.05s`。边缘阻力把 `edgeResistanceStart` 之外的目标余量压缩为 `excess / (1 + strength × excess)`，不是简单把所有重心硬夹在圈内；真正状态仍能越界。

`PublishState()` 分类为 `Stable(<0.35)`、`Loaded(0.35–0.72)`、`Critical(0.72–1)`、`OutsideSupport(≥1)`。从 `movementInfluenceStart` 起按风险降低驾驶和转向能力；越界触发 `TippedOver` 后冻结常态重心积分，交给侧翻系统。

拍照模式占用右杆时，手动配平输入暂时归零，但坡面和惯性仍继续计算。退出拍照后，需要右杆先回到死区内才重新接受手柄配平，避免最后的取景杆位直接变成一记重心输入。

### 8.4 连续侧翻：90° 为一步，形状与地势决定能否继续

**设计目的：落差和速度应该改变翻滚次数，向坡下翻不能被正常驾驶的坡度限制卡住。** 另一方面，不能为“忽略下坡坡度”而穿过树干、地图边界或未经实现的巨大断崖。因此当前使用独立的 `TryEvaluateTumbleSegment`，不直接复用普通驾驶的完整硬停规则。

越界时保存合速度、重心越界方向和程度。以局部方向 X/Y 绝对值较大的一轴决定前后翻 / 左右翻，以便选择长或宽；**真正的平面位移方向仍保留原始重心世界方向**，不是只能向四个轴向翻。

设机身高度 `H`，本次轴向尺寸 `A`（前后翻取足迹长，左右翻取足迹宽），每一步为 90°：

```text
已完成步数偶数：当前竖直尺寸 H，下一竖直尺寸 A，平移距离 H
已完成步数奇数：当前竖直尺寸 A，下一竖直尺寸 H，平移距离 A
```

当前不是精确的长方体角点绕接触边运动，而是按设计的高 / 长宽交替位移，对每一步起点终点进行平滑插值；Transform 的 Z 保持不变。海拔只参与逻辑能量，不代表物体在 Unity 三维空间真实飞起。

初始单位质量能量取侧翻方向上的**正向**速度分量：

```text
v = WorldSpeedToMapSpeed(max(0, dot(capturedVelocity, tumbleDirection)) × tumbleDirection)
E = 0.5 × effectiveInertiaFactor × v²
    + g × H × balanceOverflowEnergyScale × max(0, balanceMagnitude - 1)
```

每一步的能量门槛含三个部分：把机身重心翻过支撑边所需的高度、路径正向抬升、滚动阻力。设当前 / 下一竖直尺寸为 `h0,h1`：

```text
bodyBarrier = max(0, 0.5 × sqrt(h0² + h1²) - 0.5 × h0)
E_required = g × max(0, terrain.MaximumPositiveRiseMeters + bodyBarrier)
             + rollingResistanceCoefficient × g × distance
             + energySafetyMargin
```

第一次越界已经决定“倒下”，因此首步会把能量至少补到门槛加上 `firstTipCommitSpeed` 对应的余量；否则玩家可能重心已经在圈外，却因为初始静止而完全不翻。后续步骤不再强行补能量，需满足门槛和 `minimumContinuationSpeed` 对应能量中的较大者。

落地计算：

```text
Δh = endSurfaceHeight - startSurfaceHeight + (h1 - h0) / 2
E_beforeImpact = max(0, E - g × Δh - rollingResistanceCoefficient × g × distance)
E_next = E_beforeImpact × impactEnergyRetention
impactLostEnergy = E_beforeImpact - E_next
```

`E_required` 是是否能越过势垒的门槛，不是在上式中再扣一遍的消耗。下坡 `Δh<0` 会补入势能，因此可继续翻；撞击保留率和滚阻消耗能量，最终停下。每步时长由 `distance / max(minimumStepMotionSpeed, sqrt(2E/inertiaFactor))` 算出，再限制在最短 / 最长时间之间。

边界、上方障碍、缺少地形数据和过大的向下 Detail 台阶仍会停止这套接地模型。尤其 `maximumGroundedDetailStepMeters` 限制的是突发落差，不是连续下坡的总降高；当前没有在大断崖处切换完整自由落体 / 碰撞求解。

### 8.5 结束摇摆、360° 恢复与前后朝向反转

**设计目的：消耗完能量不意味着姿态瞬间静止。** 玩家希望最后一下有反向摇摆，且完整翻回正面的结果应允许继续移动。当前仅在下一步**能量不足**时进入 `FinalRocking`；因边界、超时等原因直接停止，并不一定播放这套最终摇摆。

摇摆曲线使用四个平滑段，归一化角度依次为：

```text
t:       0       0.22       0.50       0.74       1
angle:   0  →   -1    →    +0.5  →   -0.2   →   0
```

角幅由剩余能量接近下一步门槛的程度，以及上一落地损失的能量共同决定。在 `0.22` 和 `0.50` 处发布两次逐渐减弱的 `RockImpact`。机身位置偏移属于 `RobotMarkerView` 的视觉层，不额外推进世界位置，也不算增加了一次完整翻滚。

`CompletedStepCount` 计的是 90° 步骤：`4 / 8 / 12` 才是 `360° / 720° / 1080°`。`Settle()` 允许恢复驾驶的条件还包括：至少完成一步、没有在未完成步骤中被中断、Mover 仍由侧翻接管。只看 `count % 4 == 0` 不够，0 步或第 5 步翻到一半不能当作正常回正。

若最终摇摆将以正面朝上结束，`UpdateFinalRockBalanceState()` 在这段摇摆**进行时**以 `1-SmoothStep(settleProgress)` 将显示重心归中；摇摆结束即完成回中，不再追加延迟动画。其他满足回正条件的终止路径保留 `StartUprightBalanceRecovery`，以余速度和衰减振荡接回正在计算的常态重心。

**朝向箭头的目的不是固定指向屏幕上方，而是表示机身正面的表面投影。** `RobotMarkerView.UpdateDirectionIndicatorSurfaceProjection()` 消费侧翻组件的连续四分之一圈进度，在前后翻两次 90° 后把“前”显示在后方，再两次恢复；左右翻则不改变机身前后的意义。侧面朝向镜头时箭头变窄 / 隐去，避免仍像完全朝上的图标。具体投影与屏幕尺寸处理见第 9 章；这里要记住该表现不直接旋转真实 `RobotMover.Forward`，不能从贴图翻转推导驾驶轴改变。

机身叉号只在 `Fallen` 中显示，按贴图有效内容尺寸与机身可见尺寸计算比例，目的在于填满圆形身体内部，而不是按带透明边的整张图片尺寸强行放大。

### 8.6 重心 UI：危险位置、摆动与控制权分离

**设计目的：持续可读，同时表现重心快速变化。** 早期“翻倒就隐藏 UI”会让玩家看不到之后最重要的危险方向；只把点贴在支持圈外又缺少倒下后的悬垂感。后续需求因此提高外侧距离、加粗叉号、增强不透明度，并加入短残影和着陆回弹。

`RobotBalanceView` 创建独立屏幕空间 Canvas，投影玩家中心和重心世界方向。它不属于 Main UI 的侧翻旋转根，因此 Main UI 停在 90° 时，重心圈和点不会跟着变成一套旋转控制坐标。显示数据的优先级为：

```text
SelfRighting.DisplayedBalanceState（扶正正在管理显示）
  > Tumble.TumbleBalanceState（翻滚 / 摇摆 / 回中表现）
  > Balance.CurrentState（常态实际重心）
```

侧翻显示半径以 `outerBalanceMagnitude` 为主要外侧位置。每步中间用 `sin(πt)^inwardSwingSharpness` 制造短暂向内摆动窗口，用能量比例和后续步数衰减幅度，再叠加几何弧形偏移和短暂落地回弹。方向以侧翻方向为中心左右摆，步数奇偶交替摆向；大部分时间仍在圈外，而不是每次完整绕支持圈转一整圈。

这里的外侧距离和轨迹是**表现参数**，不是重心越界门槛。`outerBalanceMagnitude=1.66`、显示上限 `1.9` 不表示侧翻要先超过 1.66；真实触发仍是常态重心幅度 ≥1。修改圈外视觉距离不能用来平衡侧翻难度。

瘫痪时圆点变叉号，当前 `fallenPointCrossSizeOfPointDiameter=1.6`，轮廓 `1.25px`，侧翻 UI 不透明度倍率 `1.2`。符合机械臂支撑条件时即切成黄色实心圆点，不必等右杆节奏达标；黄色由 `RobotBalanceView.selfRightingBalancePointColor` 配置。首次进入圈内、开始回正机身后恢复普通点色。

翻滚残影默认最多 5 个、寿命 `0.16s`；它是快速位移的阅读辅助，不代表多个独立重心或多个物理受力点。

### 8.7 机械臂：安装点、刚性瞄准和双阶段展开

**设计目的：让手和整条臂像一个机械组件，并保留靠近身体的操作空间。** 原始需求中手腕不能独立旋转，手指向目标时要带动整条外臂；增加长度主要由 `Robot_Arm_2` 拉伸承担。后来又增加一段从身体垂直伸出的连接臂，让外臂从连接臂末端以直角展开，而不是一开始整条细线平贴身体。

当前每侧运行时层级的核心关系为：

```text
MarkerVisualRoot
└── ConnectorPivot（身体安装点）
    ├── Body Connector Artwork Root / Robot_Arm_2
    └── Outer Arm Joint（整个外臂瞄准旋转）
        └── Outer Arm Reveal Root（展开 / 回收表现）
            └── Arm Artwork Root
                ├── Robot_Arm_2（伸缩段）
                └── Arm 2 End Joint（随伸缩段端点平移）
                    └── Arm 1 Fixed Assembly / Robot_Arm_1
                        └── Fixed Hand Joint / Robot_Hand
```

正常站立时，以局部前方的左右两侧为安装点；侧翻期间，包括尚未停稳时，则使用锁定的侧翻方向选择四个基本点中最近的相邻两个。算法取绝对分量更大的轴为主点，另一分量符号决定次点，再按相对方向角分配左右。故两臂安装点允许相差 90°，不强制来自直径两端。方向落在轴线上时有确定的符号分支，不随机换边。

可见机械臂切换安装点时用 `SmoothDampAngle` 过渡；完全收起时可直接定位。连接臂默认沿圆形身体外法线伸出，`connectorDirectionDegrees=0`；外臂静息方向相对连接臂镜像转 `perpendicularAngleDegrees=90`。

展开不是简单缩放整个根：连接臂先增长，当进度达到 `outerArmExtendStartNormalized`，外臂再开始展开。回收顺序相反：外臂先收，当其进度低于 `connectorRetractStartOuterNormalized`，连接臂回收到身体。第二条臂有很短延迟，避免完全机械同步；中途松手 / 再按下使用当前进度继续，不必等待完整动画结束。

### 8.8 机械臂输入稳定性、触及距离与美术约束

**设计目的：小幅摇杆动作应该允许细致拉近目标，而不是把机械臂突然甩到另一边。** 只计算“目标点减关节位置然后归一化”，在目标接近关节时会出现很大的角度变化。当前采用进入 / 退出瞄准不同阈值、幅度曲线、角速度上限和平滑共同降低敏感度，不能只靠增大死区解决；过大的死区又会失去贴近身体的操作行程。

目标点为 `CurrentTargetLocal × balanceRingRadiusLocal`。对每条臂从自身外关节指向同一个点，得到“精确瞄准方向”；但低幅度输入先从静息方向逐步混入，而不是任何非零输入都立刻精确交汇。达到 `fullAimMagnitude` 并待角度平滑追上后，两臂才完全朝同一目标点瞄准。这是为灵敏度问题保留的有意缓冲。

死区后幅度控制伸长，额外长度由射线与最大触及圈求交，再扣掉外臂原长得到：

```text
R = balanceRingRadiusLocal × maximumReachOfBalanceRadius
s = outerJointPosition；d = currentOuterDirection
distanceToCircle = -dot(s,d) + sqrt(dot(s,d)² + R² - dot(s,s))
extraLength = max(0, min(distanceToCircle - naturalOuterLength + extensionOffset,
                         bodyDiameter × maximumExtensionOfBodyDiameter))
requestedExtraLength = extraLength × stickMagnitude × outerDeployment
```

因此“最大触及为支持圈 3/4”已经演变为可调触及圈上限，加上独立拉货行程上限；**当前 Prefab 是 `0.808`，而不是历史方案的 `0.75`**，并可能先被半个机身直径的额外行程限制截住。手臂没有要求手掌实际到达目标点；要求的是手 / 外臂方向朝向它。

美术尺寸还存在硬编码约束：源画布中心 `63.5px`、Arm 2 基端 `96px`、外端 `60px`、手中心约 `35px` 用于计算拉伸锚点与原长。替换素材若改变画布、裁剪或部件位置，仅替换 Sprite 不一定能无缝适配；必须复核 `ApplyConnectorLength`、`CalculateMaximumExtension` 和 `ApplyExtension`。

按住机械臂模式时，方向箭头通过约 `0.16s` 淡出；退出约 `0.2s` 淡入。侧翻时也允许操纵机械臂，但拍照模式会阻止进入机械臂模式。当前没有抓取货物、机械手碰撞或关节力求解，拉货行程是为后续交互保留的控制 / 表现基础。

### 8.9 扶正：虚拟支撑与重复推杆节奏

**设计目的：把“撑住—发力—拉回—落地—配平”变成一条能读懂的动作链。** 左杆确定支撑方向，右杆重复用力提供努力感；有效支撑先亮黄色点，表示具备施力条件；节奏达标后才移动重心；方向或节奏丢失则失去进展并落回去，不能只是松开输入后永远保留进度。

**当前限制必须明确：** 所谓“撑墙 / 撑地”是虚拟支撑平面的交互判定。`EvaluateArmSupport()` 仅检查机械臂模式是否按住、左杆重映射幅度是否足够，以及目标方向与侧翻方向的点积；没有 Raycast，也不检测实际墙体、手掌落点或连接臂是否已经碰撞到障碍物。虚拟线面是提示，不是物理 Collider。

侧翻并伸臂时即可显示虚拟平面，位置为侧翻方向上的 `guideDistanceOfBalanceRadius × 重心圈半径`，宽 / 厚按机身直径比例计算，显示随机械臂展开程度渐入。真正的扶正状态机只在 `Tumble.State == Fallen` 时开始，尚在翻滚中不能提前将机器人扶起。

支撑判定：

```text
armsHeld && remappedLeftMagnitude >= minimumArmPushMagnitude
&& dot(normalizedArmTarget, lockedTumbleDirectionLocal)
   >= cos(supportDirectionToleranceDegrees)
```

右杆一次“推”的判定采用重新武装机制：幅度回到 `effortPushRearmMagnitude` 以下才允许下一次；再超过 `minimumEffortPushMagnitude` 时产生一次脉冲。方向需对准侧翻反方向，并且左杆提供的机械臂支撑输入正确，才把时间戳记入队列。

```text
推杆频率 = 最近 effortPushWindowSeconds 内的有效推杆数 / 时间窗长度
```

一直把杆顶在边缘只产生一次，不会每帧计数；摇杆回中是下一次发力的必要部分，回中本身不算方向错误。没有伸臂时不发布发力震动事件；伸臂后方向不合要求的推杆仍可发布 `ForcePulse(Accepted=false)`，但不增加有效节奏。

状态机：

| 状态 | 进入 / 行为 | 下一步 |
| --- | --- | --- |
| `Inactive` | 未瘫痪；不接管重心显示 | 进入 Fallen 后初始化锁定方向 |
| `FallenIdle` | 外侧白叉，未有效支撑 | 左杆支撑方向与幅度正确 → BuildingSupport |
| `BuildingSupport` | 有支撑即黄色圆点；积累有效推杆 | 节奏达标 → PullingBalance |
| `PullingBalance` | 足够节奏时重心缓慢向圈内移动 | 首次到达圈内 → RightingChassis；失败 → ReturningAfterFailure |
| `ReturningAfterFailure` | 变叉号；清空节奏，平滑回原外侧位置 | 回退完成 → FallenIdle |
| `RightingChassis` | 点恢复普通颜色，机身插值回正 | 落地后交回正常驾驶并施加反向惯性 |

### 8.10 扶正进度、失败与反向惯性

达到节奏门槛后，进度按时间推进，较快节奏最多加速到 `1.35` 倍：

```text
cadenceScale = clamp(currentFrequency / requiredFrequency, 1, 1.35)
p += Δt / balancePullDuration × cadenceScale
displayedMagnitude = lerp(fallenMagnitude, recoveredInsideMagnitude, SmoothStep(p))
```

如果支撑或频率短暂不满足，进度先以 `failedReturnDuration` 决定的速度向零回退。超过容错时间才进入完整失败；右杆在发力幅度下方向错误则直接失败。失败时清空时间窗，圆点变叉，幅度用 `1-(1-t)^3` 的缓动回原瘫痪位置，避免突然瞬移。

容错的设计含义不同：`armDirectionLossGraceSeconds` 缓冲左杆略微偏移，`effortCadenceLossGraceSeconds` 缓冲两次发力之间的频率波动；它们不应被理解为完全不损失进度的免罚时间。

`balancePullDuration=3s` 是满进度的基准，不是严格的整套扶正总耗时。当前首次显示幅度 `<=1` 就进入回正，不必等待插值终点 `0.88` 或 `p=1`；节奏超额也可加速，之前建立节奏与之后 `chassisRightingDuration` 还需另算。对外说明应使用“连续努力约数秒”，不能保证所有输入情况下恰好 3 秒成功。

机身回正选择最近的 360° 整数姿态进行视觉插值，不再继续平移一整步。完成后：

1. 侧翻状态归 `Upright`，清理侧翻计数和能量。
2. `RobotMover` 恢复 `Driven`。
3. 实际重心恢复到原侧翻方向的圈内位置，当前幅度 `0.82`。
4. 向原侧翻反方向注入 `1.5` 幅度、`1.2s` 时长的临时重心目标。
5. 该惯性前 55% 时间保持峰值，之后平滑衰减；右杆应及时向这股力的反方向配平。
6. 发布 `RightingLanded` 供剧烈落地镜头 / 手柄冲击使用。

第 3 点以实际参数传递为准：`CompleteSelfRighting` 传入原侧翻方向作为落地重心方向，反方向作为惯性方向；不要只读字段 Tooltip 中的“opposite side”文字。反向惯性回到常态 `RobotBalanceController` 弹簧中求解，若重新越界，仍走普通 `TippedOver` 流程，不是单独强制播放另一段失败动画。

### 8.11 调参与验证入口

本节主要配置均在 [RobotMarker.prefab](AnimalGame/Assets/Prefabs/Resources/Robot/RobotMarker.prefab) 对应组件上；不要只修改 `.cs` 初始化值就认为已覆盖现有 Prefab。

| 组件 / 字段 | 当前资产值 | 设计用途 |
| --- | --- | --- |
| Balance `centerOfMassHeightMeters` / `usableSupportFraction` | `0.9 / 0.98` | 实际重心高度与可用支撑尺寸，影响坡面失衡难度 |
| Balance `maximumCounterbalance` | `0.6` | 玩家最大配平能力 |
| Balance `balanceSpringFrequency` / `balanceDampingRatio` | `2 / 1.3` | 重心追随速度与稳定程度 |
| Tumble `robotHeightMeters` | `1.8m` | 高 / 长宽交替位移与能量几何 |
| Tumble `impactEnergyRetention` / `rollingResistanceCoefficient` | `0.42 / 0.05` | 每次落地剩余能量、沿途耗能 |
| Tumble `maximumStepCount` / `maximumTumbleDuration` | `12 / 10s` | 防止无限翻滚的保险上限 |
| Tumble `finalRockDuration` / 最小最大摇摆角 | `1.2s / 6°–18°` | 最后余势的长度与强弱 |
| Tumble `outerBalanceMagnitude` | `1.66` | 侧翻显示重心远离圈的程度，不改变实际门槛 |
| Arms `leftStickDeadZone` | `0.016` | 当前特意保留的小幅拉近操作；不同于脚本初值 `0.15` |
| Arms `aimEnterMagnitude / aimExitMagnitude / fullAimMagnitude` | `0.12 / 0.06 / 0.7` | 防抖进入退出阈值与完整交汇幅度，均在死区重映射后 |
| Arms `aimSmoothingTime / maximumAimSpeedDegreesPerSecond` | `0.2s / 240°/s` | 整条外臂转动缓冲和限速 |
| Arms `maximumReachOfBalanceRadius / maximumExtensionOfBodyDiameter` | `0.808 / 0.5` | 外圈触及上限与额外拉货行程上限 |
| Arms `connectorLengthOfBodyDiameter / perpendicularAngleDegrees` | `0.2 / 90°` | 连接臂长度与外臂默认夹角 |
| Arms `extendDuration / retractDuration` | `0.4s / 0.3s` | 外臂展开和回收，不是代码初值 `0.3/0.25` |
| Arms `artworkThicknessScale / connectorThicknessScale` | `2.57 / 2.57` | 外臂与连接臂粗度，不直接增加伸长距离 |
| SelfRighting `supportDirectionToleranceDegrees` | `18°` | 左杆支撑朝向容错 |
| SelfRighting `minimumArmPushMagnitude` | `0.9` | 左杆需接近推满才能提供支撑 |
| SelfRighting `effortPushToleranceDegrees` | `38°` | **右杆发力角度当前值；脚本初值为 22°** |
| SelfRighting `requiredEffortPushFrequencyPerSecond / effortPushWindowSeconds` | `3次/s / 1s` | 有效推杆节奏与统计窗口 |
| SelfRighting `minimumEffortPushMagnitude / effortPushRearmMagnitude` | `0.65 / 0.3` | 一次推入与再次计数的回中阈值 |
| SelfRighting `armDirectionLossGraceSeconds / effortCadenceLossGraceSeconds` | `0.14s / 0.55s` | 支撑丢失与节奏不足的容错 |
| SelfRighting `balancePullDuration / failedReturnDuration` | `3s / 0.8s` | 扶正基准努力时间、失败回退速度 |
| SelfRighting `chassisRightingDuration` | `0.46s` | 进入圈内之后的机身回正过渡 |
| BalanceView `selfRightingBalancePointColor` | `(1, 0.78, 0.12, 1)` | 有支撑时的黄色点，可独立改色 |

建议按行为链验证，而不是只检查每个组件存在：

- 在无初速度下重心越界，仍承诺首个合法侧翻步骤；随后是否继续由能量决定。
- 向连续陡下坡翻时不触发驾驶的下坡坡度阻挡；真实边界 / 上方障碍仍应阻止路径。
- 分别测试前后 / 左右翻，确认距离在高与相应长宽间交替，前后箭头每两步反向。
- 能量耗尽后有最终摇摆；4 步完整回正与 3 步侧面瘫痪不同，半步中断不可误恢复。
- 360° 倍数的最后摇摆期间重心已开始回中，结束时同步到位；普通侧面瘫痪则保留外侧叉号。
- 伸臂时箭头渐隐、连接臂先出 / 后收；小幅杆位不会马上甩臂，较大杆位才完全交汇瞄准。
- 瘫痪后左杆支撑正确即可出现黄色圆点；只推一次右杆并保持不动不能持续增加发力次数。
- 节奏不足、左杆偏离、右杆方向错误分别触发正确的进度回退 / 失败反馈。
- 首次拉入圈内后普通点色恢复，机身落地后需及时抵消反向惯性；失败再次侧翻仍能重复进入整条流程。

这些是验证清单，并非本文已执行的自动化或手柄实机测试。当前模型的边界包括：虚拟支撑而非真实墙碰撞、参数化连续翻滚而非三维刚体、机械臂没有抓取 / 关节动力学，以及输入占用由各组件协作而非集中式模式管理器。后续扩展时应先确定要提升哪一层，不要把表现近似误当成已经存在的物理基础。

## 9. 机身表现、镜头、手柄反馈与 UI

### 9.1 设计背景：用反馈解释机身发生了什么

这一组需求不是单纯“让画面更激烈”。机器人主体是二维符号，缺少真实三维机身天然提供的滚转、着陆和遮挡线索。设计者反复加强侧翻镜头与手柄冲击、要求每次着陆有冲击，并让 UI 在翻倒后停在对应的角度，是为了让玩家辨认“正在翻滚”“哪一面朝上”“已经着地而无法驾驶”。最后的小幅摇摆则补上动能尚未完全耗尽的感觉。

与此同时，不能让所有反馈都一样强：撞树先被要求增强，随后又明确改为与撞击速度相关；重心 UI 被要求不随整层 UI 旋转，以免玩家失去扶正操作的参照。维护本章系统要分别考虑事件发生、反馈强度和信息可读性，不能只调一个全局震动倍率。

核心入口：[RobotMarkerView.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMarkerView.cs)、[RobotCameraFollow.cs](AnimalGame/Assets/Scripts/RobotMap/RobotCameraFollow.cs)、[RobotCameraShake.cs](AnimalGame/Assets/Scripts/RobotMap/RobotCameraShake.cs)、[RobotHeightMotionDetector.cs](AnimalGame/Assets/Scripts/RobotMap/RobotHeightMotionDetector.cs)、[HeightMapPlayerSceneBootstrap.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapPlayerSceneBootstrap.cs) 中的 `RobotTumbleUiRotation`。

### 9.2 机身尺寸与方向箭头

**原始问题。** 更换较小机身贴图的目标是让玩家在地图中显得更小，但不连带改变重心、越障和其他已调好的系统。后来要求箭头贴近机身、机械臂操控时渐隐，以及侧翻时不再永远朝着画面前方。因此视图应解释机身状态，不成为新的物理状态源。

`RobotMarkerView` 创建独立的 `markerVisualRoot`，在其中组合身体、填充、方向箭头、拍照相机形态和瘫痪叉号。`bodyDiameter` 与 `visualBodyDiameterRatio` 分开；当前 RobotMarker Prefab 的屏幕尺寸补偿开启，`bodyScreenDiameterPixels = 45`、`visualBodyDiameterRatio = 0.85`。45 是基准屏幕直径参数，不应把所有叠加后的可见像素都精确理解为 45。

正交相机下的补偿计算是：

```text
worldUnitsPerPixel = orthographicSize × 2 / camera.pixelHeight
desiredWorldDiameter = bodyScreenDiameterPixels × worldUnitsPerPixel
markerVisualRoot.scale = desiredWorldDiameter / bodyDiameter
```

补偿改变视图根缩放，不会自动修改检测器的米制支撑矩形或障碍碰撞半径。美术四周透明留白另由 `bodyArtworkVisibleDiameterPixels` 等可见尺寸参数处理，不能直接把整张 PNG 宽度当成机身宽度。

**表面投影式箭头。** 用连续四分之一圈进度 `q` 和转向符号计算 `θ = sign × q × π/2`；`abs(cos θ)` 控制压扁与透明度，`sin θ` 控制沿翻滚方向向机身边缘偏移。左右翻压 X，前后翻压 Y；前后翻到背面时 Y 方向符号翻转。因此两次 90° 前后翻后，机身的“前”出现在反面，再两次才回来。它是二维投影近似，不是三维 Mesh 旋转后投影的结果。

箭头 Alpha 还乘以机械臂 / 生物扫描蓄力隐藏系数与拍照模式可见度。当前机械臂隐藏 / 恢复时长为 0.16 / 0.2 秒。只改投影透明度而忽略这些模式系数，可能把本来应隐藏的箭头又显示出来。

瘫痪叉号只在 `RobotTumbleState.Fallen` 显示。缩放用叉号可见区域的对角线对齐目标机身直径，解决之前“填不满”与“伸出圆外”的矛盾；它不是简单将图片边长拉到圆直径。更换素材后需重新核对 `rolloverSignVisibleDiameterPixels` 和 `rolloverSignDiameterRatio`。

`UpdateDriveBob()` 的行驶摆动、运动尾迹和最后摇摆视觉偏移也在这个视图中。它们不应被当成玩家米制位移或新的碰撞足迹。

### 9.3 相机跟随与缩放合成

**设计目标。** 正常驾驶需要稳定跟随，重心偏移要能影响观察中心；拍照时视线应逐渐靠近取景点；扫描有独立的蓄力缩放。若这些功能各自无条件写同一相机参数，某个效果会覆盖另一个效果，或退出模式时留下错误缩放。

`RobotCameraFollow` 的执行顺序是 200，在 `LateUpdate` 用 `SmoothDamp` 跟随位置、`SmoothDampAngle` 跟随朝向。目标通常来自 `RobotBalanceController.CameraFollowTarget`，拍照时切换到专用 AimFollowTarget，退出拍照再平滑返回。当前 Camera Prefab：正交 Size = 9，位置阻尼 = 0，旋转阻尼 = 0.28，跟随目标旋转开启；不要将脚本位置阻尼初值 0.18 当成当前配置。

`RobotCameraShake` 在顺序 250 的 `LateUpdate` 追加局部位移、Z 旋转和 Size 冲击。基础缩放统一组合为：

```text
composedSize = baseOrthographicSize × scanZoomMultiplier / photoFocusMagnification
finalSize = composedSize × (1 + clampedShakeZoomOffset)
```

每帧先由 Follow 写基准姿态，再由 Shake 加偏移，避免直接把上帧震动当成新基准而累积漂移。新增缩放功能应检查这个合成点，而不是持续在另一个 Update 中抢写 `orthographicSize`。

### 9.4 镜头冲击从哪里来

**原始问题。** 单靠普通减速造成的微小抖动不能解释大质量机器人每翻过一面时的着陆。另一方面，持续按住方向顶树不应无限重复触发完整撞击。

镜头反馈分为两路：

- **离散冲击**：订阅侧翻开始、每次翻滚完成、最终摇摆接触、扶正发力、扶正失败、回正落地；也比较普通驾驶阻挡状态、上一帧速度、三级爬坡进入滑落、虚拟落地等变化。
- **连续振动**：驾驶、地形粗糙 / 失衡、侧翻、扫描蓄满和模式入场产生的小幅或持续运动。

位置、旋转和缩放分别有弹簧位置与速度。`IntegrateSprings()` 将最多 0.05 秒的步长再划分到约 1/120 秒子步积分，最后限幅；`ApplyShakeToCamera()` 将弹簧与持续振动组合。普通与侧翻使用不同上限，避免普通驾驶过度晃动，同时保留侧翻所需的强度。

当前 Camera Prefab 的侧翻关键值：`tumbleImpactMultiplier = 4.5`，最大偏移 1.3 Unity 单位、旋转 22°、缩放比例 0.12。它们是上限或倍率，不表示每帧都达到这些数值。

每次翻滚着陆的镜头强度使用损失的单位质量能量：

```text
e = Clamp01(ImpactLostSpecificEnergy / tumbleImpactEnergyAtFullStrength)
strength = Lerp(tumbleStepMinimumImpactStrength, 1, sqrt(e))
```

当前能量满强度参考 8、最小着陆强度 0.65。着陆事件绕过普通撞击 / 减速冷却，保证连续翻滚不会漏掉中间落地；进入侧翻还会清掉先前普通驾驶弹簧，避免旧冲击混入新阶段。

### 9.5 撞树与手柄震动

**设计演变。** 起初只有高速撞树才有轻微反馈，设计者要求明显增强；随后强调低速碰撞与高速撞击必须有差别。因此现在的方向是降低触发门槛、保留可感知的低速底值，并继续随速度增长，不是全部拉满。

实体阻挡首次出现或原因改变、且普通冲击冷却允许时，读取**硬停前保存的上一帧世界速度**，避免 HardStop 已将车速清零后无法感知撞击：

```text
t = InverseLerp(minimumSpeed, max(minimumSpeed + 0.01, fullSpeed), incomingSpeed)
strength = Lerp(minimumStrength, 1, Clamp01(t)^exponent)
```

Camera Prefab 当前障碍参数：最小速度 0.08、满强度速度 3.4（世界单位 / 秒）、底值 0.18、指数 0.9；位置 / 旋转 / Size 冲击参数为 0.26 / 3.8° / 0.022。这里不是撞击表面法向速度的完整碰撞动力学，斜擦也主要由进入阻挡时的速度模长决定。

手柄低频 / 高频马达另有映射与包络，不是直接把镜头角度原样发给硬件。普通输出采用 Attack / Release 平滑和最低输出阈值；树干接触与侧翻落地会抬高当前输出，绕过通用 Attack 的等待，保留第一帧冲击。

| 反馈 | 当前 Prefab 的主要值 | 目的 |
| --- | --- | --- |
| 侧翻持续低频 / 高频 | 0.95 / 0.82 | 保持翻滚重量感，不只在开始震一下 |
| 每次侧翻落地低频 / 高频 | 1 / 1 | 为每个明确接触提供强冲击 |
| 侧翻落地峰值保持 / 总时长 | 0.24 / 0.95 s | 让短促事件有足够可感知持续时间 |
| 障碍低频 / 高频 | 0.95 / 0.8，乘速度强度 | 高低速仍有区别 |
| 障碍峰值保持 / 总时长 | 0.12 / 0.55 s | 接触冲击后衰减 |

`AdaptiveGamepadRumble` 按 `AdaptiveLegacyGamepadInput.ActiveFamily` 选择 Windows XInput 或 Sony Input System 后端，切换后端时停止旧设备输出。Sony 有独立校准和普通 / 障碍 / 侧翻上限。马达命令最终在 0…1，软件不能无限突破硬件最大幅度；要增强“冲击感”还可检查持续时间、包络和事件是否被抑制。

失焦、暂停、禁用或销毁时有停止输出路径。注意当前 `enableCameraShake = false` 的分支也会停止手柄震动：它不是仅关闭屏幕表现而完全保留所有触觉反馈的独立开关。支持 Sony 的代码依赖 `ENABLE_INPUT_SYSTEM`，不能凭 Prefab 开启震动就保证所有平台与连接方式可用。

### 9.6 虚拟离地与落地检测

**工程解释。** 地图是二维高度采样，无法依赖真实机身 Rigidbody 垂直落地事件。为了让快速越过地势下降处仍有失重 / 着地反馈，`RobotHeightMotionDetector` 在逻辑高度空间维护 `VirtualBodyHeightMeters`、垂直速度和离地状态。

支撑时根据前后帧 Surface 高度估算地面垂直速度并平滑；按虚拟重力预测机身高度。地面确实降低、平面速度足够、地面远离速度与分离高度均达门槛时进入 Airborne。空中积分重力；机身重新接近地面且相对接近速度为正时落地，满足最小离地时长后产生 `RobotLandingImpact`，包含冲击速度、强度、离地时长和落差。

这套检测用于状态与反馈，不是将机器人 Transform.z 设成抛物线。侧翻有自己权威的落地事件，普通虚拟落地不能在同一次侧翻接触上重复叠加反馈。

### 9.7 整层 UI 翻滚与例外

**设计演变。** 最初要求整层 UI 随方向旋转，随后明确不是小角度晃动：一次侧翻对应 90°，翻到哪一面就停在哪个角度。之后又要求重心 UI 不旋转，调试高度信息也保持独立。整层翻滚表达机身朝向，重心显示则保留操作参照，两者承担不同信息任务。

`RobotTumbleUiRotation` 在顺序 350 的 LateUpdate 读取 `ContinuousQuarterTurnProgress`，旋转角为 `screenQuarterTurnSign × progress × 90°`。侧翻开始时根据世界方向在屏幕中的投影锁定符号，不随随后相机旋转反复重判；Upright 时回到 0°。

它每 0.5 秒查找非 WorldSpace 的根 Canvas，创建 `Tumble UI Rotation Pivot` 并将 Canvas 的直接子节点移入。按 `RobotBalanceView.BalanceCanvasName` 排除重心 Canvas；只对 MainUI 固定旋转 Pivot 的初始位置，其他界面保留自身定位规则。

这是一套广泛接管根 Canvas 的实现。新增不应旋转的界面必须检查排除规则，不能假设“新建一个 Canvas 就自动独立”。IMGUI 不属于 Canvas，Bootstrap 用 GUI.matrix 单独处理旋转；地形调试面板在恢复矩阵后绘制，FPS 则有不同调用位置。

### 9.8 Main UI 范围与显示同步

**原始问题。** Main UI 美术放大后，地表显示边界、扫描波、动物 / 植物裁剪不能继续沿用旧圈半径，否则出现圈内空缺、圈外漏出和效果尺寸错位。需求是让一套视觉范围的消费者跟随它，不是逐个手调到看起来差不多。

`ScanChargeUI.uiRingVisualReference` 指向 Main UI Artwork。`GetUiRingScreenRadiusPixels()` 将局部中心以及两个轴上的半径点变换到屏幕，取两轴中较小半径，防止非均匀缩放越出圆圈；`GetUiCenterScreenPoint()` 则返回实际屏幕中心。`SynchronizeUiArtworkScale()` 同步扫描控制图形的缩放。

消费者包括地图地表材质、生物信号裁剪、`PlayerUiOrganicVisibility` 的植物和动物共享裁剪，以及地形扫描的范围。后者构建采样网格仍采用屏幕中心，当前并不意味着任意平移整个 Main UI 后所有系统都已完全适配；这与单纯放大居中的圆圈是不同需求。

源码：[PlayerUiOrganicVisibility.cs](AnimalGame/Assets/Scripts/Rendering/PlayerUiOrganicVisibility.cs)、[PlayerUiOrganicSprite.shader](AnimalGame/Assets/Shaders/PlayerUiOrganicSprite.shader)。共享服务登记 SpriteRenderer、分配裁剪感知材质，并设置中心 / 半径 / 边缘柔度 Shader 全局参数。裁剪仅影响显示，**不会停掉圈外动物 AI，也不是永久发现范围或物理边界**。全局只有一组圆，不能直接视为分屏多玩家方案。

右上角 `ROBOT TERRAIN DATA` 由 Bootstrap 的 `showRobotTerrainData` 控制，当前三个玩家相关场景均关闭；FPS 独立，当前 Rocky 场景仍开启、测试场景关闭。关闭信息框不停止通行检测，也不应通过删除检测组件实现。

### 9.9 调整与验收

先在 RobotMarker 调机身 / 箭头表现、Camera Prefab 调镜头与震动、MainUI 调界面范围；不要把碰撞尺寸当作美术尺寸。验证低速 / 高速撞同一树、连续多次侧翻的每次着地、最后摇摆衰减、单次侧翻后 UI 停在 90°、重心 Canvas 保持不转，以及切出应用后马达停止。更换 Main UI 尺寸后，同时检查地表、植被、动物和扫描边缘。

## 10. 地形扫描、生物认知与拍照

### 10.1 设计目的与数据边界

扫描与拍照解决的不是同一个“看见”问题。地形扫描提供通行信息；生物扫描让未知动物退出认知缺失状态；拍照则在快门时刻判定取景框拍到了谁，并展示该物种的结果。设计者明确要求扫描过的动物不再过一段时间又恢复未知，而照片展示暂不生成游戏截图，改用物种预设照片库。

工程上，输入手势、检测数据和显示分开：`ScanChargeUI` 路由扫描输入，`TraversalScanOverlayUI` 生成通行标记，`BioScanController` 负责生物检测波，`DiscoverableEntity` 持有认知状态；拍照由 `PhotoModeController` / `PhotoModeUI` / `PhotoResultUI` 分别管理流程、取景表现和结算。

### 10.2 扫描输入：短按与长按不是两套竞争监听

源码：[ScanChargeUI.cs](AnimalGame/Assets/Scripts/RobotMap/ScanChargeUI.cs)。当前 LB / L1（键盘 E）的手势由一个入口判断：

| 输入过程 | 当前结果 |
| --- | --- |
| 一次短按后松开 | 等待第二次短按，不立即扫描 |
| 两次短按释放落在 0.3 s 窗口内 | `TerrainScanRequested`，启动地形扫描波 |
| 持续按住至少 0.25 s | 开始生物蓄力，清掉短按候选 |
| 蓄力未完成就松开 | 取消，机械雷达收回、镜头恢复 |
| 蓄满后继续按住 | 保持准备态与蓄满反馈 |
| 蓄满后松开 | `FullyChargedBiologicalScanReleased`，发出生物波 |

这些是代码现状的手势说明；不是将未提供完整历史背景的手势选择追认为最初设计要求。统一路由的作用是避免同一个 LB 同时被两个系统解释为不同指令。

MainUI 当前 `maximumChargeDuration = 0.2 s`，而脚本初值为 1.5 s。进入蓄力前另有 0.25 s 长按门槛，正常完整按住约 0.45 s 加帧调度，不是看到 Inspector 的 0.2 就意味着全部过程只有 0.2 s。

UI 状态为 `Idle → Charging → Charged → Releasing → Idle`，环和相机还有各自阶段。动画 `Scan_Idle / Scan_Hold / Scan_Release` 由 `SampleAuthoredState()` 按归一化进度采样；不只依赖 Animator 自动转场。拍照控制器锁定输入时，扫描手势被取消且不会把中断的按住误当成短按。

### 10.3 地形扫描快照与局部更新

**工程目的。** 满屏无差别标记会覆盖地图纹理和玩家，整张图同步做昂贵路径检测也会卡住扫描。当前采用随波揭示、重点区域选择、米坐标快照和分帧重查，以保留地图可读性与反馈时序。

源码：[TraversalScanOverlayUI.cs](AnimalGame/Assets/Scripts/MapTest/TraversalScanOverlayUI.cs)、[TraversalSignsGraphic.cs](AnimalGame/Assets/Scripts/MapTest/TraversalSignsGraphic.cs)。

一次扫描先捕获当前闭合区域，建立 UI 圈内屏幕采样网格，排除玩家中心，按半径排序。波向外扩展时才处理对应候选；每帧同时受数量和毫秒预算限制。候选转到地图坐标后：

1. 用 Surface 高度中心差分估算梯度。
2. 以 `abs(height - nearestContourHeight) / gradientMagnitude` 估计到等高线的距离，挑选近线候选。
3. 对当前闭合区域内不可通过的候选建立种子，将附近一定米数内的候选纳入。
4. 最终显示前再从玩家位置到候选点调用通行路径检测，得到相对玩家的可通过状态。

这不是寻路：直线路径结果不代表绕路也不能到达。标记存地图米坐标，渲染时再投影屏幕，不是永远贴在扫描时的屏幕像素位置。

当前 TraversalScanOverlay Prefab：网格间距 56 参考像素、最多 120 个标记、扫描计算每帧最多 32 次 / 1.75 ms，扫描完成后的存活时间 3.5 s。周期重查间隔 0.75 s；玩家移动还会触发受位移门槛和预算限制的重查。当前 `enablePeriodicRefreshBreathing = false`，周期重查不进行整组淡出；`enableChangedStateBreathing = true`，移动触发的单标记状态变化会在淡出低谷替换结果再淡入，避免图标突然换成相反符号。两条更新路径不能混为同一种呼吸动画。

`Q` 打开的 [TraversalOverlayUI.cs](AnimalGame/Assets/Scripts/MapTest/TraversalOverlayUI.cs) 是独立的全网格调试显示，默认隐藏。关闭 Q 网格不会关闭正式扫描快照。

### 10.4 生物检测波与永久认知

**历史需求。** 被生物扫描识别的动物应永久退出认知缺失；否则玩家已经完成的观察又随临时计时失效，难以建立稳定的认知进展。“永久”在当前实现中需要准确限定为实体生命周期内不自动到期，不是已经完成跨存档持久化。

源码：[BioScanController.cs](AnimalGame/Assets/Scripts/RobotMap/BioScanController.cs)、[DiscoverableEntity.cs](AnimalGame/Assets/Scripts/Discovery/DiscoverableEntity.cs)。雷达机械臂展开、准备点形成和粒子发射是表现；实际命中用独立的扩张圆环，粒子不会因撞到动物而停下。

每帧记录前后波半径，将厚度扩展后的环带与实体圆相交：

```text
inner = max(0, previousRadius - waveHalfThickness)
outer = currentRadius + waveHalfThickness
相交条件：distance + entityRadius >= inner 且 distance - entityRadius <= outer
```

这样大步长时波跨过动物也能被检测，不依赖稀疏可见点恰好碰到精灵。每次波用实例 ID 集合去重；动物调用 `SetDiscovered(true)`，非动物使用 `RevealTemporarily(duration)`。隐匿且 `AnimalAgent.IsPresent = false` 的动物不可扫描。扫描距离 / 波速使用世界单位，实体碰撞半径字段为地图米并转换；不要将两者原值直接比较。

`DiscoverableEntity` 保存 `isPermanentlyDiscovered` 与临时剩余时间；`IsDiscovered` 是两者的或。`DiscoveryChanged` 仅在综合可见认知状态改变时触发。对象禁用再启用不会按临时计时自动取消永久认知，但销毁重建会重新从 `startDiscovered` 初始化；`discoveryId` 当前不是一个已接入磁盘存档的键值数据库。

日常潜水还要与受惊躲藏区分：麝鼠日常水下阶段仍处于 `Daily`、`IsPresent = true`，生物扫描不按 Renderer 是否隐藏过滤它；照片检测另有 Renderer 可见度检查。因此当前不能保证“肉眼看不见的动物一定无法扫描”。

生物波检测没有使用地形射线、树木遮挡或 UI 裁剪来过滤目标；UI 圈限制的是信号显示，不等于探测算法只承认画面可见像素。后续若要求遮挡生效，需要在检测层明确添加规则，而非只更改 Shader。

### 10.5 拍照对焦：按住完成准备，再次按下才拍

**原始设计。** 玩家进入拍照模式后，按住 RB 完成约 0.75 秒对焦；提前松开清空进度，防止零碎点击累计；完成后再按一下才拍照。Frame 放大再缩回、Aim 从大到小收束、镜头靠近、框外变暗，都用来让玩家明确感到“正在锁定这个画面”。取消也要平滑恢复，不能因逻辑归零而视觉瞬移。

源码：[PhotoModeController.cs](AnimalGame/Assets/Scripts/RobotMap/PhotoModeController.cs)。外层状态为 `Inactive / Entering / Active / Exiting`，内层为：

```text
Framing --RB按下--> Focusing --保持0.75s--> Focused
                     └--提前松开--> Framing，逻辑进度归零
Focused --松开使shutterArmed=true，再按RB--> Capturing
Capturing --闪光结束--> Reviewing（有结果）或 Framing（无结果）
Reviewing --B返回--> Framing
```

进入 / 退出拍照用手柄西侧面键（Xbox X）或 P，对焦 / 快门用 RB 或 Space。进场要求正常驾驶模式、未侧翻、未被机械臂占用；发生侧翻会立即退出。进出场、对焦、已对焦、拍摄和回看阶段锁定驾驶输入；正常 Framing 不要误写为始终锁车。

取景右杆控制机器人局部前方楔形范围内的二维位置：死区后幂曲线调节速度，纵向限制最近 / 最远距离，横向限制在该深度允许的范围内。进入时要求右杆先回中再接受输入，避免之前的重心输入直接把取景框甩走。Focus 完成后右杆幅度超过 `focusRetargetCancelDeadZone` 会取消对焦以便重新取景。

对焦与闪光计时采用 `Time.unscaledDeltaTime`，取景移动使用 `Time.deltaTime`。逻辑进度 `FocusProgress01` 与表现进度 `FocusPresentation01` 分开：取消立即清空前者，后者 SmoothDamp 回 0，所以视觉缓退不代表保留了可以继续累积的对焦进度。

### 10.6 Frame、Aim、暗化与白闪如何同步

源码：[PhotoModeUI.cs](AnimalGame/Assets/Scripts/RobotMap/PhotoModeUI.cs)、[PhotoFocusDim.shader](AnimalGame/Assets/Shaders/PhotoFocusDim.shader)、[PhotoRangeDim.shader](AnimalGame/Assets/Shaders/PhotoRangeDim.shader)。

Frame 在对焦进度 `p` 时的倍率：`1 + (1.2 - 1) × sin(πp)`，半程达到 1.2，末端回到 1。Aim 在前 8% 进度逐渐消失并放大，随后从 3.5 倍向 1 倍收束，到 24% 进度完成重新显现。使用短暂淡出而非立刻删除对象，是对“开始消失”的平滑动画实现。

当前配置位置分工：

| 参数 | 当前值 | 资产 / 组件 |
| --- | --- | --- |
| `focusDuration` | 0.75 s | RobotMarker / PhotoModeController |
| `focusZoomMagnification` | 1.3 | 同上 |
| `focusCancelBlendDuration` | 0.25 s | 同上，镜头等表现回退 |
| `focusFramePeakScale` | 1.2 | MainUI / PhotoModeUI |
| `focusAimStartScale` | 3.5 | 同上 |
| `focusOutsideBrightness` | 0.3 | 同上 |
| `focusCancelVisualBlendDuration` | 0.18 s | 同上，Frame / Aim回退 |

取景前跟随 Aim 偏移的比例由 0.5 逐渐到对焦后的 1；相机放大通过第 9 章的统一 Size 合成完成。框外压暗用黑色覆盖层，目标 Alpha 是 `1 - 0.3 = 0.7`，不是提高材质曝光。它与拍照模式的楔形范围暗化是两套遮罩；原本已被范围层压暗的区域，其最终亮度不保证刚好剩 30%。

白闪当前分为 0.04 s 淡入、0.02 s 保持、0.14 s 淡出。`PhotoCaptured` 在白闪首次达到峰值时只触发一次。**与“物理按下快门的同一瞬间”有细微区别：当前命中快照在按下后约 0.04 s 的闪光峰值获取**，不是按键边沿立刻冻结目标；高速运动目标的严格快门时序若有新要求，应改这个采样时机。

### 10.7 拍到谁：取景几何与主目标选择

**设计目标。** 只有快门时取景框内确实包含对应动物，才展示其专属照片结果，不能因为动物在附近就结算；同时照片不需要重建当前游戏像素，只需选出对应物种的预设素材。

源码：[AnimalPhotoSubject.cs](AnimalGame/Assets/Scripts/Animals/AnimalPhotoSubject.cs)、[PhotoResultUI.cs](AnimalGame/Assets/Scripts/RobotMap/PhotoResultUI.cs)。`PhotoResultUI` 订阅快门事件，在闪光结束前建立 `pendingResult` 并请求 Review，待控制器真正进入 Reviewing 才显示。

检测步骤：

1. 从 `PhotoModeUI.TryGetCaptureFrameScreenCorners()` 获取实际 Frame 的四个屏幕角点，按 `frameInsetNormalized = 0.025` 内缩。
2. 遍历 `AnimalPhotoSubject.Active`，排除未启用、不在场、没有有效包围盒或没有可用显示 Renderer 的对象。
3. 将配置的 `photoBoundsRenderers` 合并 Bounds 投影为屏幕矩形，排除相机背面及过小目标。
4. 对目标矩形与取景四边形做多边形裁剪，求交集面积。`coverage = intersectionArea / subjectArea`，不是动物占取景框面积的比例。
5. 通过门槛后，以覆盖率、居中程度和相对尺寸加权，选最高分的一个主目标。

当前门槛是目标最长边至少 24 px、面积至少 400 px²、自身覆盖率至少 18%；权重为 0.55 / 0.3 / 0.15。一张照片不是同时为框中所有动物分别弹出结果。

判定使用包围盒，不检查精灵逐像素透明区域、实际树冠遮挡或 GPU 裁剪后的可见比例，也没有要求动物必须已被生物扫描认知。因此“拍到”当前是几何资格判定，不是完整的光学可见性检测。

### 10.8 预设照片库、参考图演示与封存 UI

**设计演变。** 最初设想照片结算图展示动物、认知和奖励；设计者随后明确不要从游戏数据生成真实照片，改成每种动物 Prefab 的预设照片库随机取图。动态结算 UI 出现黑屏后，又要求暂时封存该 UI，只显示麝鼠 Reference Picture，按 B 返回。文档必须描述最后实际启用的演示路径，而不是将仍在代码里的完整布局当成当前产品表现。

`AnimalPhotoSubject.resultPhotoLibrary` 是 `AnimalResultPhoto[]`，每项包含 Sprite 和 `normalizedCrop`。Crop 会与 Sprite 在纹理中的矩形组合得到 RawImage 的 UV，不是把裁剪值当世界坐标。选择时过滤空图，多张有效图时避免同一 `SpeciesId` 连续选到相同索引；只有一张则正常重复。照片库位于动物 Prefab / 场景实例的这个组件，不在 PhotoModeController。

当前 MainUI：`showReferenceLayoutOverlay = true`，`referenceLayoutSprite` 指向 `Arts/Muskrat_Reference_UI_Photo.png`。`IsReferencePictureOnlyMode` 成立时：

- 仍进行动物取景命中和快照流程，但不要求照片库非空。
- 显示整张参考图，关闭动态背景与内容，不执行动态入场动画。
- B / Escape 或手柄东侧面键返回取景；保存键不生效。
- **该开关是全局结果模式，不按物种筛选。** 其他动物若满足拍照条件，也会显示同一张已绑定参考图；不能描述为已经实现每个物种各自的演示 UI。

关闭参考图模式会进入保留的动态照片 / 文字布局，并要求有效照片库；这只是切换现有代码路径，不代表原黑屏问题已经通过完整回归或正式 UI 已验收。恢复前应单独验证层级、字体、布局与所有物种素材。

动态分支的 `PhotoResultSnapshot` 记录物种字段、选择的照片、动物位置和海拔、覆盖率、时间及奖励数字；位置不是玩家拍摄位置。`PhotoAlbumService` 目前只是静态内存列表，Y 保存并不写磁盘、导出 PNG 或发放到完整经济系统。图鉴 / 奖励文字也不能当成已完成相应系统的证据。

### 10.9 调整与验收

新动物需要照片组件、明确的 Bounds Renderers、稳定 SpeciesId 和有效照片库；先检查当前是否在全局 Reference Picture 模式。验证框外动物不结算、框内不同尺寸 / 覆盖率选择、多个动物只选一个、躲藏动物不可拍、未完成对焦松开后从零重来、完成后必须松开再按才拍、退出回看不会重复触发快门。

生物认知要分别验证“同一实体离开范围再返回仍已知”和“重载场景是否保留”：目前前者属于实现目标，后者需要新增存档机制，不能靠延长 temporaryRevealDuration 解决。所有这些是文档给出的检查建议，本次未启动游戏执行。

## 11. 固定地图编辑、地表表现与高度图生产

本章源码入口：[HeightMapLevelAsset.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapLevelAsset.cs)、[MapTestSceneController.cs](AnimalGame/Assets/Scripts/MapTest/MapTestSceneController.cs)、[HeightMapSurfacePainterEditor.cs](AnimalGame/Assets/Editor/HeightMapSurfacePainterEditor.cs)、[HeightMapStaticWaterPainterEditor.cs](AnimalGame/Assets/Editor/HeightMapStaticWaterPainterEditor.cs)、[DynamicHeightContours.shader](AnimalGame/Assets/Shaders/DynamicHeightContours.shader)。

### 11.1 从一次性地图演示变成可持续制作的关卡

**设计背景：关卡不应该每次运行都重新变成一张无法编辑的临时地图。** 最初地图承担的是验证驾驶、坡度、扫描与侧翻的作用；加入正式落基山脉地图、树木和灌木后，需要像关卡编辑器一样，在固定地形上反复摆放、调整和保存内容。地图放大也应保留已有玩家系统，而不是为了新地图复制一份驾驶和侧翻代码。

因此，“固定地图”要解决的是三件不同的事：地图的参数和作者绘制内容有持久来源；场景中放置的对象能与该地图对应；运行时仍通过统一的地图查询接口服务玩家和动物。它不等于保存 Unity 运行时生成的每个临时对象，也不等于把地图制作成一个包含所有内容的巨型 Prefab。

**当前实现：一个地图资产，加上引用它的场景和可复用 Prefab。**

| 层次 | 保存内容 | 主要文件 / 类型 |
| --- | --- | --- |
| 地图定义 | 高度图、物理尺寸、高度范围、平滑参数、等高线样式 | `HeightMapLevelAsset` |
| 地表作者数据 | 每格地形 ID、材质表、过渡和闭合区域 Alpha 参数 | 同一地图资产中的 Editor 字段 |
| 地表成品 | 完整地图范围的静态 RGBA 图 | `<地图资产名>_SurfaceVisual.png` |
| 水体作者数据 | 每格水深、最大深度、通行深度、波纹参数 | 地图资产中的 `staticWaterDepthMap` 等字段 |
| 水体成品 | 水域范围与归一化深度 | `<地图资产名>_WaterMask.png` |
| 场景布置 | 地图控制器、出生点、树木 / 灌木 / 动物实例 | 引用地图资产的 `.unity` 场景 |
| 可复用对象 | 机器人、植被、动物等组件和默认配置 | 相应 `.prefab` |

`MapTestSceneController.ApplyFixedLevelAsset()` 把固定资产参数应用到地图控制器；玩家仍调用控制器的坐标与高度查询方法。这样，增加地图主要改变数据，不改变调用方约定。控制器内部生成的高度数组、预览 Texture 和地图 Sprite 仍属于可重建的运行时 / 编辑器预览资源。

**编辑入口与操作顺序：**

1. 在 Project 窗口通过 `Create > Animal Game > Height Map Level` 建立地图资产，或复制已有地图资产作为新地图的起点。
2. 设置 `Height Map`、物理长宽、高度范围、平滑和显示参数；让目标场景的 `MapTestSceneController` 引用这个资产。
3. 在 Scene 中摆放对象；需要地图米制锚点时，选择对象，执行 `Animal Game > Level > Anchor Selected Objects To Height Map`。
4. 通过地表 / 水体 Painter 编辑地图层，并保存场景与资产。复制地图资产后还要重新烘焙，使成品 PNG 写到新资产自己的路径。

对象锚定由 [HeightMapPlacedObject.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapPlacedObject.cs) 与 [HeightMapPlacedObjectEditor.cs](AnimalGame/Assets/Editor/HeightMapPlacedObjectEditor.cs) 提供。`Capture Current Position` 把当前 Transform 换算成地图米制位置并记录采样高度；`Snap To Stored Position` 执行反向换算。编辑器会在对象 Transform 被移动时捕获新位置，但不要把它理解为“任意改变地图比例后所有对象都必然自动重排”的完整关卡重定位系统。

**出生点可视化的设计目的**是消除反复填写米制坐标、运行游戏、再退出调整的试错过程；同样也让“出生后立刻遇到 BLOCK”可以与地形形状一起排查。选择带 `HeightMapPlayerSceneBootstrap` 的对象，即可拖动 Scene 中的中心 Handle；Inspector 的 `Frame Player Spawn In Scene` 会定位到出生点，并提示地图矩形之外或可玩轮廓之外的位置。实现见 [HeightMapPlayerSceneBootstrapEditor.cs](AnimalGame/Assets/Editor/HeightMapPlayerSceneBootstrapEditor.cs)。该标记是 Editor 工具，不是在游戏里生成一个可碰撞实体。

### 11.2 地表纹理为什么保存为静态成品

**原始目的不是增加会随玩家变化的地形，而是让玩家读懂这里是什么地面。** 草地、碎石、岩石等区域由设计者一次性定义，之后始终位于地图上的同一位置；玩家移动、扫描、侧翻时，不应重新随机生成材质边界，也不应导致某处纹理变成另一种地形。

同时，画面采用仪器 / 地图式表现：地形 Texture 只在玩家 Main UI 的范围内显示，范围外不显示这些地表纹理。这里动态变化的是“可见窗口”，不是地图内容。把地图纹理固定在世界 / 地图位置上，再用 UI 窗口揭示它，才能让玩家感到自己正在探索一张稳定存在的地图，而不是带着一块纹理蒙版到处走。

**实现选择：把昂贵的材质组合、交界噪声、闭合区域渐变提前计算。**

```text
地图资产：地形 ID 数组 + Palette + 过渡 / 区域参数
    → Editor 构建临时图集、邻接距离图、闭合区域 Alpha 图
    → BakeSingleTerrainSurface.shader 合成
    → 同目录 <LevelName>_SurfaceVisual.png
    → 资产 BakedSurfaceVisual 引用
    → 运行时地图 Shader 采样 + UI 圆形显示窗口
```

`HeightMapSurfaceBaker.Bake()` 使用临时 RenderTexture 执行一次 `Graphics.Blit`，读回 RGBA32 并保存 PNG，然后设置资产引用、更新烘焙版本和保存资产。临时材质与纹理在 `finally` 中释放。这里使用 GPU 完成 **Editor 烘焙**，不意味着每帧都在重新合成地形。

运行时，`RefreshSurfaceMaterialSettings()` 只把成品图、UI 中心 / 半径 / 边缘宽度等传给地图材质。中心与半径来自 `ScanChargeUI.GetUiCenterScreenPoint()` 和 `GetUiRingScreenRadiusPixels()`，因此 UI 尺寸改变时不必重新绘制地图。没有 UI 引用时才使用屏幕中心和默认半径。

`DynamicHeightContours.shader` 以屏幕像素距离计算显示系数，乘到成品地表的 Alpha 上。编辑模式关闭这个范围限制，使整个地图都能被编辑；Play 模式才限制在 UI 圈内。纹理采样坐标始终是地图 UV，不跟随玩家重置。

**性能边界：静态内容不等于零渲染成本。** 每帧仍需显示地图、采样纹理和计算圆形窗口；减少的是全图地形混合、噪声制作和区域距离场的运行时工作。4096² RGBA32 无 mipmap 的像素数据约为 64 MiB，2048² 约为 16 MiB，未包含导入缓存和其他地图纹理。烘焙分辨率、地图面积和目标设备预算要一起考虑。

### 11.3 多种地形的数据模型：ID 而不是四个通道

**设计演变：** 最初只需要一种材质测试；随后加入多种纹理，并预计地形类型可能超过十种。如果用一张 RGBA 权重图的一通道对应一种材质，就会很快遇到数量限制；继续堆通道还会把运行时混合变得复杂。当前方案把“地图某处属于什么地形”与“最后显示成什么像素”分开。

作者数据 `surfaceMask` 实际是 `byte[]` 地形 ID 网格，不是 RGBA 权重图：`0` 表示没有地表纹理，`1..255` 对应 Palette 中的稳定 ID。每个作者格子只有一个主地形；边缘混合在烘焙时由相邻区域推导，不要求每格保存十几种权重。

Palette 条目 `TerrainSurfaceDefinition` 包含：

| 属性 | 作用与调整目的 |
| --- | --- |
| `Terrain Id` | 作者地图引用的稳定身份；不是条目的显示顺序 |
| `Display Name` | 编辑器可读名称 |
| `Pattern Texture` | 平铺素材 |
| `Tint`、`Opacity` | 控制颜色与整体透明度，不修改源图 |
| `Tile Size Meters` | 一次完整纹理重复覆盖的地图米数 |
| `Transition Mode` | `Hard / Alpha / Noisy / Hybrid` |
| `Transition Width Multiplier` | 对该材质局部调整全局过渡宽度 |
| `Noise Strength Multiplier` | 对该材质局部调整边缘噪声位移 |

`EnsureSurfaceAuthoringData()` 检查 ID 唯一性、将重复 ID 分配到空闲编号、限制最多 255 项，并保留旧单材质数据迁移逻辑。Palette 非空后，正常调整应针对各条目；`Legacy Single-Surface Migration` 中的旧 `surfaceTileSizeMeters` 等字段不是所有新条目的统一控制器。

**不要随意修改已经画进地图的 ID。** 改名称 / 素材 / 不透明度会让原区域继续对应同一材质身份；改 ID 则可能使已有字节值失去对应条目。新增第九、第十种材质可以新增唯一 ID，不应借用已有 ID 覆盖其他区域。把图片复制进 Arts 文件夹也不等于该图片已经进入 Palette，仍需增加条目并指定素材。

当前两个地图资产都保存了八个条目：`Grid Test`、`Grass Test`、`Grass`、`Grassland`、`Gravel`、`Rock`、`Water`、`Web`。这里的 `Water` 只是地表纹理的一种名称；画这个 ID **不会产生静态水深或水体通行判定**，物理水域必须使用下文的 Static Water Painter。

**编辑操作：** 打开 `Animal Game > Level > Terrain Surface Painter`，或选择地图资产后点击 `Open Terrain Surface Painter`。指定 `Fixed Map Asset`，选择 `Paint Terrain` 中的条目，在 Scene 左键拖动；`Shift + 左键` 擦除到 ID 0，`Alt + 鼠标` 保留 Scene 导航。刷子半径以地图米计量，连续拖动会按半径比例插入中间落笔点，避免高速拖动留下断点。

一次笔画结束后自动烘焙；`Fill With Selected`、`Clear All Terrain` 和 Undo / Redo 也会走相应烘焙流程。修改 Palette、Tile Size、闭环参数或替换同路径贴图后，显式点击 `Bake Current Terrain Map` 最可靠：不能把“参数已保存”误认为“所有成品 PNG 已更新”。

烘焙生成器还保存版本号。`HeightMapSurfaceBakeUpgrade` 在编辑器初始化延迟回调以及回到 Edit Mode 时检查旧版本成品并升级；这不是对每一种资源内容变化的通用依赖构建系统。

### 11.4 交界过渡：自然边界，但不要碎线和脏点

**设计问题来自稀疏的线状素材。** 格线、碎石线和草纹包含大片透明区；如果为了制造“噪声过渡”，在每个很小的单元内随机选材质 A 或 B，就会把完整线条切成孤立的短杠、十字和碎片。此前观察到的杂乱边缘不是单纯“贴图分辨率不够”，也不是增加模糊就能根治的问题。

希望保留的是：大尺度边缘不完全规整，过渡带内透明度自然衔接，但每一侧材质仍是一张连续图案。当前算法因此把随机性集中在 **地形分界的位置**，不再随机切断材质自身。

实现分两层：

1. `BuildPairTexture()` 从作者 ID 网格寻找边界，以八邻域距离传播为每格选择最近的另一种地形及其距离。临时 RGBA 图的 R / G 是主、副 ID，B 是归一化边界距离，A 表示是否存在副材质；这四个通道不是四种地形。
2. [BakeSingleTerrainSurface.shader](AnimalGame/Assets/Shaders/BakeSingleTerrainSurface.shader) 先完整采样两侧图案，再按边界距离计算混合。大尺度 Value Noise 和较弱的细节噪声只偏移边界。材质 ID 对的规范排序符号保证同一条边界的两侧使用一致的噪声方向，固定 `Surface Noise Seed` 保证重复烘焙可重现。

| 模式 | 当前效果 |
| --- | --- |
| `Hard` | 保留作者网格定义的硬分界 |
| `Alpha` | 使用连续混合带；当前代码仍共用带噪声偏移的距离，若要纯平直渐变还需把噪声幅度调成 0 |
| `Noisy` | 一条连续、不规则的硬边；不会在过渡带里散布随机碎片 |
| `Hybrid` | 不规则边界加连续 Alpha 混合，是当前条目的默认模式 |

两种地形接壤时，任意一侧选择 Hard，交界就保持 Hard；任意一侧是 Hybrid 则使用 Hybrid；Alpha 与 Noisy 接壤也合并成 Hybrid。与 ID 0 接壤时采用非空材质的模式。

Hybrid 的有效混合宽度为 `lerp(AlphaCoreWidth, TransitionWidth, AlphaBlendShare)`，再考虑条目宽度倍率。`Surface Boundary Noise Scale Meters` 控制宽阔起伏，`Surface Boundary Noise Amplitude Meters` 控制最大偏移尺度；编辑器显示为 `Boundary Detail Scale (metres)` / `Boundary Detail Strength` 的字段仍名为 `surfaceScatterCellSizeMeters` / `surfaceScatterStrength`，历史命名不意味着现在仍在做纹理碎片散布。

混合颜色前先做预乘 Alpha：混合的是 `(rgb × alpha, alpha)`，之后按结果 Alpha 还原 RGB。其目的在于透明图案相接时减少透明像素隐藏 RGB 造成的暗边或颜色污染，尤其适合本项目白线叠黑底的素材。

**限制：** 临时邻接图每点最多保存一个副材质，不是任意多材质同时加权的混合模型；三种及以上材质汇合时仍需实际观察交点效果。提高 ID 网格分辨率改善的是作者边界空间精度，提高成品 PNG 分辨率改善的是最后显示精度，不能互相替代。

### 11.5 Tile Size、图集与“看起来分辨率很低”的区别

**设计目的：纹理图案的视觉单位应能独立调整。** 例如希望一块岩石纹或一组草纹占据更大的游戏空间，不应通过放大整个地图 / 缩小玩家实现，否则会连带改变空间感和已有配置。`Tile Size Meters` 越大，一次纹理重复覆盖的地图范围越大，重复次数越少；它不改变地形高度、可玩面积或碰撞。

烘焙时采用 `frac(mapMeters / tileSizeMeters)` 生成重复 UV。每个材质被放入 2048² 临时图集的一个 128² 单元；四周各预留两像素重复边，实际内容区为 124²，以避免双线性采样混到相邻材质。当前这套图集尺寸与使用 128² 测试素材相匹配，但单纯换成 1024² 源图不会自动让每种材质保留 1024² 细节。

`CopyRepeatedPatternIntoAtlas()` 对素材做双线性重采样；最终地图 PNG 又有自身像素密度，因此可见细节至少同时受以下因素限制：源图质量、124² 图集内容区、Tile Size、地图米数 / 成品分辨率、相机放大倍率。把低像素密度的烘焙地图继续放大，可能再次显露线条台阶，即使源图本身已经更新。

**历史数值回弹问题的保护点：** `HeightMapSurfacePainterWindow` 保留同一个 `SerializedObject`，用 `UpdateIfRequiredOrScript()` 读取并 `ApplyModifiedProperties()` 提交。代码注释明确记录了曾经每次 IMGUI Repaint 都重建该对象，导致正在输入的 `Tile Size Meters` 被旧值覆盖的问题。重构这个窗口时，需要测试“直接键盘输入数值”，不能只测试拖动数字。

调整后建议核对三件事：新数值失焦后仍保留；手动烘焙后同一区域的重复图案确实变大 / 变小；地图尺寸、机器人碰撞和高度数值不变。不要通过反复增加纹理模糊来补偿烘焙分辨率或错误的采样尺度。

### 11.6 闭合等高线区域 Alpha：边缘清晰、内部只保留浅纹理

**设计背景与演变：** 在闭合等高线圈出的地形块中，边缘的 Texture 较明显，能帮助玩家感知形状和地形类型；中心的 Texture 应明显减弱，让机器人、重心、扫描信息和格线不被一大片密纹理淹没。最初讨论过依据玩家当前区域动态调整，但最终明确要求地图保持静态：不能玩家走近某处，它的纹理浓淡才改变。

后续调整又强调了两个细节：大区域不该要走非常远才进入“浅色中心”；中心也不应简单消失成完全没有纹理的黑块。预期是边缘形成可读的带状纹理，内部保留很轻的材质感。这个目标需要同时处理 **区域距离、最小 Alpha 和源图本身的透明度**，不是把一个叫“中心 Alpha”的参数填成 5 就必然得到最终 5% 的画面。

当前静态实现是同一 Editor 文件中的 `StaticClosedContourAlphaBuilder`，并非运行时扫描使用的 `ContourRegionIndex`。它在每次地表烘焙中计算一次系数图，最后乘入地表 PNG；PNG 不再保留与玩家位置相关的计算。

**区域建立流程：**

1. 按专用分辨率建立 Surface Height，仍使用地图的 `Surface Smoothing Sigma Meters`。这使纹理渐变基于平滑后的宏观地势，减少细小灰度噪点被当成无数小区域。
2. 从最低高度按 `Contour Interval Meters` 枚举实际阈值；每个阈值同时处理 `height >= threshold` 的高地和 `height <= threshold` 的低地，山包与洼地都可形成区域。
3. 四邻域洪泛得到连通分量。地图四条直边可封闭延伸至边界的等高线，但一个区域至少要接触真实的高度阈值变化；纯平地图不能仅靠矩形边界被认定为闭合等高线。
4. 小于 `Surface Closed Contour Minimum Area Square Meters` 的分量忽略，避免少量图像噪点成为独立渐变区。
5. 以有效区域边界为距离源，使用优先队列在八邻域传播地图米制距离。嵌套区域重叠时，每个像素选择包含自己的最小面积区域，保留内层结构。

这里所谓“中心”，不是几何质心，也不是多边形包围盒中心，而是 **离所属区域边界较远的位置**。狭长、弯曲区域可以有一条内部浅色带；中心不必是唯一的点。区域距离是栅格内传播的近似距离，不是精确向量轮廓距离。

**Alpha 曲线：**

```text
D = min(配置 FadeDistance, 该区域最大内部距离)
H = min(配置 EdgeHoldDistance, D × 0.25)
t = clamp01((当前内部距离 - H) / max(ε, D - H))
s = SmoothStep(0, 1, t)
f = 1 - (1 - s)^FadeStrength
区域 Alpha 系数 = lerp(EdgeMultiplier, CenterMultiplier, f)
```

大区域在固定的地图米数后就到达中心系数，不要求先接近整个区域的几何中央；小区域把距离上限压缩到自身宽度，最深处仍能变浅。`FadeStrength` 越大，离开边缘后越快降低，正是为了改善“内部太明显、必须走很远才变浅”的体验。序列化字段 `surfaceClosedContourDistanceCurve` 对应代码属性 `SurfaceClosedContourFadeStrength`，不是 `AnimationCurve` 对象。

**为什么还要归一化素材 Alpha：** 某些源纹理已经整体很透明，再乘 Palette Opacity 和中心系数后，内部几乎不可能看见。`CalculatePatternAlphaReference()` 统计源图非零 Alpha 的中位数；烘焙时把非零像素的 Alpha 向 `clamp01(sourceAlpha / medianAlpha)` 插值，插值强度由 `Pattern Alpha Normalization` 控制。完全透明像素仍透明，不会凭空把素材空白填成灰色块。

最终中心强度近似为：`归一化后的源图 Alpha × Tint Alpha × Palette Opacity × CenterMultiplier`，还受地形交界混合、UI 裁切及其他表现影响。它是乘法链，`CenterMultiplier = 0.143` 与 `Opacity = 0.35` 相乘约为 5%，但只在源 Alpha 与 Tint Alpha 接近 1、没有其他衰减时成立。

**当前保存值与历史目标不应混淆。** 两个地图资产现在保存如下参数；这是本次代码 / 资产核对结果，不代表这些数值已经通过视觉验收：

| 参数 | 当前两图保存值 | 意义 |
| --- | --- | --- |
| `Surface Pattern Alpha Normalization Strength` | 0.75 | 减少素材原始透明度差异 |
| `Surface Closed Contour Edge Alpha Multiplier` | 0.235 | 边缘仍需乘 Palette Opacity |
| `Surface Closed Contour Edge Hold Distance Meters` | 1.5 | 边缘保留带，内部会受区域宽度限幅 |
| `Surface Closed Contour Fade Distance Meters` | 10 | 到达浅色内部平台的最大距离 |
| `Surface Closed Contour Center Alpha Multiplier` | 0.02 | 当前并非历史讨论的 0.143 |
| `Surface Closed Contour Distance Curve` | 2 | 当前 FadeStrength |
| `Surface Closed Contour Minimum Area Square Meters` | 4 | 过滤小分量 |
| `Surface Outside Closed Contour Alpha Multiplier` | 0 | 无有效区域的位置不显示地表纹理 |

当前 Palette Opacity 为 0.35 时，源 Alpha 接近 1 的中心上限约为 `0.35 × 0.02 = 0.007`，即 0.7%；如果设计仍希望最浅处接近 5%，当前保存值与该目标有差异，应通过明确调参任务处理，而不是在文档中把它写成已经实现 5%。

**限制与修改风险：**

- 该 Builder 用地图矩形与高度阈值建立区域，没有把 `PlayableAreaMask` 传入自己的 `BakedHeightField.Bake()`；不应声称它已按任意形状的可玩轮廓重新建立完全一致的 Alpha 区域。
- 计算包含按高度层反复洪泛与距离传播，耗时发生在 Editor。提高闭环专用分辨率、增加高度层数会增加烘焙时间；不要把这段计算搬进每帧 Update。
- 修改高度图、范围、等高距或平滑参数后，需要重新烘焙地表，否则运行时等高线可能已经改变，而地表渐变还对应旧地形。
- 透明的线纹中部是否“可见”取决于最终像素对比和显示大小，不能只读一个 Alpha 数字验收。

建议固定同一张地图、同一相机倍率，观察一个大区域、一个狭长区域、一个凹地、一个由地图边界封闭的区域。沿边缘走向内部应逐渐变淡，停在同一地图位置时不应因玩家移动或扫描而重写浓淡；重新加载后相同位置应保持一致。

### 11.7 等高线渲染与相对海拔滤镜

**设计目的：让玩家不必只靠密集曲线猜测哪边高、哪边低。** 单纯闭合线形状可以同时表示山包和洼地，初看不一定能判断。相对海拔滤镜要回答的是“比机器人脚下更高还是更低”，不是把草地和岩石换成新材质，也不应改变地形危险程度。它是可开关的阅读辅助层。

当前主地图仍是 Sprite。`CreateHeightVisualization()` 一次生成基础颜色预览；`DynamicHeightContours.shader` 采样与物理 Surface 对应的归一化高度纹理，将高度变成等高线坐标 `(height - minHeight) / contourInterval`，按到最近整数层的距离画线，而不是在场景中生成大量 LineRenderer。

线宽按 `fwidth` 做屏幕空间适配；`Maximum Contour Coverage` 限制局部线条宽度过大造成的整片变白。控制器按当前相机视口网格采样可见高度范围，决定最低到最高可见等高线的宽度和不透明度变化。它改变的是同一组等高线的表现，不会随镜头移动改变等高距或实际海拔。

采样由相机渲染回调触发并做帧 / 相机去重；每次约为 `ViewportHeightSamples × ceil(ViewportHeightSamples × camera.aspect)` 个地图采样。视口网格只是一种范围估计，不保证捕获每个极小山尖；这与地形通行判定的采样路径是两件事。

**滤镜入口：** `MapTestSceneController.UpdateElevationFilterRuntime()` 响应 `AdaptiveLegacyGamepadInput.WasDpadRightPressedThisFrame()`；默认键盘为 `H`。配置位于控制器的 `Relative Elevation Filter`，不是地表 Palette 条目。

| 参数组 | 设计作用 |
| --- | --- |
| `Lower Elevation Filter Color` / `Higher Elevation Filter Color` | 以冷 / 暖色区分相对当前玩家的低 / 高地 |
| `Elevation Filter Relative Range Meters` | 多少米的相对差值达到充分着色 |
| `Elevation Filter Neutral Band Meters` | 脚下附近高度差不明显着色，避免平地也满屏噪声 |
| `Elevation Filter Maximum Opacity` / `Response Exponent` | 控制信息层强度，不淹没原有黑白纹理和目标 |
| `Player Height Reference...` | 高亮与当前玩家同高的参考线 |
| `Elevation Filter Hillshade Strength` | 用 Surface 邻点梯度补充方向阴影，帮助理解起伏 |
| `Elevation Filter Transition Duration` | 开关时平滑过渡，避免突兀换屏 |

每帧取玩家当前位置的 Surface Height；Shader 计算 `relativeHeight = terrainHeight - playerHeight`。超过中性带后，按相对范围归一化、指数整形，再乘最大不透明度。附加的坡向阴影来自周边四次高度纹理采样与一个固定虚拟光方向，不是场景真实光照或实际地形网格。

滤镜在 Main UI 范围内显示；进入时半径与强度有过渡。同高参考线使用玩家的精确采样海拔，不必正好是标准等高距的整数倍，并抑制完全平坦区域出现整片参考线。控制器不能取得有效玩家高度时关闭可见滤镜层。

**与静态纹理约定不冲突：** 地表成品图不变；滤镜是明确由玩家开关的动态叠加层。玩家上坡后“相对自己更高 / 更低”的分类会改变，这是滤镜的定义，不是地表重新生成。排查颜色变化时应先区分这两个系统。

### 11.8 运行时闭合区域索引的不同职责

源码：[ContourRegionIndex.cs](AnimalGame/Assets/Scripts/MapTest/ContourRegionIndex.cs)、[TraversalScanOverlayUI.cs](AnimalGame/Assets/Scripts/MapTest/TraversalScanOverlayUI.cs)。

**设计目的：一次地形扫描不应只是一块脱离地势结构的屏幕点阵。** 扫描时需要能识别机器人所在的闭合等高线区域，给采样 / 显示逻辑提供区域身份。因为玩家会移动并重新扫描，区域查询发生在运行时；但这不需要把上节静态纹理的 Alpha 计算也变成实时。

`TraversalScanOverlayUI.Initialize()` 创建索引；`BeginScannedSnapshot()` 在取到机器人地图位置后调用 `TryGetCurrentClosedRegion()` 并保存区域 Handle。索引按玩家所处高度的下方等高层查询高地分量，同时按上方等高层查询低地分量，选择两者中面积较小的有效区域。

`GetOrBuildLevel()` 第一次遇到某个高度层时进行高地 / 低地四邻域洪泛，缓存整张标签数组和各分量面积。`Contains()` 后续按栅格标签检查区域包含关系。地图四边仍能封闭触边等高线；纯矩形而没有阈值变化仍不算一个有效区域。

此索引不是直接读取 `StaticClosedContourAlphaBuilder` 结果：前者运行时按需查询、使用高度场分辨率、服务扫描；后者 Editor 遍历多个阈值、建立距离梯度、服务地表 PNG。二者不能简单共享一个“当前区域变量”，否则容易把固定地表变成随玩家变化。

**成本与限制：** 每个新高度层会分配高 / 低地标签数组，当前实例中按层缓存，首次查询可能产生 CPU 与内存峰值。2048² 的单个 `int[]` 就约 16 MiB，两侧约 32 MiB / 层，另有共享队列、字典等；不是每次 Contains 都洪泛，但也不是无成本区域句柄。索引绑定建立时的 HeightField，地图重建时应同步重建依赖索引。

### 11.9 静态水：固定范围和水深，可动的波纹表现

**实现体现的设计区分：** 水体不能只是和草纹一样的装饰 ID。它既需要固定地理范围，也需要明确的深度供通行系统判断；浅水与深水应能提供不同视觉提示，而波纹运动不应每帧改变水深或可通行性。这部分的算法取舍来自现有实现，不把未在历史对话中明确出现的水体细节冒充设计者原话。

打开 `Animal Game > Level > Static Water Painter`，或地图资产 Inspector 的 `Open Static Water Painter`。在 `Depth Brush` 中选择 `Set / Add / Subtract / Smooth / Erase`；左键拖动画，Shift 擦除，Alt 导航。`Target Depth (m)` 用于 Set，`Depth Change Per Dab (m)` 用于增减，`Strength` 与 `Hardness` 控制笔刷强度和边缘。整图 Fill / Clear 有确认框，避免误清水域。

作者数据每格一个字节：0 是干地，1..255 编码非零水深。运行时 `SampleStaticWaterDepth()` 从地图米制坐标双线性采样后，按 `encodedDepth / 255 × MaximumStaticWaterDepthMeters` 解码；例如最大深度为 10 m 时，单字节量化步长约 0.039 m。

当前测试地图水深图为 512²、落基山脉为 1024²，两图最大深度 10 m、最大可通行深度 1.2 m。深水判定由通行系统读取水深数组，不依赖画面是否看得清，也不依赖水纹素材的 Alpha。

**注意最大深度不是只改显示标尺。** 已存数据是归一化字节；改变 `Maximum Depth (m)` 会重新解释现有字节对应的米数。例如同一字节 128，在最大 10 m 和 20 m 时代表约 5.02 m 和 10.04 m。若只想调波纹视觉，应调视觉参数，不要改物理最大深度。

`HeightMapStaticWaterMaskBaker` 把作者图输出为 PNG：R 为二值水域范围，G 为归一化水深，B=0、A=255。PNG 使用线性、无压缩、无 mipmap、Clamp、Bilinear 导入。范围和深度放不同通道，是为了避免用深度接近 0 的像素直接等同于模糊水岸；地图 Shader 另对范围做柔化。

`StaticWaterEditorPreview.shader` 用于 Scene 编辑预览；正式运行使用 `DynamicHeightContours.shader` 中的水分支。正式表现把两层不同方向 / 尺度的图案叠加，并通过正弦 UV 扭曲避免单纯平移的纸片感；深度越大，图案更不透明，速度按 `Deep Speed Multiplier` 减慢。

**当前特别约定：水域在 UI 圈外也可见。** 圈外使用时间为 0 的固定波纹，圈内混合到随时间运动的波纹；这个圆形窗口选择的是“静止还是动画”，不是“显示还是隐藏”。而普通地表纹理在圈外不显示。水域完整范围还会压掉底下的普通地表纹理，避免水纹透明孔隙中又露出草地 / 格线材质；地图等高线仍在后续渲染层绘制。

这不是动态液体模拟：没有水流传播、侵蚀或因波纹改变碰撞；在 Play 模式移动到不同位置，只改变可见动画表现。建议验收同一水域圈内运动、圈外静止且仍存在；越过 1.2 m 的通行结果稳定；修改 Preview Tint 不应误认为已经调整正式水色，因为该 Tint 是 Scene 预览参数。

### 11.10 从彩色等高图重建高度：避免生成式灰度图失真

源码：[convert_colored_height_map.py](AnimalGame/Tools/HeightMaps/convert_colored_height_map.py)、[GeneratedHeightMapTextureImporter.cs](AnimalGame/Assets/Editor/GeneratedHeightMapTextureImporter.cs)。输入素材：[Rocky_Moutain_Height_Colored_Map.png](AnimalGame/Assets/Maps/Rocky_Moutain_Height_Colored_Map.png)、[Rocky_Moutain_Height_Colore_Palate.jpg](AnimalGame/Assets/Maps/Rocky_Moutain_Height_Colore_Palate.jpg)。

**设计背景：视觉上像山脉，不等于高度数值正确。** 之前灰度图由彩色等高图经生成式图像处理得到，出现了地形轮廓失真、细碎分层与不预期的台阶。生成过程可能重新解释山脊、补出沟壑、改变边界，虽然图片更像地形，却不能保证每个原始颜色仍代表指定海拔。这类误差会进一步表现为无法通行的 Step Block，不应全部通过放宽机器人通行阈值掩盖。

设计者提供了确定的颜色顺序：从色表右下往左上逐级升高，每级相差 5 m。既然原图已有明确的离散高度约束，合理的数据生产路径是 **按色表确定性反算高度，再在相邻等高带内补充连续坡面**，而不是让图像模型自由重画山体。普通“彩色转灰度”也不正确，因为 RGB 亮度顺序不等于提供的海拔顺序。

当前脚本依赖 Python、NumPy、Pillow，尚不是通用 Unity 菜单工具。流程如下：

1. 按给定 3×10 色卡布局读取色块中心区域中位色，避开 JPEG 色块接缝；丢弃低色度空格，再逆转行优先顺序得到低到高色表。
2. 把非正方形地图居中补边成正方形再缩放，避免直接拉成正方形导致地理比例变形；使用最近邻保留离散色块，不先混出大量中间颜色。
3. 从 `(0,0)` 对相同背景色做连通填充，再保留地图中心附近的单一主要陆块。原图棕色背景也可能是合法高度色，必须用连通性区分，不能删除所有棕色像素。
4. 对原图唯一 RGB 色值匹配最近 Palette 色，颜色距离不超过 12 的作为可靠匹配；局部非色表像素从可用邻居修复。
5. 提取各高度阈值边界，计算八邻域近似欧氏距离。在第 k 个色带内使用 `t = dLower / (dLower + dUpper)`，令 `height = (k + t) × heightStep`，在带内生成连续坡面而不主动移动作者边界。
6. 最高色带没有更高等高线约束，因此保持已知最高高度，不凭空制造一个更高山峰；最后输出 16 位高度、8 位查看预览及独立黑白可玩 Mask。

这正是高度数据与边界 Mask 分离的目的：深色低谷仍是合法地形，地图外背景却不是“海拔相同的一块可走平地”。16 位保存提高数值精度，但不能还原原始分层图从来没提供的真实细节；带内插值仍是一种基于现有约束的近似地形，不是测绘 DEM。

以下命令以 Unity 工程目录为工作目录，输出名称与现有导入器约定一致；执行会覆盖同名生成图，应在计划替换时运行，而不是阅读 README 时必须执行：

```powershell
python Tools/HeightMaps/convert_colored_height_map.py --colour-map Assets/Maps/Rocky_Moutain_Height_Colored_Map.png --palette Assets/Maps/Rocky_Moutain_Height_Colore_Palate.jpg --output-height Assets/Maps/Generated/Rocky_Moutain_Height_R16.png --output-preview Assets/Maps/Generated/Rocky_Moutain_Height_Preview.png --output-mask Assets/Maps/Generated/Rocky_Moutain_PlayableMask.png --resolution 2048 --height-step-metres 5
```

导入器仅针对 `Rocky_Moutain_Height_R16.png` 和 `Rocky_Moutain_PlayableMask.png` 这两个确切文件名生效：高度强制 R16、Bilinear；Mask 强制 R8、Point；二者线性、可读、无压缩、无 mipmap、Clamp，并设置最大导入尺寸 2048。更换文件名、输出更大分辨率或新增其他地区工具时，需要同步处理导入规则，不能默认任意灰度 PNG 都获得 16 位精度。

**生成完成还没有接入地图。** 必须在目标 `HeightMapLevelAsset` 中把 Height Map 指向 R16、Playable Area Mask 指向生成 Mask，并按脚本输出报告的范围设置 Minimum / Maximum Height。若 17 种颜色全部用于地形、每级 5 m，则范围是 0–80 m；把同一数据再映射成 0–100 m 会把高度比例放大，颜色间隔就不再代表原来的 5 m。等高线显示间隔是否也改成 5 m 则是另一个表现选择。

**当前资产的真实状态：** `RockyMountainHeightMapLevel.asset` 仍引用 `Rocky_Moutain_New_GreyMap.png`，范围 0–100 m；`Playable Area Mask` 为空，`Use Height Map Border Mask` 为 false。`Generated` 中虽然已经有 R16 / Preview / Mask 文件，但它们不是当前落基山脉资产实际绑定的数据。文档保留这一差异，不在说明期间擅自切图或改关卡数值。

**工具限制与验收：** 色卡布局坐标、色度阈值、RGB 距离阈值、单陆块假设都是针对当前素材的；换色卡格式、压缩严重的彩图、多岛屿或背景与地形发生同色连通时，不能直接保证正确结果。对照检查应包含外轮廓、至少几条原始色阶边界、低谷、最高色带与同一坐标高度；确认之后再检查玩家周围 Step / Slope 判定，而不是只看缩略图“像不像山”。

### 11.11 编辑后验收与常见误解

| 观察到的问题 | 先查什么 | 为什么 |
| --- | --- | --- |
| 新 PNG 已替换，地表还是旧样子 | 目标地图 Palette 引用、手动 Bake、成品 PNG 引用 | 地表读取的是成品，不在运行时重采样源纹理 |
| Tile Size 输入后回弹 | 是否正在改正确 Palette、SerializedObject 生命周期 | 不是合法数值只能为 8 |
| 交界有短杠 / 碎十字 | Bake Shader 是否旧版本、噪声是否在随机切材质 | 可能是过渡算法而非源图像素低 |
| 中心几乎看不到纹理 | 源 Alpha、归一化、Opacity、CenterMultiplier 乘法链 | “中心系数”不是最终不透明度 |
| 圈外还有水但没普通地表 | 当前水体与普通地表可见性约定 | 水在圈外固定显示，不是漏裁切 |
| 画了 Water 地形却不阻挡 | 是否写了静态水深图 | 地形 ID 名称不产生物理水深 |
| 调平滑后纹理渐变不贴等高线 | 是否重新 Bake Surface | 等高线来自当前高度，渐变来自最后一次 PNG |
| 生成 R16 后游戏没变化 | 固定地图资产实际 Height Map 引用 | 磁盘存在新文件不代表当前场景使用它 |
| 地图边缘缺失或低谷不可走 | Mask 引用、黑边阈值、Inset、源图轮廓 | 可玩轮廓与灰度海拔不能简单合并 |

本章描述的是当前已实现的地图制作管线。验收的核心是把“数据没更新”“表现没重烘焙”“源图缺少精度”“可玩边界错误”和“实际通行规则”分开：只有这样，调节画面时才不会无意改变驾驶难度，修复像素台阶时也不会把本应存在的悬崖一起消掉。

## 12. 动物、感知、生态与植被

本章以当前两个物种——麝鼠与北美黑啄木鸟——为例，解释动物怎样利用地图中的植物、水域和树木，怎样发现玩家，以及受惊后怎样躲藏、返回。代码中的 `Curious` 对应设计讨论里的“惊扰/警觉观察”，不能仅按英文理解成主动接近玩家的“好奇”。

### 12.1 设计背景：动物应当生活在地图中，而不是只充当扫描目标

原始设计要求动物先拥有可以观察的日常活动，再根据玩家的接近作出反应。对于啄木鸟，玩家应能看到“停树—换树—面向树干啄木”的连续生活行为；对于通用观察逻辑，玩家接近得越近、移动得越快，被发现的风险越大。动物正面能够看到玩家时，还应额外提高发现概率。这样玩家的观察、扫描和拍照不是对着一个固定靶标按键，而是在自己的移动方式与动物的警觉之间取舍。

“发现玩家”与“立即逃跑”被明确分开：发现后先进入该物种自己的惊扰表现，再定时决定逃离或激怒。这给玩家一个可观察的中间反馈，也让不同物种可以用不同概率表达胆小、警觉或者攻击倾向。啄木鸟明确没有攻击行为，主要通过较高逃离概率表现警惕。

后续设计又将“受惊后永久消失”改为“躲避一段时间后重新出现”。目的不只是重新生成一个外观相同的动物，而是避免玩家一次失误便清空地图里的观察对象，允许退出干扰范围、等待、再次观察。当前实现保留原动物实例，恢复日常活动；扫描后的认知状态因此也不会因为这一轮躲藏被自然清空。

工程上采用“共享状态机 + 物种行为组件 + 共享数据配置”，是为了让发现玩家的规则一致，同时让逃离目的地、栖息行为等保持物种差别。这是当前代码结构的工程解释，并不意味着所有物种已经拥有完整生态模拟。

### 12.2 文件职责与运行时所有权

| 文件 | 职责 | 不负责的内容 |
| --- | --- | --- |
| [AnimalAgent.cs](AnimalGame/Assets/Scripts/Animals/AnimalAgent.cs) | 实例生命周期、通用状态切换、惊扰反应概率、隐藏/返回入口 | 不直接决定吃哪棵植物、飞往哪棵树 |
| [AnimalSpeciesConfig.cs](AnimalGame/Assets/Scripts/Animals/AnimalSpeciesConfig.cs) | 物种共享的半径、速度、概率、日常权重、等待时间 | 不保存某一只动物的当前状态 |
| [AnimalTypes.cs](AnimalGame/Assets/Scripts/Animals/AnimalTypes.cs) | 状态、行为类型、食物类型与可序列化权重/时长结构 | 枚举本身不是行为实现 |
| [AnimalBehaviourSet.cs](AnimalGame/Assets/Scripts/Animals/AnimalBehaviourSet.cs) | 各状态 Enter/Tick/Exit 接口，通用躲藏计时与安全判定 | 没有通用寻路器或通用攻击实现 |
| [AnimalPerception.cs](AnimalGame/Assets/Scripts/Animals/AnimalPerception.cs) | 玩家距离、速度、视锥及遮挡加成，定时发现检测 | 不改变动物外观，不进行扫描认知 |
| [AnimalMotor.cs](AnimalGame/Assets/Scripts/Animals/AnimalMotor.cs) | 地图米制移动、转向、局部避障、直线飞行、瞬移 | 不使用玩家的履带坡度/Step Block 通行模型 |
| [MuskratBehaviour.cs](AnimalGame/Assets/Scripts/Animals/MuskratBehaviour.cs) | 进食、游走、潜水、受惊入水与返回 | 不修改共享物种配置 |
| [PileatedWoodpeckerBehaviour.cs](AnimalGame/Assets/Scripts/Animals/PileatedWoodpeckerBehaviour.cs) | 出生树绑定、选树、飞行、啄木、回树躲藏 | 当前不实现亚健康树类别 |
| [AnimalFoodSource.cs](AnimalGame/Assets/Scripts/Animals/AnimalFoodSource.cs) | 植物作为食物目标的类型、权重、进食间距 | 不是食物库存；没有被吃掉的资源扣减 |
| [AnimalPlaceholderView.cs](AnimalGame/Assets/Scripts/Animals/AnimalPlaceholderView.cs) | 占位动物本体、方向、缩小与隐没表现 | 不驱动行为状态机 |
| [AnimalDiscoveryVisual.cs](AnimalGame/Assets/Scripts/Animals/AnimalDiscoveryVisual.cs) | 未识别雪花场、识别后的显现、外部隐藏合成 | 不决定玩家有没有扫描成功 |
| [AnimalSoundEmitter.cs](AnimalGame/Assets/Scripts/Animals/AnimalSoundEmitter.cs)、[AnimalSoundTypes.cs](AnimalGame/Assets/Scripts/Animals/AnimalSoundTypes.cs)、[AnimalSoundWaveManager.cs](AnimalGame/Assets/Scripts/Animals/AnimalSoundWaveManager.cs) | 声音事件对应的可视声波配置、触发和复用 | 当前不是 AudioSource 音频播放系统 |

配置资产分别是 [MuskratConfig.asset](AnimalGame/Assets/Data/Animals/MuskratConfig.asset) 与 [PileatedWoodpeckerConfig.asset](AnimalGame/Assets/Data/Animals/PileatedWoodpeckerConfig.asset)。Prefab 分别是 [Muskrat_Placeholder.prefab](AnimalGame/Assets/Prefabs/Animals/Muskrat/Muskrat_Placeholder.prefab) 与 [PileatedWoodpecker_Placeholder.prefab](AnimalGame/Assets/Prefabs/Animals/PileatedWoodpecker/PileatedWoodpecker_Placeholder.prefab)。

需要区分三类数据：

1. 物种共性：在 `AnimalSpeciesConfig` 资产中，例如所有引用它的麝鼠共享警觉半径和逃跑速度。
2. 实例布置：在场景中的 `HeightMapPlacedObject`、啄木鸟 `birthTree` 等字段中，例如这一只鸟属于哪棵树。
3. 运行状态：`AnimalAgent.CurrentState`、计时器、`AnimalMotor.TargetMapPosition`、当前树/食物等，只存在于运行实例。

`AnimalAgent.Start()` 尝试初始化；地图尚未准备好时，`Update()` 会重试。初始化从 `HeightMapPlacedObject` 获取地图，必要时查找场景中的 `MapTestSceneController`，将动物当前 Transform 转换成 `HomeMapPosition`。随后依次初始化 Motor、Perception、物种行为与声波组件，再进入 Daily。缺少 config、motor、perception 或 behaviour 会报错并关闭 Agent，不能靠继续等待修复这些缺失。

实例在 `OnEnable/OnDisable/OnDestroy` 维护 `ActiveAgents` 集合，供照片判定、植物接触淡出等查询。隐藏时不禁用 GameObject，因此不能只检查集合中是否存在；要使用 `IsPresent`，它要求初始化完成且不是 Hiding/Despawned。

### 12.3 通用状态机：受惊与永久移除不是同一件事

```text
Daily ──发现检测成功──> Curious（惊扰/看向玩家）
  ^                       ├─反应抽样──> Fleeing ──物种逃离结束──> Hiding
  │                       ├─支持攻击且抽样成功──> Aggressive       │
  └─玩家离开警觉圈足够久───┘                                  │
  └──────────安全出现完成 + 检测宽限期──────────────────────────┘

Despawned：保留的显式永久移除入口；当前两种受惊返回流程不调用它。
```

`AnimalAgent.Update()` 使用 `Time.deltaTime`，先递减返回后的检测宽限时间，再在 Daily 且未压制感知时调用 `Perception.TickDetection()`。之后执行当前状态的行为 Tick，最后执行 `Motor.Tick()` 与声波 Tick。因此行为在这一帧设置的移动目标，会在同一帧被 Motor 消费；状态机不是 Rigidbody/FixedUpdate 驱动。

惊扰状态每帧先停止位移，但仍允许物种行为调用 `FaceMapPosition()` 转向玩家。若玩家不在警觉范围，累计 `outsideAlertTimer`，达到 `CuriousLostPlayerDelaySeconds` 后返回 Daily；回到警觉范围会清除此计时。若玩家仍在范围中，按 `ReactionIntervalSeconds` 做一次随机抽样。

`EnterFleeing()` 和 `EnterAggressive()` 都会压制通用感知，避免逃离途中再次触发“刚发现玩家”。`BeginHiding()` 停止移动并进入物种隐藏阶段；`CompleteHiding()` 恢复 Daily、重置感知计时，并加入 `PostReappearGraceDurationSeconds` 的检测宽限。该宽限是避免“刚出现立刻又被吓走”的工程保护，不是玩家永久隐身。

`Despawn()` 才会执行 `gameObject.SetActive(false)`。不要把恢复流程改为先 Despawn 再等待：物体禁用之后，其 Update 不会继续推进隐藏计时。

### 12.4 发现概率：慢走、保持距离与利用朝向为什么有效

设计希望玩家的操作连续地影响风险，而不是“进圈必被发现/出圈绝对安全”之外再没有层次。因此警觉圈只是允许检测的范围，距离和速度是概率乘数，“看见”是额外加成而非唯一渠道。即使玩家站在动物背后或者树干后方，仍保留听觉意义上的基础发现概率。

当前公式为：

```text
proximity = 1 - clamp01(distanceMeters / AlertRadiusMeters)
speed01 = clamp01(abs(RobotMover.CurrentSpeed) / PlayerSpeedForMaximumBonus)

distanceMultiplier = lerp(1, NearestDetectionMultiplier, proximity)
speedMultiplier = lerp(1, MaximumPlayerSpeedDetectionMultiplier, speed01)
sightMultiplier = 有直接视线 ? DirectLineOfSightDetectionMultiplier : 1

pDetect = clamp01(BaseDetectionChancePerCheck
                 × distanceMultiplier × speedMultiplier × sightMultiplier)
本次发现 = Random.value < pDetect
```

注意速度项直接读取 `RobotMover.CurrentSpeed`，没有先经过 `WorldSpeedToMapSpeed()`。如果未来修改玩家速度的单位或者世界比例，应核对 `PlayerSpeedForMaximumBonus` 的标定，不应仅凭字段名称假设已经转换成地图米/秒。

直接视线的判定依次为：

1. 将动物与玩家位置转换到地图坐标。
2. 玩家方向与动物 `FacingMapDirection` 之间夹角不超过 `DirectVisionAngleDegrees / 2`；字段存的是完整视角，不是左右各多少度。
3. 遍历有效实体障碍物，检查动物—玩家线段是否穿过 `HeightMapObstacleFootprint` 的圆形核心。命中则没有视线加成。

这不是三维地形视线：目前没有山脊高度遮挡、树冠透明度遮挡或物理 Raycast。树冠本身不作为实体核心，因此“画面上被树叶覆盖”不等于“动物听不到/看不到”。

第一次进入警觉圈先重置倒计时，不会立即抽样；之后间隔到期才抽样。离开再进入会重新等待完整间隔。实现每帧最多抽一次，没有用 while 在卡顿帧补做多次抽样，因此极低帧率下实际频率可能降低。

`BaseDetectionChancePerCheck` 是每次检测概率，不是每秒概率。若条件不变，独立检测 n 次的累计发现概率为 `1 - (1 - pDetect)^n`。例如每 0.1 秒检测一次、单次概率 0.3，大约 10 次检测后累计发现概率约 97.2%；不能把 0.3 当作“每秒只有 30%”。调小检测间隔会显著强化警觉。

惊扰后的反应也使用距离乘数：

```text
pFlee = clamp01(BaseFleeChancePerCheck × lerp(1, NearestFleeMultiplier, proximity))
pAggressive = SupportsAggression
    ? clamp01(BaseAggressionChancePerCheck × lerp(1, NearestAggressionMultiplier, proximity))
    : 0
r = Random.value
if r < pAggressive: 激怒
else if r < pAggressive + pFlee: 逃离
else: 继续惊扰
```

同一次抽样先给激怒分配区间，再给逃离分配区间；两者不是独立抽签。若两项之和超过 1，激怒优先，逃离有效概率会受剩余区间限制。当前两个行为组件的 `SupportsAggression` 都返回 false，所以仅把资产中的激怒概率改大，并不能让它们攻击。

### 12.5 当前物种配置与调参效果

下表取自已保存的配置资产，不是 C# 字段默认值；场景若替换 config 引用，则以其实际引用为准。

| 配置 | 麝鼠 | 北美黑啄木鸟 | 设计上的作用 |
| --- | ---: | ---: | --- |
| `ActivityRadiusMeters` | 20 m | 45 m | 日常活动围绕出生区域，不把随机走动变成无边界迁移 |
| `DailyMoveSpeedMetersPerSecond` | 1.8 | 9 | 保留地面移动与鸟类快速换树的差别 |
| `FleeSpeedMetersPerSecond` | 8 | 14 | 受惊后的明确速度升级 |
| `AlertRadiusMeters` | 14 m | 18 m | 玩家从多远开始承担被发现风险 |
| `DetectionIntervalSeconds` | 0.1 s | 0.5 s | 检测节奏；应与每次概率一起调 |
| `BaseDetectionChancePerCheck` | 0.3 | 0.12 | 警觉圈边缘、无速度/视线加成时的单次基础概率 |
| `DirectVisionAngleDegrees` | 75° | 31° | 当前可见正面范围；并非所有方向等概率 |
| 最近距离/最高速度/直接视线发现乘数 | 3 / 2 / 1.5 | 3 / 2 / 1.5 | 接近、高速、正面可见三种风险可以叠加 |
| `ReactionIntervalSeconds` | 0.1 s | 0.2 s | 已经受惊之后重新决定反应的节奏 |
| `BaseFleeChancePerCheck` | 0.05 | 0.15 | 啄木鸟基础逃离倾向更强，但最终还受间隔影响 |
| `NearestFleeMultiplier` | 2.5 | 3 | 越逼近动物，越难让它继续停留观察 |
| `CuriousLostPlayerDelaySeconds` | 2.5 s | 3 s | 玩家离开后不瞬间恢复日常 |
| `FrightenedHideDurationSeconds` | 6–10 s | 8–14 s | 打断一次观察需要付出等待成本，但不会永久失去对象 |
| `HideSafetyCheckIntervalSeconds` | 0.75 s | 1 s | 最短隐藏时间过去后，定期检查是否可以返回 |
| `ReappearSafeDistanceMultiplier` | 1.1 | 1.05 | 返回点距玩家至少 15.4 m / 18.9 m |
| `PostReappearGraceDurationSeconds` | 1.5 s | 1.5 s | 完全出现之后短暂避免重复受惊 |

注意：返回等待时间只是开始尝试出现的最早时间，不保证“第 10 秒一定出现”。玩家持续守在返回点附近，动物就继续隐藏。

### 12.6 麝鼠：植物选择、水域行为与受惊返回

麝鼠用水域和植物把动物行为绑定到实际地图内容。工程上这避免了随机动画与环境脱节：没有可食用植物就不选择对应进食行为，没有合适深度水域就不开始潜水。当前不包含饥饿、消化、植物耗尽等资源模拟。

日常行为先过滤可执行项，再按 `SelectionWeight` 轮盘抽选；若选中项在真正启动时失败，将其移除后继续尝试，而不是让整个状态机卡住。无任何可执行项时进入 1 秒 FallbackIdle 后重试。当前权重分别为附近进食 1.5、游走观察 1.35、前往植物进食 1.5、潜水再出现 1；它们是选择权重，不是屏幕时间占比。

主要分阶段行为为：

- 附近进食：植物必须在出生点活动范围内，同时距当前位置不超过 `NearbyFoodDistanceMeters`，面向植物静止进食。
- 游走观察：在出生点活动圆内随机找可占据位置，到达后按随机间隔左右转头；移动过久会超时重选。
- 前往植物进食：全活动范围内选食物，绕实体核心求进食落点，到达后进食。
- 潜水：先走到合适水点，0.35 秒缩小淡出，在完全隐藏后移动至附近合适水点，等待潜水行为时长，再 0.35 秒浮出。水下移动目前是隐藏时的位置迁移，不是连续水下路径模拟。

食物通过 `AnimalFoodSource.ActiveSources` 枚举，候选权重为 `source.SelectionWeight × Config.GetFoodSelectionWeight(source.FoodType)`；采用加权蓄水池抽样，不需要先创建完整候选列表。Bush、Lotus、Australis 是当前三种食物类型。实体植物的进食距离为 `障碍半径 + 动物半径 + 物种额外间距 + 植物额外间距`，以 30° 步长尝试周围 12 个落点，避免把吃东西的位置放到树干中心。

水域直接读取地图静态水深数据 `TrySampleStaticWaterMapPosition()`，不从水域颜色或者植物贴图推断。目前麝鼠 `MinimumDiveWaterDepthMeters = 0.2`，`ResurfaceRadiusMeters = 10`，`WaterSearchSpacingMeters = 1.5`。所以画了水生植物而没有配置静态水域，并不能让麝鼠自动潜水。

受惊逃离具有物种专有路径：

1. 如果已经在满足潜水深度的水中，直接潜入。
2. 否则扫描地图寻找最近的有效水点并逃向它；找不到或者移动超时，就退回通用方向逃跑。
3. 退回方案以远离玩家的方向为中心，在 ±60° 内选一个方向，将远目标夹在地图矩形范围内。离开相机视口外扩 15% 的范围、逃跑累计 10 秒或到达目标，均会转入 Hiding，不再永久删除。
4. 水中潜入的个体返回时仍要求有效安全水点；陆地退回逃跑的个体在找不到水点时，允许在出生区寻找安全陆地点。

返回搜索先检查当前隐藏位置，再查其周围水点，再查出生区水点。开始出现之后还会逐帧检查玩家安全距离，若玩家又接近，立即隐藏并等待下一次机会。完全出现后才 `CompleteHiding()`，进入 1.5 秒检测宽限。

性能边界：找最近水点目前按 `spacing` 遍历整张地图，再在结果附近做局部细化。750 m × 750 m、1.5 m 间距时，粗搜索约有 25 万个位置候选，虽然不是每帧执行，很多麝鼠同时逃跑时仍值得分析峰值。扩大地图或增加数量前，应考虑水域候选缓存/空间索引，而不是无限降低扫描间距。

### 12.7 北美黑啄木鸟：以出生树为家的直线飞行行为

最初设计的具体目的，是让啄木鸟的行动依据树木健康状态产生偏好：在任意乔木停留，在枯死/亚健康树上啄木，换树时偏向更适合觅食的树；发现玩家后看向玩家并有较高概率逃离，不发生攻击；逃离不是随便飞出屏幕，而是回到绑定的出生树中躲藏。这样地图编辑时放置树木，实际会改变鸟类的活动路线和观察位置。

需要明确保留一项历史与现状差异：最初要求“枯死 50%、亚健康 35%、健康 15%”，但当前 `TreeHealthState` 只有 `Healthy` 和 `Dead` 两类。当前 Prefab 的 `healthyTreeWeight = 0.4`、`deadTreeWeight = 0.6`，啄木只允许 Dead；不能将三种健康状态和 50/35/15 写成已完成能力。将来恢复原始三类设计，需要同时扩展枚举、候选集合、权重、啄木可用性、Prefab 和编辑器配置。

`birthTree` 是场景实例引用。初始化未指定时，会查找最近有效树作为退回；日常活动范围优先以出生树中心为圆心，而不是永远使用动物最初摆放的位置。选树从 `TreeHabitat.ActiveTrees` 中收集同地图、有效、位于活动半径内的对象；通常排除当前树，若场景只有一棵可用树则允许复用，避免单树场景无事可做。

换树采用两级随机：先按健康/枯死类别权重选类别，再在该类别内部均匀选一棵。类别中树多，不会自动抢占另一类别的权重；没有该类别候选时，其权重归零。只有两类都非空且权重保持 0.4/0.6，类别概率才正好为 40%/60%。

日常主阶段：

- `Perched`：停止移动，持续面向树干，停留时长当前为 4–8 秒。
- `Flying`：调用 `AnimalMotor.SetAerialTarget()` 以 9 m/s 直线到目标树的停靠点。它忽略实体植物避障，但每一步仍要求在有效地图中。
- `Pecking`：只在枯死树上执行，逻辑位置不来回移动，而是将 Visual Root 沿自身前方向做正弦偏移；振幅 0.08 m，频率 5.5 Hz，持续 2.5–5 秒。
- `FallbackIdle`：无树可用时等待并重试，不凭空生成新树。

三种行为权重分别为停留 1.15、换树 1.15、啄木 1.35。`FlyToTree` 的配置时长不是实际飞行时长；飞行主要由实际距离/速度决定，`MaximumTravelTimeSeconds = 20` 是超时保护。

`TreeHabitat.TryGetPerchMapPosition()` 优先选择朝来向一侧的树干外沿：停靠距离为树 `perchRadiusMeters` 加动物体型与额外间距。若靠近不规则地图边界，则尝试 0、±30、±60、±90、±135、180° 的位置，最后才退回树干中心，减少合法树木却找不到可用停靠点的问题。

惊扰阶段停止飞行或啄木，连续转向玩家。日常飞行被惊扰打断时，缓存目标树和抵达后的动作；若玩家离开、恢复日常，会尝试续飞，而不是无条件丢弃原行动。真正转入 Fleeing 时则清掉此缓存，以返家优先。

逃离与返回流程：`ReturningHome → EnteringHome → Hiding.Hidden → Hiding.Emerging → Daily`。返家速度 14 m/s；到出生树后，用 0.45 秒 SmoothStep 将位置推进树心，同时缩小、淡出。隐藏最少 8–14 秒后检查停靠点安全性；安全时反向播放树心到停靠点的出现过程。出现期间玩家再靠近或树失效会取消出现。找不到任何可用返回树时会继续隐藏重试，不会在错误位置硬生成。

这仍是二维地图中的表现模拟：没有实际树洞几何、鸟类三维飞行高度或者真实骨骼啄木动画。

### 12.8 动物移动：地图米制局部避障，不是玩家机器人的驾驶系统

行为和运动分离的目的是让同一套吃、躲藏、回树逻辑适配地图比例变化，同时不把机器人的履带爬坡限制强加给鸟类或麝鼠。当前 Motor 每帧以 `min(剩余距离, 速度 × deltaTime)` 计算位移，并通过地图坐标转换写回 Transform；旋转用 `MoveTowardsAngle` 按物种转速推进。

地面移动会依次尝试目标方向、±30、±60、±90、±120°，选择第一条不穿越实体圆形核心的路径。检测半径是 `障碍 RadiusMeters + 动物 BodyRadiusMeters`，通过点到移动线段距离判断整个本帧扫掠是否阻塞，不只检测终点。若出生布置意外把动物放在障碍内部，允许距离障碍越来越远的移动，帮助其退出而不是永远困住。

飞行移动仅保留目标和每步位置的有效地图检查，不走地面避障角度列表。`TeleportToMapPosition()` 也只要求合法地图位置，不执行障碍扫掠；它用于完全隐藏后的潜水迁移、树心躲藏等受控流程，不适合直接替代一般行走。

当前没有全局路径规划、NavMesh、动物间碰撞、复杂围栏绕路或者玩家通行坡度限制。连续局部避障可能在复杂凹形障碍附近兜圈或超时；这属于能力边界，不应通过把运动速度调大掩盖。

### 12.9 认知缺失表现与躲藏表现如何合成

原始认知设计要求生物扫描到的动物永久退出认知缺失状态。因此“动物是否被识别”和“动物现在是否藏起来”必须是两份状态：已经扫描的麝鼠潜水再浮出，不应重新成为未知噪声；反过来，未扫描动物潜水时，未知噪声也不能继续留在水面泄漏准确位置。

[DiscoverableEntity.cs](AnimalGame/Assets/Scripts/Discovery/DiscoverableEntity.cs) 保存认知状态并发送 `DiscoveryChanged`，`AnimalDiscoveryVisual` 只订阅事件做表现。已知形象和未知雪花场按 `SmoothStep(revealProgress)` 交叉淡变，默认显现时长 0.4 秒。`AnimalPlaceholderView.SetSubmergeProgress()` 将 Visual Root 缩小到原来的 58%，并把 `1 - progress` 作为外部可见度交给 DiscoveryVisual；最终已知与未知画面都会乘这个可见度。

未识别外观还有独立雪花流速、形态变化、大小变化和延迟位置采样。其工程目的可从实现读出：不让玩家通过噪声场精确读取动物每帧位置和朝向。未知 renderer 在 LateUpdate 保持世界旋转为零；默认每约 0.3 秒刷新位置，其中刷新动画 0.2 秒，先淡出、在全透明时换位置、再淡入，而不是连续准确跟随动物。

这里的“永久识别”是当前实例运行期间的状态，不代表已经实现跨场景/跨存档持久化。隐藏流程保留同一实例，因而保留其识别状态；重新加载场景仍取决于 `startDiscovered` 等初始化配置。

`DiscoverableEntity.IsScannable` 会排除 `AnimalAgent.IsPresent == false` 的隐藏动物。照片系统通过 `AnimalPhotoSubject` 与 Agent 衔接，具体照片库与结算逻辑见拍照章节；扩展动物时不要只隐藏 Sprite 而遗漏“是否在场”的逻辑判定。

### 12.10 声波支持的真实范围

原始啄木鸟需求明确暂时不制作音效。当前项目已经有 `AnimalSoundEmitter` 和 `AnimalSoundWaveManager`，但这些名字指向“可视化声波”，并没有在这组类中播放 AudioClip。不要把已有环形波纹误写成鸟鸣/啄木音频已经完成。

麝鼠在休息、观察、惊扰、进食、潜入、浮出等阶段显式调用声波事件，运动声波则由 Agent 每帧在 Motor 之后更新：速度大于 0.03 时按间隔发射，类型按逃离、入水、陆地优先选择。Prefab 中每个事件可单独启用/关闭，并有半径、环数、时长、透明度与重复间隔。

啄木鸟的现有 Prefab 将所有声波 settings 的 `enabled` 设为 0；其行为代码也没有专用鸟鸣或啄木声音事件。因此当前承诺的可观察行为已经与音频制作解耦。

声波 Manager 按需建立共享 Mesh、Material 和最多 256 个可复用波纹对象；先复用空闲对象，到达上限时覆盖进度最靠后的对象。每个波纹通过 `MaterialPropertyBlock` 设置进度、颜色、环数、断裂与不规则参数；不是每帧创建一张贴图。进度使用 `1 - (1 - t)^2` 扩散，开头淡入、后半段淡出。

### 12.11 植被：树干实体、树冠遮挡、栖息点是三套独立职责

植被最初要达到的视觉和交互目标很具体：Tree/Bush 的分层组合看起来接近 Complete 参考图；玩家不能穿过树干/中心，但可以走进树冠下面；叶冠应遮住玩家，而不应连树自身的树干与中心也全部遮没。因此“碰撞轮廓”“美术外观”“遮挡底板”不能直接共用同一张 Sprite 的透明范围。

当前 [Test_Tree.prefab](AnimalGame/Assets/Prefabs/Environment/Vegetation/Test_Tree.prefab) 的关键顺序是：Canopy Occlusion 1099、Leaves 1100、Trunk 1101、Center 1102。Bush 的 Center 当前为 1103。更高排序的树干/中心保持可读，玩家等较低排序对象则在遮挡底板下；不是依靠单纯把 Leaves 排序无限提高解决。

| 组件 | 数据/机制 | 修改时的边界 |
| --- | --- | --- |
| [HeightMapPlacedObject.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapPlacedObject.cs) | 地图位置、采样高度、编辑用 footprintRadius | 记录布置，不等同于真实障碍碰撞 |
| [HeightMapObstacleFootprint.cs](AnimalGame/Assets/Scripts/MapTest/HeightMapObstacleFootprint.cs) | 实体核心的圆形米制半径，注册 ActiveFootprints | 玩家行走/翻滚、动物地面避障、直接视线共享它；叶冠不应挂同样的实体核心 |
| [VegetationCanopyOccluder.cs](AnimalGame/Assets/Scripts/MapTest/VegetationCanopyOccluder.cs) | 64×64 临时菱形/圆形黑色填充 Sprite | 解决线稿叶冠透明内部露出玩家的问题，不新增碰撞 |
| [TreeHabitat.cs](AnimalGame/Assets/Scripts/MapTest/TreeHabitat.cs) | 健康类型、树心、停靠半径、活动树注册 | 让乔木成为鸟类目标；灌木不自动成为乔木 |
| [VegetationContactFade.cs](AnimalGame/Assets/Scripts/MapTest/VegetationContactFade.cs) | 玩家/动物靠近时整体降低植被 Sprite alpha | 后续加入的接近可读性效果，与原始完全遮挡意图需要一起调和 |

当前 Tree、Bush、Dead Tree 的实体核心半径均为 0.6 m，而根节点编辑 footprintRadius 仍为 0.3 m。实际阻挡应看 `Solid Core` 上的 `HeightMapObstacleFootprint`，不要只修改根组件中的编辑半径。模型 Transform 缩放不会自动把这个逻辑米制半径同步变大，换贴图后要重新校准。

Leaves 的基础透明度在 SpriteRenderer 颜色中，当前 Tree/Bush 均约 0.502；贴图本身可保持完整 alpha。遮挡底板基础 alpha 为 1，所以仅仅降低叶片 alpha 并不会自动露出底板下的玩家；两者控制不同效果。底板形状是菱形或圆形近似，不是根据任意新叶片自动生成的精确轮廓。

后续接近淡出的工程目的，是让玩家和动物进入植物附近时不完全丧失主体位置判断。当前 `VegetationContactFade` 对整棵植物所有 Sprite（包括树干、中心、叶片、遮挡底板）统一乘以 `1 - reduction`；没有改变实体阻挡。

```text
combinedRadius = plantRadius + actorRadius + contactPadding
edgeDistance = max(0, centerDistance - combinedRadius)
若 edgeDistance > fadeStartDistance: reduction = 0
否则 reduction = lerp(fadeStartAlphaReduction, contactAlphaReduction,
                       1 - edgeDistance / fadeStartDistance)
最终 alpha = 已保存基础 alpha × (1 - reduction)
```

当前默认边缘距离 3 m 开始、起始降低 50%、接触降低 90%、额外边距 0.05 m；玩家半径优先来自 TraversalEvaluator，动物来自各自 Config，植物半径取根 footprint 与有效实体核心中的较大值。多个角色同时接近时取最大降低值，而不是把 alpha 重复相乘。

需要注意这一实现的具体边界：3 m 之外 reduction 为零，一旦进入范围就至少降低 50%；没有额外时间平滑，所以默认参数在淡出边界可能发生突变。并且原本 alpha≈0.502 的叶片在接触时只剩约 0.0502，原本完全不透明的遮挡底板只剩 0.1。看到树冠附近玩家显现时，应先判断是不是接近淡出，而不是重新归咎为排序失效。

[Test_Lotus.prefab](AnimalGame/Assets/Prefabs/Environment/Vegetation/Test_Lotus.prefab) 与 [Test_Australis.prefab](AnimalGame/Assets/Prefabs/Environment/Vegetation/Test_Australis.prefab) 则提供水生植物外观、食物目标和接近淡出，不要求存在阻挡组件。荷叶额外拥有黑色背景 Sprite；布置它们不等于自动编辑地图水深。

### 12.12 编辑器工具、扩展流程与配置回写风险

当前 [AnimalAgentEditor.cs](AnimalGame/Assets/Editor/AnimalAgentEditor.cs) 绘制日常范围、警觉/听觉范围、直接视角等 Gizmos；[PileatedWoodpeckerBehaviourEditor.cs](AnimalGame/Assets/Editor/PileatedWoodpeckerBehaviourEditor.cs) 提供 `Bind Nearest Tree`、吸附出生停靠点、选择出生树，并展示出生树、当前树、目标树的关系。它们用于把地图米制范围转换成编辑器中可见的辅助线，不应通过拖大贴图猜测警觉范围。

现有生成/安装工具：

- [AnimalPrefabGenerator.cs](AnimalGame/Assets/Editor/AnimalPrefabGenerator.cs)：创建缺失的麝鼠原型与配置，给 Bush/Lotus/Australis 安装食物来源；菜单 `Animal Game/Animals/Rebuild Muskrat Prototype`。
- [PileatedWoodpeckerPrefabGenerator.cs](AnimalGame/Assets/Editor/PileatedWoodpeckerPrefabGenerator.cs)：创建缺失的鸟类原型，菜单 `Animal Game/Animals/Rebuild Pileated Woodpecker Prototype`。
- [AnimalDiscoveryPrefabInstaller.cs](AnimalGame/Assets/Editor/AnimalDiscoveryPrefabInstaller.cs)：维护麝鼠认知组件及雪花场资源，菜单 `Animal Game/Animals/Install Discovery Visual On Muskrat`。
- [TreeHabitatPrefabInstaller.cs](AnimalGame/Assets/Editor/TreeHabitatPrefabInstaller.cs)：给固定的 Test_Tree/Test_Dead_Tree 配置栖息组件，菜单 `Animal Game/Vegetation/Install Tree Habitats`。
- [VegetationContactFadePrefabInstaller.cs](AnimalGame/Assets/Editor/VegetationContactFadePrefabInstaller.cs)：给 Vegetation 文件夹中缺少该组件的 Prefab 安装接近淡出，菜单 `Animal Game/Vegetation/Install Contact Fade On All Prefabs`。
- [WaterPlantPrefabGenerator.cs](AnimalGame/Assets/Editor/WaterPlantPrefabGenerator.cs)：水生植物创建与荷叶背景补齐，菜单 `Animal Game/Level/Rebuild Water Plant Prefabs`。

以上部分工具通过 `InitializeOnLoad + EditorApplication.delayCall` 自动检查资产，不只有点击菜单时才执行。两种动物配置已有资产时通常保留现有配置，而 `Rebuild ... Prototype` 会重建对应 Prefab，因此对原型外观/组件的手动调整可能被覆盖。不要将 Rebuild 当作普通刷新按钮。

尤其 `TreeHabitatPrefabInstaller` 对两个固定路径的 Prefab 会重新设置健康类型、树心和由实体核心推导的停靠半径；如果直接修改这两个原型的特殊栖息参数，下一次编辑器加载可能被它同步。相反，ContactFade installer 已有组件时跳过，通常保留现有参数。遇到“修改后又变回去”，要先查对应 installer 的写入条件，而不是只查 OnValidate。

新增物种的最小流程：创建新的 `AnimalSpeciesConfig`，实现一个 `AnimalBehaviourSet`，组装 Agent/Motor/Perception/外观/发现/照片相关组件；在 `AnimalDailyBehaviourKind` 增加新类型时保留原枚举数字对应关系，以免已有序列化行为串位；确保所有退出路径停止移动、恢复外观、清理临时目标，并让受惊结束进入 Hiding 而不是永久禁用实例。若支持攻击，必须实现 SupportsAggression 与攻击阶段的实际行为，不能只配概率。

新增乔木的最小流程：配置地图放置组件，在实体树心设置独立 `HeightMapObstacleFootprint`，分离叶冠与实体核心的排序/透明度，挂 `TreeHabitat` 并设置健康类别和停靠半径。再把场景鸟实例绑定到它，检查周围活动范围内确实存在可选目标。只有 Sprite 的装饰树不会自动成为鸟类栖息点。

### 12.13 建议验收场景与现有局限

以下是设计验收建议，不代表已经自动运行的测试结果：

1. 在同一距离分别静止、低速、高速接近；保持足够重复次数，比较发现率，而不是用一次随机结果判断代码正确性。再比较正面无遮挡、背后和树干遮挡，确认后两者仍可被听觉概率发现。
2. 动物进入 Curious 后离开警觉圈，确认不是立即恢复；重新进圈会中断“丢失玩家”计时。对啄木鸟确认任何概率配置都不会触发现有未实现的攻击。
3. 扫描同一只动物、吓跑、等待返回，确认它仍为已知外观；未识别动物潜入时雪花场也必须消失。受惊后的 `Hiding` 期间不应被生物扫描或拍照当作在场目标；日常潜水仍属于 `Daily`，当前扫描不检查其 Sprite 可见度，不能把两种隐藏混为同一状态。
4. 对麝鼠分别测试有水、无水、玩家守住水点、附近有障碍的场景；隐藏时间到但不安全时继续等待，不能强制在玩家旁边冒出。
5. 对啄木鸟测试单树、多树、无枯树、出生树被移除、不规则地图边界，确认可用行为过滤和退回逻辑；当前统计类别目标应为健康/枯死 40/60，而非历史三类比例。
6. 玩家经过树冠下时，先关闭接近淡出核对基础遮挡和树心排序，再启用淡出核对靠近后的可读性；实体树干仍应阻挡通行。不要把两种情况下的不同显现结果混为一个遮挡 bug。
7. 同时放置大量植物和动物，用 Profiler 检查每棵植物遍历动物的代价，以及多只麝鼠同帧寻水时的峰值。目前注册集合减少了反复查找对象，但没有空间分区和统一 AI 调度。

本系统当前是可扩展的原型生态行为，不是完整野生动物模拟：没有存档持久化、繁殖、领地争夺、食物消耗、三维视觉遮挡或全局路径规划。保留这些边界有助于后续程序员判断该补充新层，还是只需调整已有字段。

## 13. 开发工作流、数据生命周期与扩展

### 13.1 从设计问题决定改动层

**设计目的。** 项目经历多轮手感试验，许多要求都是“只改这一层，不影响其他已完成内容”：缩小身体不改既有玩法、移动重心图标不自动增加侧翻难度、修复新高度图不削弱旧地图的越障限制、关闭调试信息不取消检测。这要求接手程序员先理解改动的意图，再选择配置或算法入口。

建议先把需求写成“原有问题 → 期望可观察变化 → 不应改变的行为”。例如：

```text
问题：连续山坡被灰度量化成大量意外 Step Block。
期望：同样输入能连续通过这些伪台阶，坡向变化更稳定。
保留：真实大台阶 / 悬崖仍能阻挡，测试图的玩家能力不降低。
实现层：源图与每地图Detail平滑/Raw复核，而非首先抬高共享Step阈值。
验证：伪台阶、真实落差、旧测试图三组一起比较。
```

配置字段旁的 Tooltip 是读取意图的线索，但有些初值和描述已经落后于 Prefab。实际调参沿“场景绑定 → Prefab / ScriptableObject → 场景 override → 运行时覆盖”检查，不能仅以搜索到的第一个常量为准。

### 13.2 新增地图的工作流

1. 在 Unity 工程中准备高度源、必要的明确可玩掩码，并设置正确导入格式。8 位图、R16 和经过艺术处理的图片具有不同精度限制。
2. 复制一个现有 `HeightMapLevelAsset` 作为新地图定义，设置源图、物理长宽、高度范围、Surface / Detail 平滑和显示尺度。保留原地图资产便于对照。
3. 在新场景放置一个明确绑定该资产的 `MapTestSceneController` 与一个 `HeightMapPlayerSceneBootstrap`。不要在同一可玩场景重复放多份 Bootstrap。
4. 用出生点工具选择可采样位置；进入游戏核对其周围支撑足迹、台阶、静态水和障碍，而非只检查中心点有效。
5. 在固定地图上摆放环境 Prefab，检查 `HeightMapPlacedObject` 的米坐标，以及实体阻挡、栖息地组件是否匹配对象用途。
6. 用第 11 章的画笔编辑地表种类与水深，烘焙并保存资产；它们不是通过运行时玩家移动写回的地图内容。
7. 按需要加入 Build Settings，确认启动顺序。重新进入 Play 对照相机 Size、角色屏幕尺寸、UI 半径、移动米数与地形检测。

这个流程实现的是持续编辑的关卡，而不是一键把所有图片转换成已经平衡好的可玩地图。地图扩大后，路径成本、纹理空间分辨率、障碍数量和动物分布都需要分别检查。

### 13.3 哪些数据保存了，哪些没有

**设计背景。** “地图是永久固定的”“扫描后永久认知”“保存照片”听起来都涉及持久化，但目前对应不同数据生命周期。程序员必须说明保存到哪里，不能用一个永久布尔值代替完整存档。

| 内容 | 当前主要载体 | 生命周期 / 限制 |
| --- | --- | --- |
| 地图高度源、米制尺寸、平滑、材质 Palette | `HeightMapLevelAsset` 与源纹理 | 编辑器资产保存后持久存在 |
| 地表种类绘制、水深绘制、烘焙引用 | 地图资产与生成纹理 | 保存资产 / 烘焙输出，不跟玩家操作动态改变地表归属 |
| 出生点、场景树木 / 动物摆放、绑定引用 | `.unity` 场景及 Prefab 引用 / override | 需要保存场景；并非都在地图 ScriptableObject 内 |
| 通用机器人、相机、动物能力参数 | `.prefab` / 物种 `.asset` | 共享引用会影响多张地图；Play 中实例修改不自动成为资产修改 |
| CPU Raw / Detail / Surface 高度场 | 运行时数组与纹理 | 初始化生成；不是每次采样重新写进源 PNG |
| 驾驶速度、侧翻能量、扶正进度 | 运行时组件字段 | 重建玩家后重新初始化 |
| 生物永久认知 | `DiscoverableEntity` 实例状态 | 不自动到期；没有跨新实例 / 应用重启的存档恢复 |
| 动物躲藏与再出现 | 动物实例状态机及计时 | 不等于销毁后按物种 ID 重新生成存档对象 |
| 照片相册 | `PhotoAlbumService` 静态列表 | 可随当前托管域存活，不是磁盘照片文件或完整存档 |
| 扫描通行图标 | 运行时快照与对象池 | 有存活时间，之后清理；不修改静态地表 |

目前没有在本次核对的运行时脚本中发现覆盖这些系统的统一存档服务。若新增存档，应先设计稳定的实体标识、地图版本和状态恢复顺序；`GetInstanceID()` 仅适合一次运行内去重，不能充当永久存档 ID。

### 13.4 重新加载场景与调试入口

源码：[SceneReloadShortcut.cs](AnimalGame/Assets/Scripts/SceneReloadShortcut.cs)。R 是为了在侧翻、瘫痪和试验后快速回到场景初始状态，不是机器人扶正能力。

`BeforeSceneLoad` 创建唯一监听对象并 `DontDestroyOnLoad`；R 按下后优先按当前场景 Build Index 重新加载，否则尝试场景路径。`reloadInProgress` 防止一次重载中重复提交，`sceneLoaded` 清理标志。监听不是每张地图手工挂一个组件，也没有限定只在 Editor 编译；正式构建是否保留这个开发捷径需要单独决策。

重载场景不等于整个进程或托管域重启：内存中的静态服务可能保留，而场景组件重建。因此验证永久认知、照片列表、全局 Shader 和输入缓存时，应分别测试重载场景、停止再进入 Play、关闭应用三种情况。

| 入口 | 用途 | 与正式玩法的关系 |
| --- | --- | --- |
| Q | `TraversalOverlayUI` 全网格通行调试 | 独立于 LB 地形扫描快照 |
| Bootstrap `showRobotTerrainData` | 右上角地形信息框 | 当前玩家相关场景关闭；不影响检测 |
| Bootstrap `showFrameRate` | FPS显示 | 与地形信息开关独立 |
| DiscoverableEntity Context Menu | `Debug/Discover`、`Debug/Reset To Unknown` | 显式测试认知状态，不是自动扫描规则 |
| 地图与动物自定义 Inspector / Gizmo | 参数、出生点、行为范围与绑定可视化 | 编辑辅助，需核对写回的资产 / 场景 |

### 13.5 资源生成器不是无副作用的刷新按钮

**工程目的。** 生成器和 Installer 降低初始搭建与旧资源迁移成本，但有些命令会写 Prefab、动画、输入配置或美术派生文件。设计者已调好的参数也属于内容，不应为了修复一个缺引用就无条件重建整套资源。

主要入口包括：

- [RobotMapPrefabGenerator.cs](AnimalGame/Assets/Editor/RobotMapPrefabGenerator.cs)：`Animal Game/Rebuild Robot Map Prefabs`、`Animal Game/Repair Gamepad Input Axes`。
- [AnimalPrefabGenerator.cs](AnimalGame/Assets/Editor/AnimalPrefabGenerator.cs)：麝鼠原型生成；[PileatedWoodpeckerPrefabGenerator.cs](AnimalGame/Assets/Editor/PileatedWoodpeckerPrefabGenerator.cs)：啄木鸟资源与场景原型搭建。
- [AnimalDiscoveryPrefabInstaller.cs](AnimalGame/Assets/Editor/AnimalDiscoveryPrefabInstaller.cs)：认知显示组件安装。
- [TreeHabitatPrefabInstaller.cs](AnimalGame/Assets/Editor/TreeHabitatPrefabInstaller.cs)、[VegetationContactFadePrefabInstaller.cs](AnimalGame/Assets/Editor/VegetationContactFadePrefabInstaller.cs)：栖息地与接触淡化组件迁移。
- [WaterPlantPrefabGenerator.cs](AnimalGame/Assets/Editor/WaterPlantPrefabGenerator.cs)：水生植物 Prefab 与相应派生资源。

这些文件中存在 `InitializeOnLoad` 自动检查路径，但不意味着每次编译都会无条件覆盖所有资产。具体重建 / 缺失补齐 / 版本迁移条件应查看相应入口。运行菜单前保存并查看版本控制状态，执行后审阅实际 diff，特别留意 Prefab 数值、GUID、场景引用与导入设置。

### 13.6 新功能接入与时间步

**设计目的。** 同一只右杆在正常驾驶时调整重心、在拍照时调整取景、在扶正时进行反复发力。新功能需要接入控制权，而不是监听到输入就无条件执行。动画同样应在退出、中断和侧翻时正确恢复。

现有代码的更新阶段不是完全统一的调度器。地图准备在 -1000；拍照控制在 -100；普通驾驶处理后，高度检测为 150，相机跟随 200，镜头反馈 250，照片 / 扫描 UI 300，生物雷达 310，通行显示 325，UI 翻滚 350。数字解释组件的相对执行顺序，不能把所有 Update 与所有 LateUpdate 合成一条不区分阶段的调用栈。

动画与计时也混用不同时间：对焦、扫描 UI 多采用 unscaled 时间，实际运动、检测波与不少状态计时使用 scaled 时间。将 `Time.timeScale` 设成 0 不一定冻结所有界面；新暂停系统应显式定义哪些功能继续、哪些输入无效，并停止手柄输出。

新增一个系统至少检查：初始化引用、进入条件、输入占用、退出 / 取消 / OnDisable 清理、事件订阅解除、运行时资源释放，以及是否依赖唯一主相机或全局 UI 圈。不要仅补一个 Update 就认为模式交互完整。

## 14. 实现差异、回归验证与源码索引

### 14.1 文档如何使用

本 README 已覆盖当前自有代码的主要系统与编辑链路；“完成文档”不意味着每个历史设计目标都已实现。它以设计目的解释当前代码，同时记录近似、缺口和实验路径，不作为已通过游戏体验验收的声明。

设计者最初没有明确指定的工程细节，以实现解释记录。数值是本次核对时的资产快照；今后修改后，运行中的有效配置仍应沿引用核对，不将文档中的某个数字当成不可调整的常量。

### 14.2 接手时必须了解的实现差异

| 设计方向 / 容易误解之处 | 当前事实 | 后续接手注意 |
| --- | --- | --- |
| 更连续的高度、减少不该出现的台阶 | Surface平滑、足迹拟合、Detail平滑与Raw复核分工 | 不把拟合当作直接修改高度图，也不保证所有悬崖已完美识别 |
| 扶正时机械臂撑到支撑面 | 当前依赖输入方向和幅度的虚拟支撑资格 | 不是已经完成真实墙体接触求解 |
| 啄木鸟三种健康树的50/35/15选择 | 当前树健康枚举与配置未完整对应三分类 | 第12章记录实际状态与权重，不把设计比例直接写成事实 |
| 永久退出认知缺失 | 同一实体的永久标志不自动到期 | 没有统一跨存档持久化 |
| 专属照片结算 | 当前全局参考图片演示模式 | 非逐物种参考图路由，动态UI仍需单独验证 |
| 按下快门那一刻拍到动物 | 事件在白闪峰值，当前约延后0.04s | 严格按键瞬时冻结需改变采样时机 |
| 彩色等高图逆映射生成高精度图 | 离线转换工具存在，但Rocky资产仍引用New_GreyMap | 生成文件存在不等于运行地图已切换 |
| 静态地表没有实时内容变化 | 地表烘焙保存；范围裁剪 / 等高线仍要渲染 | 不解释成零性能成本，也不混同水纹显示规则 |
| 地图面积9倍且画面比例相同 | 米数边长3倍，当前世界显示边长约4.33倍 | 需要单独核对玩家、镜头与移动尺度 |
| 缩放Main UI后所有消费者跟随 | 共享圈半径已有读取；部分网格仍假定屏幕中心 | 任意平移 / 分屏 / 多相机并未因此全部支持 |

### 14.3 分层回归建议

这些是建议的验证用例，不是本次已经执行的 Play Mode 或自动化测试。

| 层级 | 建议检查 | 对应设计目的 |
| --- | --- | --- |
| 启动与引用 | 测试图、Rocky、动物测试图分别进入；检查资源缺失与重复实例 | 不因换地图丢失已有能力 |
| 连续地形 | 同速度通过平地、连续坡、量化坡；记录Slope/Step来源 | 去掉数据误差造成的意外台阶 |
| 真实危险 | 大台阶、危险下坡、树干、深水分别验证BlockReason | 保留正确的环境约束 |
| 侧翻 | 前后/左右、单次/多次、下坡、最终摇摆、4n次回正 | 位移、朝向、剩余运动与终态相符 |
| 重心与扶正 | 翻滚中点不消失；支撑黄点；有效推杆、错误方向、断频、回正反冲 | 提供连贯、可操作的失衡与恢复过程 |
| 机械臂 | 回中最短、平滑输入、固定节/伸缩节、二段展开回收、瘫痪选点 | 操作直观且连接关系连续 |
| UI与反馈 | 90°停留、重心不旋转、低高速撞击、逐次落地、失焦停震 | 信息可读且冲击对应事件强度 |
| 地表编辑 | 保存后重开；不同材质、边界、中心浅纹理、UI内外 | 固定可编辑地图及层次清晰的地形表达 |
| 扫描 | 短按双击与长按不串扰、未满取消、动物再次出现仍已知 | 指令明确，认知不因普通计时反复丢失 |
| 拍照 | 半程取消、完成后再按、框外/小目标/多目标、B返回 | 结果对应快门与取景过程 |
| 动物 | 距离/速度/视线发现，物种逃离、躲藏、等待后返回 | 世界持续活动而非受惊后被永久清空 |
| 性能与生命周期 | 大地图/多动物/多树下采样与渲染；重载与退出清理 | 避免固定数据重复生成、对象/震动残留 |

当前已声明 Unity Test Framework，但在本次检查的自有 Assets 代码中未发现专用自动化测试套件。将来可优先为坐标往返、平面拟合、Step残差、材质ID/烘焙输入、快门状态转换等建立可重复测试，再以实机手柄和画面验证感受；“编译无报错”无法代替震动强度和UI可读性验收。

### 14.4 按文件查找剩余入口

正文已链接大多数核心文件；下面补充辅助、旧入口与编辑组件，避免一个文件没有独立长章节就被误认为不存在或可删除。

| 文件 / 文件组 | 阅读位置与用途 |
| --- | --- |
| [RobotMapBootstrap.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMapBootstrap.cs)、[RobotMapDemo.cs](AnimalGame/Assets/Scripts/RobotMap/RobotMapDemo.cs) | SampleScene 的早期自建演示路径；不是正式地图启动替代品 |
| [MapTestSceneBootstrap.cs](AnimalGame/Assets/Scripts/MapTest/MapTestSceneBootstrap.cs) | 单独地图场景的Resources启动入口 |
| [HeightMapPlacedObjectEditor.cs](AnimalGame/Assets/Editor/HeightMapPlacedObjectEditor.cs) | 将选择对象锚定到地图的Editor操作 |
| [HeightMapPlayerSceneBootstrapEditor.cs](AnimalGame/Assets/Editor/HeightMapPlayerSceneBootstrapEditor.cs) | 出生点Inspector、Scene拖动与可视化 |
| [AnimalAgentEditor.cs](AnimalGame/Assets/Editor/AnimalAgentEditor.cs)、[PileatedWoodpeckerBehaviourEditor.cs](AnimalGame/Assets/Editor/PileatedWoodpeckerBehaviourEditor.cs) | 动物范围、参数及啄木鸟绑定的编辑辅助 |
| [AnimalTypes.cs](AnimalGame/Assets/Scripts/Animals/AnimalTypes.cs)、[AnimalSoundTypes.cs](AnimalGame/Assets/Scripts/Animals/AnimalSoundTypes.cs) | 通用状态 / 语义类型；具体行为见第12章 |
| [TraversalSignsGraphic.cs](AnimalGame/Assets/Scripts/MapTest/TraversalSignsGraphic.cs) | 通行标记批量生成UI几何，和每个对象一个独立Canvas不同 |
| [UnknownAnimalStatic.shader](AnimalGame/Assets/Shaders/UnknownAnimalStatic.shader)、[BioScanSignalClip.shader](AnimalGame/Assets/Shaders/BioScanSignalClip.shader)、[AnimalSoundWave.shader](AnimalGame/Assets/Shaders/AnimalSoundWave.shader) | 认知缺失、生物信号与动物声音的视觉呈现，不直接决定AI状态 |
| [StaticWaterEditorPreview.shader](AnimalGame/Assets/Editor/StaticWaterEditorPreview.shader) | 水域编辑预览；运行时地图显示走地图Shader链路 |

### 14.5 本次文档完成范围

本轮工作补全系统说明、设计原由、实际配置与已知差异，并检查文档结构及文件链接；没有更改游戏脚本、Prefab、场景、地图数据或 HANDOFF，也没有重跑烘焙或资源生成菜单。

README 是接手与讨论设计的说明书，不是要求每次任务重新遍历整个项目的工作规章。后续维护按明确任务更新相关内容；技术改动应围绕所需系统核对，历史 HANDOFF 仅在需要补充背景时查阅、仅在设计者要求时更新。

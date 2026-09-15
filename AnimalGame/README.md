# AnimalGame — Unity 工程入口

完整的程序员文档位于上一级的 [README：设计意图与代码实现手册](../README.md)。包含文件结构、运行时装配、地图与通行检测、机器人驾驶与侧翻扶正、镜头与反馈、扫描与拍照、地图编辑、动物与植被，以及配置、扩展和验证说明。

## 快速启动

1. 用 Unity Hub 打开本目录，即包含 `Assets`、`Packages`、`ProjectSettings` 的这一层。
2. 使用工程记录的 Unity `2022.3.16f1`。
3. 测试地图打开 [HeightMapPlayerScene.unity](Assets/Scenes/HeightMapPlayerScene.unity)；落基山脉地图打开 [RockyMountainPlayerScene.unity](Assets/Scenes/RockyMountainPlayerScene.unity)。
4. Play 后的驾驶、重心、机械臂操作见 [第 7 章](../README.md#7-玩家输入与运动)，扫描、拍照操作见 [第 10 章](../README.md#10-地形扫描生物认知与拍照)。

`SampleScene` / `MapTestScene` 属于旧测试入口，不是当前主玩法入口。Inspector 运行时实例修改不会自动保存回 Prefab；各类 `Rebuild` 菜单也不是普通刷新操作，使用前请阅读主文档中的配置回写说明。

本文件仅保留工程入口，系统说明统一维护在上一级 README，避免两份操作与架构说明逐渐不一致。

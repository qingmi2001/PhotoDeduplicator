# Windows 全文件与照片深度去重归档工具 V2.0 (模块化工程版)

本版本完成工程化分层重构（高内聚、低耦合），并正式上线 **V2.0 精准去重与审查闭环**。

## 模块化代码架构

```text
PhotoDeduplicatorGUI/
├── Models/                         # 纯数据模型层
│   ├── FileItem.cs                 # 文件元数据与哈希指纹
│   ├── ConflictGroup.cs            # 冲突分组模型
│   └── ProcessOptions.cs           # 运行配置与统计结果
├── Services/                       # 核心业务引擎层（与 UI 彻底解耦）
│   ├── HashService.cs              # 首尾 4KB 快速抽样 + SHA-256 全量校验
│   ├── MetadataService.cs          # Exif 拍摄时间与文件修改时间提取
│   ├── DiskGuardService.cs         # 磁盘剩余空间防爆仓检测
│   └── DeduplicationEngine.cs      # 扫描、哈希复核、分类归档、报告生成
├── UI/                             # 桌面交互表现层
│   ├── MainForm.cs                 # 主控制台界面
│   └── ConflictReviewForm.cs       # 冲突并排审查窗口（支持 3+ 擂台筛选与快捷键）
├── Program.cs                      # 程序启动入口
└── PhotoDeduplicatorGUI.csproj     # .NET 8 单文件发布工程配置
```

## V2.0 新增特性
1. **SHA-256 哈希精确复核**：改名但内容完全一致的副本直接自动去重；同时拦截“同名同大小但内容不同”的极端碰撞，绝不误删。
2. **审查闭环与擂台模式**：支持 3 个以上同组文件擂台式淘汰；最终胜出者**自动移入 `整理归档/年/月/`**，淘汰副本安全移入弃用区。
3. **键盘盲操快捷键**：`1` 留左、`2` 留右、`←`/`→` 翻页、`Space` 跳过。

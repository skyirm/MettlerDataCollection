# 更新日志

本文件根据项目 Git 提交记录整理。

## 2026-07-24

- `6a520bb` test(device): S470K 单元测试 21 个 case 全过
- `a74ab6a` feat(log): 全局 Serilog sink 推送 Warning+ 日志到 UI 错误抽屉
- `926e69a` feat(device): 实现单工模式（PH_ONLY / COND_ONLY）解析

## 2026-07-23

- `9dd7607` feat(ui): 菜单增加“重新选择设备”入口
- `26759c8` feat(startup): 优先从 Settings 加载启动配置
- `60fc9df` feat(startup): 增加设备类型设置并支持启动设置记忆
- `6a62fed` fix(startup): 修复启动窗口与主窗口切换问题
- `7e05520` refactor(recovery): 历史恢复按设备类型创建解析实例
- `cb6eb17` feat: 增加绘图视图自动回归和操作指南窗口

## 2026-07-22

- `5410d4c` fix(plot): 修复“显示最新数据”窗口不滑动的问题
- `e2ab636` docs: 整理代码注释
- `4838040` feat(startup): 增加启动设置窗口和设备自动发现
- `2365efd` refactor(structure): 按功能整理 Views 目录
- `8495086` refactor(mainwindow): 清理主窗口代码
- `491e968` chore: 移除项目无关文件
- `9302e8e` refactor(device): 将协议解析迁移到 S470K
- `5a295e9` refactor(device): 改用事件推送预处理行
- `7891fd2` refactor(device): 抽出 IDevice 行切分逻辑
- `35b0095` refactor(mainwindow): 删除无用的输出缓存调用
- `e890998` refactor(persistence): 抽出数据持久化服务
- `60cf233` chore: 忽略项目无关文件

## 2026-07-16 至 2026-06-15

- `8eb775a` 增加接口
- `59b61df` 修正错误
- `15de7ee` 增加导出数据目录记忆功能
- `92bd438` 增加数据存储路径设置功能

## 更早版本

- `8791ca9` 更新原始数据命名规则
- `bab9d12` bug fix
- `4f7a06c` update
- `7417f98` bugfix
- `b861d57` bug fix

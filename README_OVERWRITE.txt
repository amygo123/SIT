# SIT 一次性覆盖包（含实时库存，单文件版本）

本包只包含需要**覆盖/新增**的文件（且**没有** partial 文件），确保一次性编过：
- ResultForm.cs                  ← 已整合库存功能（同页签）
- Config.cs                      ← 已扩展库存配置字段（inventory_*）
- InventoryModels.cs             ← 新增
- InventoryClient.cs             ← 新增
- InventoryPivot.cs              ← 新增
- InventoryMatrixControl.cs      ← 新增

## 使用（仓库根目录执行）
1) 解压本压缩包到仓库根目录（允许覆盖同名文件）。**注意：请确保仓库里没有 `ResultForm.Inventory.cs` 这个文件**（如果有，删除它）。
2) 提交并推送：
   git add -A
   git commit -m "feat: realtime inventory (same-tab) + fix braces; remove partial conflicts"
   git push
3) CI 将自动构建。

## 已对齐需求
- 主仓为每次查询后的 Top3（按“可用 QtyOut 合计”动态计算），按钮“快速查看矩阵”
- 仅在主查询完成或窗口显示时刷新；不轮询
- 同页签库存面板：颜色×尺码矩阵（单元格 在库/可用），着色：=0 灰、<10 橙、<0 红
- “更多仓库…”入口 + “综合视图（全部仓）”按钮

## 备注
- 仍看到 NU1603 / NETSDK1137 / CS8632 属正常警告，不影响编译。后续如需，我可以再给一个清理包。

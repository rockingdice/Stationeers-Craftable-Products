# RockingDice's Stationpedia Craftable Products

![RockingDice's Mods cover](About/Preview.png)

Adds a reverse `Craftable` section to Stationeers material pages so players can see what each item can manufacture.

一个独立的 Stationeers / BepInEx 5 Mod，为可作为制造输入的物品百科页新增 `可制造` 分组。

- 分组固定显示在原版 `使用于` 之后。
- 简体中文界面显示 `可制造`，其他语言显示 `Craftable`，不会向非中文界面写入中文字形。
- 分类容器复用原版 `如何制造` 布局，每个产物使用独占一整行的大卡片。
- 新增分类会在页面显示后的连续两帧重建真实 `ScrollRect.content`，长列表保留原版滚动条与滚轮滚动。
- 反查游戏当前实际加载的全部制造配方，不维护硬编码配方表。
- 每张卡显示产物图标与产物名称。
- `需求` 内显示制造机器、机器等级、能量、温压条件和完整材料来源。
- 材料文本直接复用游戏原版 `Recipe.ToString(RecipeReference)`，与 `如何制造` 的格式和链接一致。
- 点击产物图标可打开对应产物百科页。

## 排序

1. 配方所需素材种类少的排在前面，例如 `2 铁` 优先于 `1 铁 + 1 铜`。
2. 素材种类相同时，当前页面材料用量少的排在前面。
3. 同等用量下，不含合金的配方优先。
4. 普通合金排在基础材料之后，超级合金更靠后。
5. 材料加工链更深、总用量更大的配方依次靠后。
6. 最后按机器等级、产物名和机器名稳定排序。

## 构建

```powershell
$env:STATIONEERS_DIR = 'C:\Program Files (x86)\Steam\steamapps\common\Stationeers'
dotnet msbuild .\StationpediaCraftableProducts.csproj /t:Rebuild /p:Configuration=Release
```

## 安装

将 `bin/Release/StationpediaCraftableProducts.dll` 放到：

`Stationeers/BepInEx/plugins/StationpediaCraftableProducts/StationpediaCraftableProducts.dll`

插件不修改配方、物品或存档，只扩展百科 UI；安装过程不会关闭 Stationeers。

从 Steam 创意工坊安装 DLL 时还需要 [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad)。

## Source / 源码

https://github.com/rockingdice/Stationeers-Craftable-Products

本项目的代码、文本和创意工坊图片在作者指导下使用了生成式 AI 辅助，并由作者进行了实机测试。

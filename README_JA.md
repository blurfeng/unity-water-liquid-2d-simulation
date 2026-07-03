<p align="center">
  🌍
  <a href="./README.md">中文</a> |
  <a href="./README_EN.md">English</a> |
  日本語
</p>

# Liquid 2D Simulation — Unity 2022.3 ブランチ

これは [Liquid 2D Simulation](https://github.com/blurfeng/unity-water-liquid-2d-simulation) の **Unity 2022.3 バージョンブランチ**（URP 14）です。

> 📖 **機能の概要・使い方・パラメータの詳細は、main メインブランチの README を参照してください：**
> [中文](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README.md) ·
> [English](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_EN.md) ·
> [日本語](https://github.com/blurfeng/unity-water-liquid-2d-simulation/blob/main/README_JA.md)

## このブランチの役割 / main との違い
このブランチはプラグインを `Unity 2022.3`（URP 14）で動作させます。**ソースコードは単一ソース設計**で、同一の `Runtime` / `Editor` コードがバージョンマクロ（`#if UNITY_6000_0_OR_NEWER`）により Unity 6 と Unity 2022.3 の両方に対応し、main の `dev` ブランチからマージされます。したがって**機能は main と同一で、使い方も同じ**です。main との違いは、内部実装と設定の入口のみです：

| | main | 本ブランチ（2022.3） |
| --- | --- | --- |
| エンジン | `Unity 6`（URP 17） | `Unity 2022.3`（URP 14） |
| 描画 | Render Graph | 従来の命令型 URP パイプライン（Render Graph なし）、表現は同じ |
| Rendering Layer 設定 | `Project Settings → Tags and Layers → Rendering Layers` | `Project Settings → Graphics → URP Global Settings → Rendering Layers (3D)`、使い方は同じ |
| デモシーン（Samples） | Unity 6 版 | 2022.3 向けに調整 |
| 更新頻度 | ソースの真源 | main からマージ、わずかに遅れる |

## UPM でインストール
Package Manager で `+` → `Install package from git URL...` をクリックし、以下を貼り付けます：
```
https://github.com/blurfeng/unity-water-liquid-2d-simulation.git?path=Assets/Plugins/Liquid2DSimulation#2022.3
```
> **デモシーン（Samples）が不要**なら、main メインブランチのリンクを直接使うこともできます（単一ソースで両バージョン共通、末尾の `#2022.3` を外すだけ）。2022.3 向けに調整済みの Samples が欲しい場合のみ、上記の `#2022.3` リンクを使用してください。

## dev から本ブランチへ更新を取り込む（メンテナー向け）
本リポジトリは原則としてソースコードを変更せず、ソースはメインリポジトリから単一ソースでマージします。以下のコマンドは、メインリポジトリの `dev` ブランチにある最新の `Runtime` / `Editor` / `package.json` を本 2022.3 ブランチに取り込みます：
```bash
git fetch origin
git checkout origin/dev -- \
  Assets/Plugins/Liquid2DSimulation/Runtime \
  Assets/Plugins/Liquid2DSimulation/Editor \
  Assets/Plugins/Liquid2DSimulation/package.json \
  Assets/Plugins/Liquid2DSimulation/package.json.meta \
  Assets/Plugins/Liquid2DSimulation/CHANGELOG.md \
  Assets/Plugins/Liquid2DSimulation/CHANGELOG.md.meta
```

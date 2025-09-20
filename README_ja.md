# Liquid 2D Simulation - 2D流体シミュレーション

Liquid 2D Simulation 是一款用于 Unity 的2D流体模拟系统。\
Liquid 2D Simulation is a 2D fluid simulation system designed for Unity.\
Liquid 2D Simulation は、Unity 向けに設計された 2D 流体シミュレーションシステムです。

![](Documents/Liquid2D_demo.gif)

## 🌍 语言/Language/言語
- ***阅读中文文档 > [中文](README.md)***
- ***Read this document in > [English](README_en.md)***
- ***日本語のドキュメントを読む > [日本語](README_ja.md)***

## 紹介
この流体粒子システムを使用することで、水、マグマ、石油など、さまざまな質感の2D流体を素早くシミュレーションできます。\
本システムは大量の粒子生成をサポートしており、モバイルプラットフォームでも利用可能です。\
`Render Graph` フレームワークを活用し、メインカメラ1つだけで流体粒子を `GPU Instance` によってレンダリングします。\
従来の、別カメラで Render Target にレンダリングする方式と比べ、大幅にレンダリング効率が向上します。  

# TODO
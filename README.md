# Liquid 2D Simulation - 2D流体模拟

Liquid 2D Simulation 是一款用于 Unity 的2D流体模拟系统。\
Liquid 2D Simulation is a 2D fluid simulation system designed for Unity.\
Liquid 2D Simulation は、Unity 向けに設計された 2D 流体シミュレーションシステムです。

![](Documents/Liquid2D_demo.gif)

## 🌍 语言/Language/言語
- ***阅读中文文档 > [中文](README.md)***
- ***Read this document in > [English](README_en.md)***
- ***日本語のドキュメントを読む > [日本語](README_ja.md)***

## 简介
使用本流体粒子系统，你可以快速实现2D流体的模拟，包括水、岩浆、石油等不同质感的流体。\
本系统支持生成大量的粒子，可以用于移动平台。\
使用 `Render Graph` 框架只使用一个主相机，并通过 `GPU Instance` 方式渲染流体粒子。\
和传统的单独相机渲染到 Render Target 的方式相比，渲染效率大幅提升。

## 📜 目录

- [简介](#简介)
- [💻 环境要求](#-环境要求)
- [🌳 分支](#-分支)
- [🌱 快速开始](#-快速开始)
  - [1.安装插件](#1安装插件)
    - [使用 UPM](#使用-upm)
    - [package](#package)
  - [2.配置 Layer](#2配置-layer)
  - [3.添加 Renderer Feature](#3添加-renderer-feature)
  - [4.创建流体粒子预制体](#4创建流体粒子预制体)
  - [5.创建粒子生成器](#5创建粒子生成器)
- [Renderer Feature 设置指南](#renderer-feature-设置指南)
- [流体粒子设置指南](#流体粒子设置指南)
- [粒子生成器设置指南](#粒子生成器设置指南)
- [效果展示](#效果展示)

## 💻 环境要求
- Unity6000.2 或更新的版本。（或者使用Unity2022.3的分支版本）
- URP通用渲染管线。
- 使用 Renderer Graph 框架进行渲染。
- 与着色器兼容的平台。

## 🌳 分支
- main 主分支。Unity6版本。
- 2022.3 Unity2022.3版本本分支。如果你需要在更旧的版本上使用此系统，可以查看此分支。更新会慢于主分支。

## 🌱 快速开始
按你喜欢的方式安装插件，然后你可以直接查看演示场景学习如何使用此系统。\
或者，按下面的步骤一步步来。
### 1.安装插件
#### 使用 UPM
```
TODO https://github.com
```
通过 UPM 安装插件到你的项目。如果你需要演示场景，使用下面的方式导入。
TODO：导入演示场景。

#### package
使用安装包将插件安装到你的项目。

### 2.配置 Layer

### 3.添加 Renderer Feature

### 4.创建流体粒子预制体

### 5.创建粒子生成器

## Renderer Feature 设置指南

## 流体粒子设置指南

## 粒子生成器设置指南

## 特性展示
# Gameplay Model 与 Unity 视图分离

拥有属性的 Gameplay 对象使用纯 C# 类型，并可直接继承 `StatsOwner<TStatSet>`；它们不继承 `MonoBehaviour`。Unity 组件主要承担视图与适配职责，观察 Gameplay Model 的状态，从而让 Stats 核心与玩法逻辑保持引擎无关，同时避免 `MonoBehaviour` 的单继承限制影响领域模型。

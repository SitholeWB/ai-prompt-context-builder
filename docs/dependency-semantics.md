# Dependency Semantics and Scoring Model

## 1. Neutral Graph Node and Edge Model

The dependency graph stores language-neutral nodes and edges:
- **Node Kinds**: `File`, `Type`, `Class`, `Interface`, `Struct`, `Record`, `Enum`, `Function`, `Method`, `Module`, `Package`, `Component`, `Template`, `Style`, `Route`, `Service`, `Hook`, `Store`, `Configuration`, `Asset`.
- **Relationship Types**: `Root`, `Import`, `Export`, `ReExport`, `DynamicImport`, `ProjectReference`, `BaseType`, `Interface`, `Implementation`, `ConstructorDependency`, `ParameterType`, `ReturnType`, `PropertyType`, `FieldType`, `ObjectCreation`, `MethodCall`, `ExtensionMethod`, `Attribute`, `Template`, `Style`, `Route`, `ComponentUsage`, `HookUsage`, `CodeBehind`, `PartialDeclaration`, `CompanionFile`, `ScriptReference`, `StyleReference`, `Service`.
- **Confidence Levels**: `Verified`, `High`, `Medium`, `Low`, `Unresolved`.

## 2. Importance Scoring Model

The importance scorer evaluates every discovered node deterministically:

| Target Dependency Type | Base Weight |
| :--- | :--- |
| Root file | 1000 |
| Framework template | 120 |
| Framework code-behind | 115 |
| Constructor dependency | 110 |
| Base type | 105 |
| Implemented interface | 100 |
| Direct import | 95 |
| Public parameter / return type | 90 |
| Component usage / Framework service | 90 |
| Property type | 85 |
| Field type / Object creation / Route | 80 |
| Hook / Store usage | 75 |
| Invoked method / Extension method container | 70 |
| Generic argument | 65 |
| Exception type | 60 |
| Style companion | 55 |
| Attribute / Annotation / Decorator | 50 |
| Local variable type | 40 |
| Test file | 30 |
| Story file | 25 |
| Binary asset | 0 |

### Scoring Adjustments
- **Depth Multiplier**: `Math.Max(0.2, 1.0 - (depth - 1) * 0.15)`
- **Direct Root Dependency Bonus**: `+25`
- **Multiple References Bonus**: `min(30, (inEdges.Count - 1) * 10)`
- **Multiple Distinct Relationships**: `min(25, (distinctRelationships.Count - 1) * 12)`
- **Confidence Multiplier**: `Verified: 1.2`, `High: 1.0`, `Medium: 0.8`, `Low: 0.6`, `Unresolved: 0.3`

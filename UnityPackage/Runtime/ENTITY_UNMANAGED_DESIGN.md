# Entityのunmanaged化に関する設計メモ

最終更新: 2026-08-15  
状態: `EntityId`は採用済み、Burst/Job連携は未採用

## 背景

現在の`Entity`は次の情報を持つ`readonly struct`である。

```csharp
public readonly struct Entity
{
    public readonly int Index;
    public readonly uint Version;
    public readonly World World;
}
```

`World`はclassであるため、`Entity`にはmanaged参照が含まれる。したがって現在の`Entity`は
`unmanaged`制約を満たさず、そのままBurstやunmanagedコンテナへ渡すことはできない。

コード上の「64-bit Generational Entity ID」という説明が指す64 bitは、
`Index`と`Version`から成る識別部分である。公開`Entity`全体は`World`参照も保持するため64 bitではない。

## 現行設計の利点

- `entity.Add()`、`entity.Get<T>()`、`entity.Despawn()`のように、Worldを別途渡さず自然に操作できる。
- 同一性を`Index + Version + World`で判定でき、別Worldの同じIndex/Versionを混同しない。
- `default(Entity)`はWorldを持たない無効なEntityとして明確に扱える。
- Worldの検索やグローバル状態を介さず、Entityから所有Worldへ直接到達できる。

## 既存EntityからWorld参照を外す場合の問題

例えば`World`参照を整数の`WorldId`へ置き換えれば、Entity自体をunmanagedにできる。

```csharp
public readonly struct Entity
{
    public readonly int Index;
    public readonly uint Version;
    public readonly int WorldId;
}
```

ただし、現在のinstance APIを維持するには`WorldId`から`World`を解決するレジストリが必要になる。
その場合は次の問題が生じる。

- Entity操作のたびにレジストリ検索が入り、Hot Pathのコストが増える可能性がある。
- World破棄後の参照とWorldId再利用を安全に区別するgeneration管理が必要になる。
- レジストリの寿命、スレッド安全性、Unity Domain Reload時の扱いが新たな規則になる。
- managedなWorldレジストリを参照するinstance APIは、EntityがunmanagedでもBurst内から利用できない。
- 公開APIの互換性を壊すか、内部解決規則を利用者から見えない形で複雑化する。
- `WorldId`をプロセス内だけで有効とするのか、シリアライズ可能な識別子とするのかを決める必要がある。

このため、`World`フィールドを`WorldId`へ機械的に置換するだけでは、Burst/Job対応という目的は達成できない。

## 提案: 通常用Entityとunmanaged IDを分ける

現行の便利な`Entity`を維持し、Worldを含まないunmanagedな識別子`EntityId`を併設する。

```csharp
public readonly struct EntityId
{
    public readonly int Index;
    public readonly uint Version;

    internal EntityId(int index, uint version);
}
```

World境界では、所有Worldを明示して利用する。

```csharp
EntityId id = entity.Id;

if (world.TryGetEntity(id, out Entity resolved))
    resolved.Has<Position>();
```

実際の公開変換APIは`entity.Id`と`world.TryGetEntity(id, out entity)`である。
EntityのVersionは1から開始し、0を`default(EntityId)`の無効値として予約する。
任意の数値から作りにくくするため、`EntityId`のコンストラクタは`internal`とする。

この案には次の性質がある。

- 通常のManaged C#利用では、現在の分かりやすいEntity instance APIを維持できる。
- Jobへ渡す値は8 byteのgenerational IDにできる。
- Worldの取り違えは、Worldを明示するAPI境界で検証できる。
- グローバルなWorldレジストリを導入せずに済む。
- Burst対応では、`EntityId`だけでなくComponent StorageをどうJobへ公開するかを別途設計する必要がある。

一方で、`EntityId`単体にはWorld情報がないため、異なるWorldのID同士を値だけで比較すると同値になり得る。
永続保持、辞書キー、シリアライズ、Worldをまたぐ受け渡しを許可するかは明示的に制限または設計する必要がある。

## 代替案

### World識別子を含むunmanagedハンドル

`Index + Version + WorldId + WorldVersion`のような完全にunmanagedなハンドルを用意する案。
Worldの取り違えを値だけで検出しやすいが、サイズが増え、World IDの発行・再利用規則が必要になる。
また、これだけではBurstからmanagedなWorldへアクセスできない。

### EntityをIDだけに置換し、操作をWorld APIへ移す

`Entity`そのものを`Index + Version`へ縮小し、すべてを`world.Add(entity, component)`形式にする案。
内部表現は単純になるが、既存APIを大きく破壊し、LitheEcsが重視する自然な利用感を失う。
明確な性能またはBurst要件なしには採用しない。

## 採用判断に必要な具体的ユースケース

抽象的に「unmanagedにできる」ことだけを目的に変更しない。少なくとも次を先に確定する。

- Unity Burst/Jobから必要なのはEntityの列挙、Componentの読み書き、構造変更の記録のどこまでか。
- Jobの実行中にWorldの構造変更を禁止するか、スナップショットまたはCommand Bufferを使うか。
- Entity IDをJobの外へ永続保持する必要があるか。
- 複数Worldを同じJobまたはコンテナで扱う必要があるか。
- UnityのNativeArray等へ格納すること自体が目的か、BurstでComponent処理することが目的か。

## 現時点の結論

既存`Entity`のunmanaged化はWorld解決と寿命管理を複雑化し、それだけではBurst/JobからECSを
操作できるようにならない。そのため現行`Entity`は変更せず、Worldローカルな`EntityId`を併設した。

将来、具体的なJob/Burstユースケースが決まった場合は、`EntityId`とJob向けStorage Viewを
組み合わせる案を第一候補として検証する。
公開APIへ採用する前に、正しさのテストに加えてEntity操作、Query、World生成、Job処理のベンチマークを行う。

# LitheEcs APIリファレンス

[English](API.md) | 日本語

最終更新: 2026-09-03  
対象: `LitheEcs` (`netstandard2.1`)

この文書は、利用者が現在の公開APIと設計上の制約を把握するためのリファレンスです。実装の正本は `LitheEcs/LitheEcs.cs` と `LitheEcs.SourceGenerator/QueryGenerator.cs` です。

## 1. 基本方針

- Managed C#のArchetype ECS。
- ComponentとRelationは`struct`。
- ライブラリ組み込みのSystemやSchedulerは提供しない。利用者がQueryから自由に処理を構築する。
- Query列挙中の構造変更には`EntityCommandBuffer`を使う。
- World、Query、Collectorは基本的に単一スレッド利用を前提とする。
- EntityCommandBufferへのコマンド記録は内部でロックされる。
- Component型IDに固定上限はない。型ID 0〜255はインラインBitSet、それ以降は遅延確保のoverflowで扱う。

```csharp
using LitheEcs;

public struct Position
{
    public float X, Y, Z;
}

public struct Velocity
{
    public float X, Y, Z;
}
```

## 2. World

### 生成と破棄

```csharp
using var world = new World();
using var reservedWorld = new World(defaultCapacity: 10_000);
```

`defaultCapacity`はEntityの初期容量です。負の値を指定すると`ArgumentOutOfRangeException`が発生します。`Dispose()`後にWorldを操作すると、原則として`ObjectDisposedException`が発生します。

### Entityの生成

```csharp
Entity entity = world.Spawn();
```

一括生成:

```csharp
var entities = new Entity[1000];
world.SpawnBatch(entities.Length, entities);
```

`resultEntities`が生成数より短い場合、格納可能な範囲だけEntityが書き込まれます。

### Despawn

```csharp
world.Despawn(entity);
world.DespawnBatch(entities);
```

EntityをDespawnするとVersionが更新され、それ以前のハンドルは無効になります。そのEntityが保持するComponentとBinding、Source・Target両方向のRelationも削除されます。別のWorldに属するEntityや、既に無効になったEntityを指定した場合、`Despawn`は何も行いません。

### Alive判定

```csharp
bool alive = world.IsAlive(entity);
bool same = entity.IsAlive;
```

Alive判定にはWorld参照、Index、Versionが使われます。別Worldの同じIndex/Versionが誤認されることはありません。

## 3. Entity

`Entity`は次を保持する`readonly struct`です。

```csharp
entity.Index;
entity.Version;
entity.World;
entity.Id;
```

同一性は`Index + Version + World`です。

`Entity.Id`はWorldへの参照を含まない`unmanaged`な識別子です。同じWorld内でEntityを識別するときに使用します。

```csharp
EntityId id = entity.Id;

bool alive = world.IsAlive(id);
if (world.TryGetEntity(id, out Entity resolved))
{
    // resolvedは指定したworldに属するEntity
}
```

`EntityId`は`Index + Version`のみを保持します。異なるWorld間での比較や受け渡し、永続化には使用しないでください。`default(EntityId)`は常に無効で、EntityのVersionは1から始まります。

### Component操作

```csharp
entity.Add(new Position { X = 1 });
entity.Add<Disabled>();

bool has = entity.Has<Position>();
ref Position position = ref entity.Get<Position>();
bool removed = entity.Remove<Position>();

if (entity.TryGetRef<Position>(out var positionRef))
    positionRef.Value.X += 1;
```

実行時Validationが有効な構成では、`TryGetRef<T>()`で取得した`Ref<T>`を、同じ型のComponentに構造変更が発生した後で使用すると`InvalidOperationException`が発生します。`Ref<T>`は一時的な参照です。Componentの追加・削除やEntityのDespawnなど、Worldの構造を変更する前に使い切ってください。構造変更後に必要な場合は、`TryGetRef<T>()`で取得し直します。

World経由でも同じ操作ができます。

```csharp
world.AddComponent(entity, new Position());
world.AddComponentBatch(entities, new Position());
ref Position position = ref world.GetComponent<Position>(entity);
bool has = world.HasComponent<Position>(entity);
bool removed = world.RemoveComponent<Position>(entity);
```

`AddComponent`で既に存在する型を指定した場合は、Componentの値を上書きします。Entityが保持していないComponentを`Get`で取得すると`KeyNotFoundException`が発生します。

### 表示

```csharp
entity.ToString();
// Entity(Index: 12, Version: 3, World: 01A2B3C4)

default(Entity).ToString();
// Entity(None)
```

## 4. Query

### 1 Component

```csharp
foreach (ref var position in world.Query<Position>())
{
    position.X += 1;
}
```

特定Entityの一致判定:

```csharp
bool matches = world.Query<Position>().Matches(entity);
```

Delegate形式:

```csharp
world.Query<Position>().ForEach(
    (in Entity entity, ref Position position) =>
    {
        position.X += 1;
    });
```

`IQueryAction<T1>`を実装したstructを`ForEach(ref action)`へ渡すこともできます。

### 2 Components

```csharp
foreach (var (position, velocity) in world.Query<Position, Velocity>())
{
    position.Value.X += velocity.Value.X;
}
```

`RefItem<T>.Value`はComponentへの`ref`を返します。


Callback形式:

```csharp
world.Query<Position, Velocity>()
    .ForEach((in Entity entity, ref Position position, ref Velocity velocity) =>
    {
        position.X += velocity.X;
    });
```

### Filter

Filter APIはすべてのQuery（1～8コンポーネント）で利用できます。

```csharp
var query = world.Query<Position, Velocity>()
    .With<Player>()
    .Without<Disabled>()
    .Any<Grounded, Flying>();
```

意味:

```text
Position
AND Velocity
AND Player
AND NOT Disabled
AND (Grounded OR Flying)
```

複数の`With`と`Without`は連結できます。`Any<T1,T2>()`を複数回呼ぶと、指定型が同じAny集合へ追加されます。

特定EntityだけをFilter判定できます。

```csharp
if (query.Matches(entity))
{
    // 現在のComponent構成がQuery条件に一致
}
```

`Matches()`は、指定したEntityが現在のQuery条件に一致するかを判定します。

### 3～4 Components

```csharp
foreach (var (position, velocity, acceleration)
         in world.Query<Position, Velocity, Acceleration>())
{
    position.Value.X += velocity.Value.X;
}
```

3～4 Component Queryも`Matches(Entity)`、`With`、`Without`、`Any`を利用できます。

### 5～8 Components

5～8 Component Query、`QueryAction`、`IQueryAction`、WorldのQuery factoryはSource Generatorにより生成されます。

```csharp
foreach (var (a, b, c, d, e) in world.Query<A, B, C, D, E>())
{
}
```

5～8 Component Queryも列挙、Delegate `ForEach`、struct action `ForEach`、`Matches`、Filter APIを提供します。

### struct action

Delegate呼び出しを避けたい場合に使用します。

```csharp
public struct MoveAction : IQueryAction<Position, Velocity>
{
    public float DeltaTime;

    public void Execute(
        in Entity entity,
        ref Position position,
        ref Velocity velocity)
    {
        position.X += velocity.X * DeltaTime;
    }
}

var action = new MoveAction { DeltaTime = deltaTime };
world.Query<Position, Velocity>().ForEach(ref action);
```

`IQueryAction`は2～8 Component向けに存在します。

## 5. JobQueryとUnity Burst

`AsJobQuery()`は、投影するComponent型を`unmanaged`に制限したJob連携用Queryを返します。
1～3 Componentの`JobQuery`では、`AcquireRanges()`を使って複数のRangeをまとめて取得できます。

```csharp
using var ranges = world.Query<Position, Velocity>()
    .AsJobQuery()
    .AcquireRanges();

for (var i = 0; i < ranges.RangeCount; i++)
{
    JobQueryRange<Position, Velocity> range = ranges.GetRange(i);
    Span<Position> positions = range.Components1.Span;
}
```

`AcquireRanges()`の返り値を保持している間は、Worldの構造変更やDisposeを行えません。取得した
`Memory<T>`、Span、pointerは、返り値をDisposeした後に使用しないでください。返り値は`using`または
`finally`で確実にDisposeします。

Unity Packageの`LitheEcs.Unity.Jobs`アセンブリを使うと、1～3 Componentの`JobQuery`を、
GCによって移動しないよう一時的に固定したゼロコピーのviewとしてBurst Jobへ渡せます。
`RunBurst()`はすべてのRangeをScheduleした後、まとめて完了を待ちます。`RunBurstUnsafe()`は
すべてのRangeを作業単位へ展開し、Query全体を1つのJobとして実行します。Component列の固定は、
すべてのJobが完了するまで維持されます。

```csharp
using Unity.Burst;
using LitheEcs.Unity.Jobs;

var movement = new MovementAction { DeltaTime = deltaTime };
world.Query<Position, Velocity>()
    .AsJobQuery()
    .RunBurst(ref movement);

[BurstCompile]
public struct MovementAction : IBurstQueryAction<Position, Velocity>
{
    public float DeltaTime;

    public void Execute(int index, ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X * DeltaTime;
        position.Y += velocity.Y * DeltaTime;
        position.Z += velocity.Z * DeltaTime;
    }
}
```

`RunBurst()`は`NativeArray`のviewとSafetyHandleを使用します。要素単位の読み出しと書き戻しも避けたい場合は、
pointerを直接使用する同期版を明示的に選択できます。

```csharp
world.Query<Position, Velocity>()
    .AsJobQuery()
    .RunBurstUnsafe(ref movement);
```

`RunBurstUnsafe()`は、GCによる移動を防ぐためComponent列を一時的に固定し、そのメモリアドレスから直接`ref`を作ります。1～3 Componentに対応します。
NativeContainerの範囲検査、alias検査、Jobの依存関係検査は行われません。実行中は同じComponent列に別のthreadや
Jobからアクセスしないでください。また、pointerや`JobHandle`を同期呼び出しの外へ持ち出さないでください。

## 6. Managed Parallel Query

1～8 Component Queryは、Worldが遅延生成する常駐ワーカーで対象Entityを並列処理できます。

```csharp
world.Query<Position, Velocity>()
    .Without<Disabled>()
    .AsParallelQuery(minimumEntityCount: 1_024, batchSize: 1_024)
    .Run((positions, velocities, entities) =>
    {
        for (var i = 0; i < positions.Length; i++)
            positions[i].X += velocities[i].X * deltaTime;
    });
```

EntityごとのDelegate呼び出しが不要な処理では、Range APIでComponentのSpanを直接処理できます。

```csharp
world.Query<Position, Velocity>()
    .AsParallelQuery()
    .Run(static (positions, velocities, entities) =>
    {
        for (var i = 0; i < positions.Length; i++)
            positions[i].Value += velocities[i].Value;
    });
```

`EntityRange`はコピーを作らず、必要な場合だけ`entities[index]`からEntityを取得します。
Rangeと各Component Spanの長さおよびインデックスは一致します。
`entities.Offset`はQuery結果全体におけるRangeの先頭indexです。Query上限に合わせた連続配列へは、並列実行順に依存せず次のように書き込めます。

```csharp
world.Query<Position>().AsParallelQuery().Run((positions, entities) =>
{
    for (var i = 0; i < positions.Length; i++)
        output[entities.Offset + i] = positions[i];
});
```

`Offset`はEntity indexではなく、そのQuery実行中だけ有効な連続indexです。次回のQueryでも同じEntityが同じOffsetになる保証はありません。

- callbackごとに、異なるEntityのComponentを処理します。
- 同じWorldでは、複数の`ParallelQuery.Run`を同時に実行したり、処理をネストしたりできません。
- 実行中にSpawn、Despawn、Componentの追加・削除を行うと`InvalidOperationException`が発生します。並列処理内で発生した例外は`AggregateException`にまとめられます。
- `EntityCommandBuffer`は所有thread専用のため、Parallel workerからコマンドを記録できません。構造変更の要求はthread-safeな方法で収集し、`ParallelQuery.Run`の完了後に所有threadから記録して`Playback()`してください。
- callbackから共有状態へアクセスする場合は、利用者側で同期してください。複数のEntityから同じ変数や同じComponentへ同時に書き込まないでください。
- Entity数が`minimumEntityCount`未満の場合は、呼び出し元のthreadで逐次実行します。既定値は4,096です。
- 大きなArchetypeは、`batchSize`単位のEntity範囲に分割します。既定値は4,096です。
- DelegateはEntityごとではなく、Rangeごとに1回呼び出されます。

### Queryの制約

- Queryの列挙中は、EntityのSpawn・DespawnやComponentの追加・削除を行わないでください。必要な構造変更は`EntityCommandBuffer`に記録し、列挙の完了後に`Playback()`で適用します。
- 実行時Validationが有効な構成では、列挙中に構造変更が発生すると、その後の列挙処理で`InvalidOperationException`が発生します。
- `ref`を通して既存のComponent値を変更する操作は、構造変更には該当しません。
- Filter付きQueryは、一致するArchetypeを内部にキャッシュします。Filter条件だけで参照しているComponentが個別に追加・削除された場合は、キャッシュを差分更新します。
- Componentの一括変更、Despawn、取得対象となるComponentの追加・削除が発生した場合は、次回の利用時にキャッシュを再構築します。
- WorldをDisposeした後は、そのWorldから作成したQueryを使用しないでください。

### EntityQueryの結果

Component列ではなくEntityの集合を安定した結果として取得する場合は`EntityQuery.Result()`を
使用します。関連する構造変更が発生すると結果は無効になるため、変更後は`Result()`を再度
呼び出してください。

```csharp
var entityQuery = world.Query()
    .With<Position>()
    .Without<Disabled>()
    .Any<Grounded, Flying>();

EntityQueryResult result = entityQuery.Result();
foreach (var entity in result)
    Process(entity);
```

### Aligned Chunk

FilterなしのQueryについて、一致するデータが最大1つの非Empty Chunkに収まる場合は
`TryGetAlignedChunk()`から整列済みComponent Spanを取得できます。条件を満たさない場合は例外ではなく
`false`を返します。

```csharp
if (world.Query<Position, Velocity>().TryGetAlignedChunk(out var chunk))
{
    Span<Position> positions = chunk.Component1;
    Span<Velocity> velocities = chunk.Component2;
}
```

## 7. EntityCommandBuffer

各Worldは、所有threadから使用する`EntityCommandBuffer`を1つ保持します。同じWorldを使うすべてのSystemで、このインスタンスを共有します。

```csharp
var ecb = world.CommandBuffer;
```

`EntityCommandBuffer`は、それを所有するthread専用です。別のthreadからコマンドを記録したり`Playback()`を呼び出したりすると、`InvalidOperationException`が発生します。現在、複数threadから共有できる`EntityCommandBuffer`は提供していません。

対応コマンド:

```csharp
DeferredEntity deferred = ecb.Spawn();
DeferredEntity initialized = ecb.Spawn(new Position(), new Velocity());
ecb.Despawn(entity);
ecb.AddComponent(entity, new Position());
ecb.AddComponent(entity, new Position(), new Velocity());
ecb.AddComponent(deferred, new Position(), new Velocity());
ecb.AddComponentBatch(entities, new Position());
ecb.RemoveComponent<Position>(entity);
ecb.AddRelation<FriendsWith>(source, target);
```

`Spawn(...)`と`AddComponent(...)`は2～4個のComponentを一度に記録できます。複数Component版はEntityまたはDeferredEntityの検証とECBのロック取得を1回にまとめます。ComponentごとのコマンドとPlayback順序は単数版と同じです。

適用:

```csharp
ecb.Playback();
```

例:

```csharp
var ecb = world.CommandBuffer;

world.Query<Health, Position>()
    .ForEach((in Entity entity, ref Health health, ref Position position) =>
    {
        if (health.Value <= 0)
            ecb.Despawn(entity);
    });

ecb.Playback();
```

重要な仕様:

- `EntityCommandBuffer`は、作成元のWorld以外では使用できません。
- `world.CommandBuffer`は、同じWorldに対して常に同じインスタンスを返します。
- 別のWorldに属するEntityを記録しようとすると、`InvalidOperationException`が発生します。
- コマンドの記録後にEntityが無効になった場合は、`Playback()`時に各World操作の規則に従って処理されます。
- コマンドは記録した順に実行されます。
- 成功・失敗にかかわらず、`Playback()`の終了時に記録済みのコマンドとpayloadは消去されます。
- `Playback()`はトランザクションではありません。途中で失敗しても、適用済みのコマンドは元に戻りません。
- Spawnのcallbackは`Playback()`中に呼び出されます。

## 8. EntityTemplate

同じComponent構成を繰り返し生成するためのPrefab相当です。

```csharp
var template = world.CreateTemplate()
    .Add(new Position { X = 10 })
    .Add(new Velocity { X = 1 })
    .Add(new Health { Value = 100 });
```

単体生成:

```csharp
Entity entity = template.Spawn();
```

一括生成:

```csharp
var entities = new Entity[1000];
template.SpawnBatch(entities);

template.SpawnBatch(count: 1000);
```

同じ型のComponentを再度`Add`すると、Templateに設定されている値を置き換えます。Singleton Componentを複数のEntityへ一括追加することはできません。

## 9. Singleton

Singleton Componentは`ISingleton`を実装します。

```csharp
public struct GameSettings : ISingleton
{
    public float Gravity;
}
```

```csharp
var globals = world.Spawn();
globals.Add(new GameSettings { Gravity = 9.81f });

Entity singleton = world.Singleton<GameSettings>();
ref GameSettings settings = ref singleton.Get<GameSettings>();

bool exists = world.HasSingleton<GameSettings>();
bool found = world.TryGetSingleton<GameSettings>(out Entity entity);
```

同じWorldで同じSingleton型を複数Entityへ追加すると`InvalidOperationException`になります。Singleton Entityは通常Componentを追加で保持できます。

## 10. マネージドオブジェクト

### Link

マネージドオブジェクトをComponentとしてEntityに保持します。

```csharp
var gameObject = new GameObject();
entity.Add(Link.With(gameObject));

GameObject linked = entity.GetLink<GameObject>();
```

内部では`Link<T>` structとして保存されます。Query条件には`Link<GameObject>`を指定します。

```csharp
world.Query<Position, Velocity>()
    .With<Link<GameObject>>();
```

### Bind

外部オブジェクトや値からEntityを逆引きするためのBindingです。classは参照同一性、structは値の等価性で識別します。

```csharp
entity.Bind(gameObject);
// または world.Bind(gameObject, entity);

if (world.TryGetEntity(gameObject, out var boundEntity))
{
}

Entity required = world.GetEntity(gameObject);
entity.Unbind(gameObject);
world.Unbind(gameObject);

entity.Bind(new NetworkId(42));
Entity networkEntity = world.GetEntity(new NetworkId(42));
```

同じ参照、または等しいstruct値を、同じWorldの複数EntityへBindすることはできません。EntityのDespawn時にBindingは自動解除されます。LinkとBindは別の機能です。

## 11. Relation

Relation型は値を持たない型識別子として使う`struct`です。

```csharp
public struct FriendsWith
{
}
```

追加:

```csharp
source.AddRelation<FriendsWith>(target);
// または
world.AddRelation<FriendsWith>(source, target);
```

確認と取得:

```csharp
bool exists = source.HasRelation<FriendsWith>(target);

Entity singleTarget = source.GetRelation<FriendsWith>();
if (source.TryGetRelation<FriendsWith>(out var target))
{
    // Relationがちょうど1件の場合
}

ReadOnlySpan<Entity> targets = source.GetRelations<FriendsWith>();
ReadOnlySpan<Entity> sources = world.GetEntitiesWithTarget<FriendsWith>(target);
```

Targetが1件だけの場合は、`GetRelation<T>()`または`TryGetRelation<T>()`で取得できます。Targetが0件または複数件の場合、`GetRelation<T>()`は例外を投げ、`TryGetRelation<T>()`は`false`を返します。複数のTargetを扱う場合は`GetRelations<T>()`を使用してください。

削除:

```csharp
source.RemoveRelation<FriendsWith>(target);
world.RemoveRelation<FriendsWith>(source, target);
ecb.RemoveRelation<FriendsWith>(source, target);
```

SourceとTargetは、同じWorldに属する有効なEntityでなければなりません。どちらかがDespawnされると、Relationも自動的に削除されます。Relationは通常のComponentとは別に保存されるため、確認には`source.Has<FriendsWith>()`ではなく`source.HasRelation<FriendsWith>(target)`を使用します。

## 12. Reactive EntityCollector

Collectorは現在の状態を表すものではなく、前回`Clear()`してから発生したComponentイベントを収集します。

```csharp
using var collector = world
    .Observe<Health>(ComponentEvent.KeyAdded | ComponentEvent.KeyChanged)
    .Or<Status>(ComponentEvent.KeyRemoved);
```

イベント:

```csharp
ComponentEvent.KeyAdded
ComponentEvent.KeyRemoved
ComponentEvent.KeyChanged
```

収集されたEntityは重複排除されます。

```csharp
foreach (var entity in collector)
{
    if (!entity.IsAlive)
        continue;

    Process(entity);
}

collector.Clear();
```

Query Filterとの組み合わせ:

```csharp
using var collector = world.Observe<Health>(ComponentEvent.KeyChanged);
var filter = world.Query<Health, Player>().Without<Dead>();

foreach (var entity in collector)
{
    if (filter.Matches(entity))
        Process(entity);
}

collector.Clear();
```

重要な仕様:

- `.Or<T>()`を使うと、別のイベントを監視対象に追加できます。
- Collectorには`And`や`Any`に相当する条件指定はありません。
- 同じEntityで複数の対象イベントが発生しても、`Clear()`までは1件だけ保持します。
- `AddComponent`で既存のComponentを上書きすると`KeyChanged`が発行されます。
- Queryや`Get<T>()`から取得した`ref`を通して値を変更しても、`KeyChanged`は発行されません。
- `KeyRemoved`が収集された時点で、対象のComponentは既に削除されています。
- Despawnによる`KeyRemoved`では、収集されたEntityが既に無効になっている場合があります。
- CollectorはComponentの変更前・変更後の値を保存しません。
- `Clear()`は収集したEntityを消去しますが、イベントの監視は継続します。
- `Dispose()`はイベントの監視を解除します。
- WorldをDisposeすると、そのWorldのCollectorも無効になります。

## 13. 構造変更のバッチ処理と診断API

`BeginStructuralBatch()`を使うと、連続する構造変更をまとめて処理できます。変更後の状態をQueryなどから
利用する前に、返されたScopeをDisposeしてください。通常は`using`を使用します。

```csharp
using (world.BeginStructuralBatch())
{
    entity.Add<Position>();
    entity.Add<Velocity>();
    entity.Remove<Disabled>();
}
```

診断機能が有効なDebugビルドでは、Snapshot APIを使ってWorldの状態を調査できます。Snapshotは取得時点の
情報を保持し、Worldの内部Storageを直接参照しません。通常のReleaseビルドには、これらのAPIは含まれません。

```csharp
WorldDiagnosticsSnapshot worldSnapshot = world.CreateDiagnosticsSnapshot();
EntityListDiagnosticsSnapshot entities = world.CreateEntityDiagnosticsSnapshot();
EntityDiagnosticsSnapshot entitySnapshot = world.CreateEntityDiagnosticsSnapshot(entity);

world.ResetAllocationDiagnostics();
AllocationDiagnosticsSnapshot allocations = world.GetAllocationDiagnostics();
string report = world.FormatAllocationDiagnostics(allocations);
```

Snapshotの作成と表示用文字列の生成では、メモリ割り当てが発生する場合があります。実行頻度の高い処理では使用しないでください。

### コンパイルシンボル

実行時Validationと診断機能は、`RELEASE`が定義されていない場合にのみコンパイルされます。個別に無効化する場合は、次のシンボルを定義します。

- `DISABLE_LITHEECS_VALIDATION`は、Queryの構造Version検査と`Ref<T>`の寿命検査を除外します。Entityの生存確認、Worldの所有関係、Dispose済み状態、thread所有権の検査は引き続き行われます。
- `DISABLE_LITHEECS_DIAGNOSTICS`は、診断用のSnapshot型とAPI、allocation counter、および関連する追跡処理を除外します。

これらは、`RELEASE`を定義しない環境で実行時の負荷を抑えたい場合に使用します。LitheEcsのSourceをコンパイルするすべてのAssemblyに同じシンボルを設定してください。

## 14. エラーと寿命

- 別のWorldに属するEntityをComponent、Relation、Bindingの操作へ渡すと、多くのAPIで`InvalidOperationException`が発生します。
- `default(Entity)`はWorldを保持しないため、`Entity`のインスタンスAPIを呼ぶと`InvalidOperationException`が発生します。
- Dispose済みのWorldを操作すると`ObjectDisposedException`が発生します。
- Query、`EntityCommandBuffer`、Collector、Entityは、所属するWorldをDisposeした後に使用しないでください。
- `ReadOnlySpan<Entity>`やQueryから取得した`ref`は、参照元のStorageに構造変更が発生する前に使い終えてください。

## 15. 検証

```powershell
dotnet test LitheEcs.sln --no-restore
dotnet build LitheEcsBenchmark/LitheEcsBenchmark.csproj -c Release --no-restore
```

BenchmarkDotNetの結果は`BenchmarkDotNet.Artifacts/results/`へ出力されます。
